using System.Text.Json;
using System.Text.Json.Serialization;
using BlueMeal.Api.Catalog;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Engines;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

public sealed record MenuItemInput(Guid DishId, decimal ServingFactor = 1m, DishType? Slot = null);

public sealed record MenuInput(
    DateOnly MenuDate, Guid MealShiftId, Guid? SubjectGroupId, Guid? CustomerId, Guid? RecommendationId, Guid? StructureTemplateId,
    int Servings, decimal? BudgetPerServing, string? Note, Guid? PlanId, IReadOnlyList<MenuItemInput> Items, uint? Version = null);

public sealed record MenuSnapshot(NutritionView Nutrition, CostView Cost, IReadOnlyList<Warning> Warnings, DateTimeOffset ComputedAt, Guid? RecommendationId, decimal? TargetEnergyKcal);

public sealed record GenerateRequest(
    DateOnly FromDate, DateOnly ToDate, IReadOnlyList<Guid> MealShiftIds, Guid? SubjectGroupId, Guid? CustomerId, Guid? RecommendationId, Guid? StructureTemplateId,
    decimal? BudgetPerServing, int? NoRepeatDays, IReadOnlyList<Guid>? RequiredDishIds, IReadOnlyList<Guid>? ExcludedDishIds, IReadOnlyList<string>? Allergens,
    bool PreferExpiringStock, decimal NewDishRatio, int? Seed, int Servings, Guid? PlanId, bool SkipWeekends = true);

/// <summary>Menu use cases: snapshot computation, workflow (SPEC §7.4.5), automatic generation (SPEC §6.4).</summary>
public sealed class MenuService(
    AppDbContext db, EngineDataService engine, ISettingsService settings, ICurrentUser user, INotificationService notifications,
    IAuditWriter audit, IDocNumberGenerator docNumbers, TimeProvider clock)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static readonly MenuStatus[] Editable = [MenuStatus.Draft, MenuStatus.Returned, MenuStatus.Rejected];
    public static readonly MenuStatus[] Official = [MenuStatus.Approved, MenuStatus.Published];

    private DateTimeOffset Now => clock.GetUtcNow();

    // ------------------------------------------------------------------ context helpers

    public async Task<(Guid? RecommendationId, decimal? Budget, decimal? AgreedPrice, IReadOnlyCollection<string> Allergens)> ContextAsync(
        Guid? subjectGroupId, Guid? customerId, Guid? recommendationId, decimal? budget, CancellationToken ct)
    {
        var subject = subjectGroupId is null ? null : await db.Set<SubjectGroup>().AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == subjectGroupId, ct);
        var customer = customerId is null ? null : await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        var rec = recommendationId ?? subject?.DefaultRecommendationId
                  ?? (subjectGroupId is null ? null : await db.Set<Recommendation>().AsNoTracking().Where(r => r.SubjectGroupId == subjectGroupId && r.IsActive).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct));
        var allergens = WarningEngine.Split(subject?.Allergens).Concat(WarningEngine.Split(customer?.Allergens)).Distinct().ToList();
        return (rec, budget, customer?.AgreedPricePerServing, allergens);
    }

    private async Task<HashSet<Guid>> RecentDishesAsync(DateOnly date, Guid? subjectGroupId, Guid? customerId, Guid? excludeMenu, int days, CancellationToken ct)
    {
        if (days <= 0) return [];
        var from = date.AddDays(-days);
        return (await db.Set<Menu>().AsNoTracking()
            .Where(m => m.MenuDate >= from && m.MenuDate < date && m.SubjectGroupId == subjectGroupId && m.CustomerId == customerId && m.Id != excludeMenu
                        && m.Status != MenuStatus.Cancelled && m.Status != MenuStatus.Rejected)
            .SelectMany(m => m.Items.Select(i => i.DishId)).ToListAsync(ct)).ToHashSet();
    }

    /// <summary>Stock shortages for the menu demand (W-STOCK-SHORT): demand vs available quantity across warehouses.</summary>
    private async Task<List<StockShortage>> ShortagesAsync(IReadOnlyList<MenuDish> dishes, int servings, CancellationToken ct)
    {
        if (servings <= 0 || dishes.Count == 0) return [];
        var demand = CostEngine.Demand(dishes, servings);
        var ids = demand.Select(d => d.Ingredient.Id).ToList();
        var stock = await db.Set<StockBalance>().AsNoTracking().Where(b => ids.Contains(b.IngredientId)).GroupBy(b => b.IngredientId)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var pending = await (from l in db.Set<PurchaseRequestLine>().AsNoTracking()
                             join r in db.Set<PurchaseRequest>().AsNoTracking() on l.RequestId equals r.Id
                             where ids.Contains(l.IngredientId) && (r.Status == PurchaseRequestStatus.Approved || r.Status == PurchaseRequestStatus.Ordered)
                             group l by l.IngredientId into g
                             select new { g.Key, Qty = g.Sum(x => x.OrderQty ?? x.SuggestedQty) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        return demand.Where(d => d.PurchaseUnits > stock.GetValueOrDefault(d.Ingredient.Id) + pending.GetValueOrDefault(d.Ingredient.Id))
            .Select(d => new StockShortage(d.Ingredient.Code, d.Ingredient.Name, d.PurchaseUnits, stock.GetValueOrDefault(d.Ingredient.Id) + pending.GetValueOrDefault(d.Ingredient.Id))).ToList();
    }

    // ------------------------------------------------------------------ snapshot

    /// <summary>Computes nutrition, cost, warnings and score for a set of dishes in a menu context (also used for live preview).</summary>
    public async Task<MenuSnapshot> ComputeAsync(DateOnly date, Guid? subjectGroupId, Guid? customerId, Guid? recommendationId, Guid? structureTemplateId,
        int servings, decimal? budget, IReadOnlyList<(Guid DishId, decimal Factor, Guid? RecipeId)> items, Guid? menuId, CancellationToken ct)
    {
        var ctx = await ContextAsync(subjectGroupId, customerId, recommendationId, budget, ct);
        var target = await engine.TargetAsync(ctx.RecommendationId, ctx.Budget, ct);
        var dishes = await engine.DishesAsync(items.Select(i => i.DishId), items.ToDictionary(i => i.DishId, i => i.RecipeId), ct);
        var menuDishes = items.Where(i => dishes.ContainsKey(i.DishId)).Select(i => new MenuDish(dishes[i.DishId].Dish, i.Factor)).ToList();
        var ne = await engine.NutritionEngineAsync(ct);
        var nutrition = NutritionEngine.Summarize(menuDishes.Select(d => ne.ComputeDish(d.Dish, d.ServingFactor, dishes[d.Dish.DishId].CookingLoss)).ToList());
        var cost = CostEngine.ComputeMenu(menuDishes, await engine.OverheadsAsync(ct), Math.Max(1, servings), target?.BudgetPerServing ?? ctx.Budget, ctx.AgreedPrice);
        var ws = await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct);
        var recent = await RecentDishesAsync(date, subjectGroupId, customerId, menuId, ws.RepeatDishDays, ct);
        var warnings = WarningEngine.Evaluate(nutrition, cost, menuDishes, new WarningContext(target, await engine.StructureAsync(structureTemplateId, ct), recent,
            ctx.Allergens, await ShortagesAsync(menuDishes, servings, ct), await engine.NutrientNamesAsync(false, ct)), ws);
        return new MenuSnapshot(nutrition.ToView(target), cost.ToView(), warnings, Now, ctx.RecommendationId, target?.EnergyKcal);
    }

    public async Task RefreshSnapshotAsync(Menu menu, CancellationToken ct)
    {
        var snap = await ComputeAsync(menu.MenuDate, menu.SubjectGroupId, menu.CustomerId, menu.RecommendationId, menu.StructureTemplateId, menu.Servings, menu.BudgetPerServing,
            menu.Items.Select(i => (i.DishId, i.ServingFactor, i.RecipeId)).ToList(), menu.Id, ct);
        menu.Snapshot = JsonSerializer.Serialize(snap, Json);
        menu.RecommendationId ??= snap.RecommendationId;
        menu.EnergyKcal = snap.Nutrition.EnergyKcal;
        menu.CostPerServing = snap.Cost.CostPerServing;
        menu.NutritionScore = snap.Nutrition.Score;
        menu.CriticalWarnings = WarningEngine.CriticalCount(snap.Warnings);
    }

    // ------------------------------------------------------------------ create / update

    public async Task<Menu> CreateAsync(MenuInput input, MenuCreationMode mode, string? explanation, CancellationToken ct)
    {
        await ValidateRefsAsync(input, ct);
        var menu = new Menu
        {
            Code = await docNumbers.NextAsync("TD", input.MenuDate, ct), CreationMode = mode, Status = MenuStatus.Draft, OwnerUserId = user.UserId,
            GeneratorExplanation = explanation,
        };
        Apply(menu, input);
        await RefreshSnapshotAsync(menu, ct);
        db.Add(menu);
        await db.SaveChangesAsync(ct);
        return menu;
    }

    public async Task UpdateAsync(Menu menu, MenuInput input, CancellationToken ct)
    {
        EnsureEditable(menu);
        await ValidateRefsAsync(input, ct);
        if (input.Version is { } v) db.Entry(menu).Property(x => x.Version).OriginalValue = v;
        db.Set<MenuItem>().RemoveRange(menu.Items);
        menu.Items.Clear();
        Apply(menu, input);
        await RefreshSnapshotAsync(menu, ct);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Menu m, MenuInput input)
    {
        m.MenuDate = input.MenuDate; m.MealShiftId = input.MealShiftId; m.SubjectGroupId = input.SubjectGroupId; m.CustomerId = input.CustomerId;
        m.RecommendationId = input.RecommendationId; m.StructureTemplateId = input.StructureTemplateId; m.Servings = Math.Max(0, input.Servings);
        m.BudgetPerServing = input.BudgetPerServing; m.Note = input.Note; m.PlanId = input.PlanId;
        var i = 0;
        foreach (var it in input.Items)
            m.Items.Add(new MenuItem { MenuId = m.Id, DishId = it.DishId, ServingFactor = it.ServingFactor <= 0 ? 1 : it.ServingFactor, Slot = it.Slot ?? DishType.Other, SortOrder = ++i });
    }

    private async Task ValidateRefsAsync(MenuInput input, CancellationToken ct)
    {
        if (!await db.Set<MealShift>().AnyAsync(s => s.Id == input.MealShiftId, ct)) throw new NotFoundException("meal shift");
        var ids = input.Items.Select(i => i.DishId).Distinct().ToList();
        if (await db.Set<Dish>().CountAsync(d => ids.Contains(d.Id), ct) != ids.Count) throw new NotFoundException("dish");
        if (input.CustomerId is { } c && user.CustomerScope is { } scope && !scope.Contains(c)) throw new NotFoundException("customer");
        if (input.Items.Any(i => i.ServingFactor is < 0 or > 10)) throw new AppException("error.validation");
    }

    public static void EnsureEditable(Menu m)
    {
        if (Official.Contains(m.Status)) throw new BusinessRuleException("menu.lockedApproved");
        if (!Editable.Contains(m.Status)) throw new BusinessRuleException("menu.invalidTransition", m.Status.ToString(), MenuStatus.Draft.ToString());
    }

    // ------------------------------------------------------------------ workflow

    private async Task Record(Menu m, ApprovalAction action, MenuStatus from, string? comment, int level, CancellationToken ct)
    {
        db.Add(new MenuApproval { MenuId = m.Id, Action = action, Level = level, UserId = user.UserId ?? Guid.Empty, UserName = user.FullName ?? user.UserName, At = Now, Comment = comment, FromStatus = from, ToStatus = m.Status });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(action switch { ApprovalAction.Approve => AuditAction.Approve, ApprovalAction.Reject => AuditAction.Reject, ApprovalAction.Submit => AuditAction.Submit,
            ApprovalAction.Publish => AuditAction.Publish, ApprovalAction.Recall => AuditAction.Recall, _ => AuditAction.Update }, nameof(Menu), m.Id.ToString(),
            new { status = from.ToString() }, new { status = m.Status.ToString(), comment }, ct: ct);
    }

    private static void Transition(Menu m, MenuStatus to, params MenuStatus[] allowedFrom)
    {
        if (!allowedFrom.Contains(m.Status)) throw new BusinessRuleException("menu.invalidTransition", m.Status.ToString(), to.ToString());
    }

    /// <summary>Submit for approval. Menus with Critical warnings require a reason (override is accepted later by someone with menu.override_critical).</summary>
    public async Task SubmitAsync(Menu m, string? overrideReason, CancellationToken ct)
    {
        Transition(m, MenuStatus.Submitted, Editable);
        await RefreshSnapshotAsync(m, ct);
        if (m.CriticalWarnings > 0)
        {
            if (string.IsNullOrWhiteSpace(overrideReason)) throw new AppException("menu.criticalBlocked", 422, m.CriticalWarnings);
            m.CriticalOverrideReason = overrideReason.Trim();
        }
        var from = m.Status;
        m.Status = MenuStatus.Submitted;
        m.ApprovalLevel = 0;
        await db.SaveChangesAsync(ct);
        await Record(m, ApprovalAction.Submit, from, overrideReason, 0, ct);
        await notifications.NotifyPermissionAsync(Permissions.Menu.Approve, NotificationType.MenuPendingApproval, "notify.menuPending", [m.Code], $"/menus/{m.Id}", ct);
    }

    public async Task ApproveAsync(Menu m, string? comment, CancellationToken ct)
    {
        Transition(m, MenuStatus.Approved, MenuStatus.Submitted);
        var levels = (await settings.GetAsync<ApprovalSettings>(SettingKeys.Approval, ct)).Levels;
        var from = m.Status;
        if (m.CriticalWarnings > 0 && !user.Has(Permissions.Menu.OverrideCritical)) throw new ForbiddenException("menu.criticalBlocked");
        if (m.CriticalWarnings > 0 && m.CriticalOverrideBy is null)
        {
            m.CriticalOverrideBy = user.UserId;
            await Record(m, ApprovalAction.OverrideCritical, from, m.CriticalOverrideReason, m.ApprovalLevel + 1, ct);
        }
        if (levels >= 2 && m.ApprovalLevel == 0)
        {
            if (!user.Has(Permissions.Menu.Approve)) throw new ForbiddenException();
            m.ApprovalLevel = 1; // first level approved, waiting for final approval (A-19)
            await db.SaveChangesAsync(ct);
            await Record(m, ApprovalAction.Approve, from, comment, 1, ct);
            await notifications.NotifyPermissionAsync(Permissions.Menu.ApproveFinal, NotificationType.MenuPendingApproval, "notify.menuPending", [m.Code], $"/menus/{m.Id}", ct);
            return;
        }
        if (levels >= 2 && !user.Has(Permissions.Menu.ApproveFinal)) throw new ForbiddenException();
        m.ApprovalLevel = levels;
        m.Status = MenuStatus.Approved;
        await LockRecipesAsync(m, ct);
        await db.SaveChangesAsync(ct);
        await Record(m, ApprovalAction.Approve, from, comment, levels, ct);
        await NotifyOwner(m, "Đã duyệt / Approved", ct);
    }

    /// <summary>Locks the recipe version used by each dish at approval time (SPEC §5.4 menu_item.recipe_id).</summary>
    private async Task LockRecipesAsync(Menu m, CancellationToken ct)
    {
        if (!db.Entry(m).Collection(x => x.Items).IsLoaded) await db.Entry(m).Collection(x => x.Items).LoadAsync(ct);
        var dishIds = m.Items.Select(i => i.DishId).ToList();
        var current = await db.Set<Recipe>().AsNoTracking().Where(r => dishIds.Contains(r.DishId) && r.IsCurrent).ToDictionaryAsync(r => r.DishId, r => r.Id, ct);
        foreach (var i in m.Items) i.RecipeId ??= current.GetValueOrDefault(i.DishId);
    }

    public async Task RejectOrReturnAsync(Menu m, bool reject, string? comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(comment)) throw new AppException("menu.commentRequired", 422);
        Transition(m, reject ? MenuStatus.Rejected : MenuStatus.Returned, MenuStatus.Submitted);
        var from = m.Status;
        m.Status = reject ? MenuStatus.Rejected : MenuStatus.Returned;
        m.ApprovalLevel = 0;
        m.CriticalOverrideBy = null;
        await db.SaveChangesAsync(ct);
        await Record(m, reject ? ApprovalAction.Reject : ApprovalAction.Return, from, comment, 0, ct);
        await NotifyOwner(m, (reject ? "Bị từ chối / Rejected: " : "Bị trả lại / Returned: ") + comment, ct);
    }

    public async Task PublishAsync(Menu m, CancellationToken ct)
    {
        Transition(m, MenuStatus.Published, MenuStatus.Approved);
        var from = m.Status;
        m.Status = MenuStatus.Published;
        m.PublishedAt = Now;
        await db.SaveChangesAsync(ct);
        await Record(m, ApprovalAction.Publish, from, null, m.ApprovalLevel, ct);
    }

    public async Task CancelAsync(Menu m, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("menu.reasonRequired", 422);
        Transition(m, MenuStatus.Cancelled, MenuStatus.Approved, MenuStatus.Published, MenuStatus.Draft, MenuStatus.Returned, MenuStatus.Rejected);
        var from = m.Status;
        m.Status = MenuStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await Record(m, ApprovalAction.Cancel, from, reason, m.ApprovalLevel, ct);
    }

    /// <summary>Recall an approved/published menu back to Draft (requires menu.recall + reason, audited).</summary>
    public async Task RecallAsync(Menu m, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new AppException("menu.reasonRequired", 422);
        Transition(m, MenuStatus.Draft, MenuStatus.Approved, MenuStatus.Published, MenuStatus.Submitted);
        var from = m.Status;
        m.Status = MenuStatus.Draft;
        m.ApprovalLevel = 0;
        m.PublishedAt = null;
        m.CriticalOverrideBy = null;
        if (!db.Entry(m).Collection(x => x.Items).IsLoaded) await db.Entry(m).Collection(x => x.Items).LoadAsync(ct);
        foreach (var i in m.Items) i.RecipeId = null;
        await db.SaveChangesAsync(ct);
        await Record(m, ApprovalAction.Recall, from, reason, 0, ct);
    }

    private async Task NotifyOwner(Menu m, string result, CancellationToken ct)
    {
        if (m.OwnerUserId is { } owner && owner != user.UserId)
            await notifications.NotifyUsersAsync([owner], NotificationType.MenuApprovalResult, "notify.menuResult", [m.Code, result], $"/menus/{m.Id}", ct);
    }

    // ------------------------------------------------------------------ generation

    public async Task<(GeneratorResult Result, IReadOnlyDictionary<Guid, DishCandidate> Candidates, Guid? RecommendationId)> GenerateAsync(GenerateRequest r, CancellationToken ct)
    {
        if (r.ToDate < r.FromDate || r.ToDate.DayNumber - r.FromDate.DayNumber > 62) throw new AppException("error.validation");
        var gs = await settings.GetAsync<GeneratorSettings>(SettingKeys.Generator, ct);
        var ws = await settings.GetAsync<WarningSettings>(SettingKeys.Warning, ct);
        var ctx = await ContextAsync(r.SubjectGroupId, r.CustomerId, r.RecommendationId, r.BudgetPerServing, ct);
        var target = await engine.TargetAsync(ctx.RecommendationId, r.BudgetPerServing, ct) ?? throw new BusinessRuleException("menu.noRecommendation");
        var structure = await engine.StructureAsync(r.StructureTemplateId, ct);
        if (structure.Count == 0) throw new BusinessRuleException("menu.noStructure");

        // candidates: active dishes with a current recipe; per-serving nutrition and cost precomputed once
        var dishIds = await db.Set<Dish>().AsNoTracking().Where(d => d.IsActive && d.Recipes.Any(x => x.IsCurrent && !x.IsDeleted)).Select(d => d.Id).ToListAsync(ct);
        var dishes = await engine.DishesAsync(dishIds, null, ct);
        var ne = await engine.NutritionEngineAsync(ct);
        var lastUse = await db.Set<Menu>().AsNoTracking().Where(m => m.SubjectGroupId == r.SubjectGroupId && m.MenuDate < r.FromDate && m.Status != MenuStatus.Cancelled)
            .SelectMany(m => m.Items.Select(i => new { i.DishId, m.MenuDate })).GroupBy(x => x.DishId).Select(g => new { g.Key, Last = g.Max(x => x.MenuDate) })
            .ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var expiringIngredients = new HashSet<Guid>();
        if (r.PreferExpiringStock)
        {
            var limit = r.FromDate.AddDays(ws.StockExpiryWarnDays);
            expiringIngredients = (await db.Set<StockLot>().AsNoTracking().Where(l => l.Quantity > 0 && l.ExpiryDate != null && l.ExpiryDate <= limit).Select(l => l.IngredientId).ToListAsync(ct)).ToHashSet();
        }
        var candidates = new Dictionary<Guid, DishCandidate>();
        foreach (var (id, (dish, loss)) in dishes)
        {
            var n = ne.ComputeDish(dish, 1, loss);
            var cost = CostEngine.DishCost(dish).Cost;
            candidates[id] = new DishCandidate(dish, n.Nutrients, n.AnimalProteinG, cost, n.MissingFoodIngredients.Count > 0,
                dish.Lines.Any(l => expiringIngredients.Contains(l.Ingredient.Id)), lastUse.TryGetValue(id, out var last) ? r.FromDate.DayNumber - last.DayNumber : null);
        }

        var noRepeat = r.NoRepeatDays ?? ws.RepeatDishDays;
        var historyFrom = r.FromDate.AddDays(-noRepeat);
        var history = (await db.Set<Menu>().AsNoTracking()
                .Where(m => m.SubjectGroupId == r.SubjectGroupId && m.CustomerId == r.CustomerId && m.MenuDate >= historyFrom && m.MenuDate < r.FromDate && m.Status != MenuStatus.Cancelled && m.Status != MenuStatus.Rejected)
                .Select(m => new { m.MenuDate, Dishes = m.Items.Select(i => i.DishId).ToList() }).ToListAsync(ct))
            .GroupBy(x => x.MenuDate).ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)g.SelectMany(x => x.Dishes).ToList());

        var days = new List<DateOnly>();
        for (var d = r.FromDate; d <= r.ToDate; d = d.AddDays(1))
            if (!r.SkipWeekends || d.DayOfWeek != DayOfWeek.Sunday) days.Add(d);

        var seed = r.Seed ?? HashCode.Combine(r.FromDate.DayNumber, r.SubjectGroupId, r.CustomerId) & 0x7FFFFFFF;
        var gen = new MenuGenerator(gs).Generate(new GeneratorRequest(days, structure, target, candidates.Values.ToList(), noRepeat,
            (r.RequiredDishIds ?? []).ToHashSet(), (r.ExcludedDishIds ?? []).ToHashSet(), (r.Allergens ?? []).Concat(ctx.Allergens).ToList(),
            r.PreferExpiringStock, Math.Clamp(r.NewDishRatio, 0, 1), seed, history));
        return (gen, candidates, ctx.RecommendationId);
    }

    /// <summary>Persists generated proposals as Draft menus (one per day × shift).</summary>
    public async Task<IReadOnlyList<Guid>> SaveGeneratedAsync(GenerateRequest r, GeneratorResult result, Guid? recommendationId, IReadOnlyDictionary<DateOnly, int>? chosenAlternative, CancellationToken ct)
    {
        var ids = new List<Guid>();
        foreach (var shift in r.MealShiftIds.DefaultIfEmpty())
        {
            if (shift == Guid.Empty) continue;
            foreach (var day in result.Days)
            {
                var alt = chosenAlternative?.GetValueOrDefault(day.Date) ?? 0;
                var proposal = alt > 0 && alt <= day.Alternatives.Count ? day.Alternatives[alt - 1] : day.Best;
                var input = new MenuInput(day.Date, shift, r.SubjectGroupId, r.CustomerId, recommendationId, r.StructureTemplateId, r.Servings, r.BudgetPerServing, null, r.PlanId,
                    proposal.Dishes.Select(d => new MenuItemInput(d.Dish.DishId, 1, d.Dish.Type)).ToList());
                var explanation = JsonSerializer.Serialize(new { vi = day.ExplanationVi, en = day.ExplanationEn, seed = result.Seed, score = proposal.Score.Total });
                ids.Add((await CreateAsync(input, MenuCreationMode.Auto, explanation, ct)).Id);
            }
        }
        return ids;
    }
}
