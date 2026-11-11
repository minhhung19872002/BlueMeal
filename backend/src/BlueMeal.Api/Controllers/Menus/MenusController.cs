using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Engines;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Engines;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Menus;

/// <summary>Menus: list, detail, manual creation, live computation, approval workflow, copy, generation (SPEC §7.4).</summary>
[Route("api/v1/menus")]
public sealed class MenusController(AppDbContext db, MenuService menus, EngineDataService engine, ISettingsService settings, ICacheService cache, IBackgroundJobs jobs) : ApiControllerBase
{
    public sealed class MenuQuery : PageQuery
    {
        public DateOnly? From { get; set; }
        public DateOnly? To { get; set; }
        public Guid? MealShiftId { get; set; }
        public Guid? SubjectGroupId { get; set; }
        public Guid? CustomerId { get; set; }
        public Guid? PlanId { get; set; }
        public Guid? OwnerUserId { get; set; }
        public MenuStatus? Status { get; set; }
        public bool PendingMyApproval { get; set; }
    }

    private bool CanSeeCost => CurrentUser.Has(Permissions.Menu.ViewCost) || CurrentUser.Has(Permissions.Cost.View);

    /// <summary>Data scope: customer representatives only see their customers' published menus.</summary>
    private IQueryable<Menu> Scoped()
    {
        var q = db.Set<Menu>().AsNoTracking();
        if (CurrentUser.CustomerScope is { } scope) q = q.Where(m => m.CustomerId != null && scope.Contains(m.CustomerId.Value));
        if (!CurrentUser.Has(Permissions.Menu.Create) && !CurrentUser.Has(Permissions.Menu.Approve) && !CurrentUser.Has(Permissions.Menu.ApproveFinal))
            q = q.Where(m => m.Status == MenuStatus.Published);
        return q;
    }

    private async Task<Menu> LoadAsync(Guid id, CancellationToken ct)
    {
        var scoped = await Scoped().AnyAsync(m => m.Id == id, ct);
        if (!scoped) throw new NotFoundException("menu");
        return await db.Set<Menu>().Include(m => m.Items).FirstAsync(m => m.Id == id, ct);
    }

    [HttpGet, HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] MenuQuery q, CancellationToken ct)
    {
        var query = Scoped();
        if (q.From is not null) query = query.Where(m => m.MenuDate >= q.From);
        if (q.To is not null) query = query.Where(m => m.MenuDate <= q.To);
        if (q.MealShiftId is not null) query = query.Where(m => m.MealShiftId == q.MealShiftId);
        if (q.SubjectGroupId is not null) query = query.Where(m => m.SubjectGroupId == q.SubjectGroupId);
        if (q.CustomerId is not null) query = query.Where(m => m.CustomerId == q.CustomerId);
        if (q.PlanId is not null) query = query.Where(m => m.PlanId == q.PlanId);
        if (q.OwnerUserId is not null) query = query.Where(m => m.OwnerUserId == q.OwnerUserId);
        if (q.Status is not null) query = query.Where(m => m.Status == q.Status);
        if (q.PendingMyApproval)
        {
            var levels = (await settings.GetAsync<ApprovalSettings>(SettingKeys.Approval, ct)).Levels;
            var canL1 = CurrentUser.Has(Permissions.Menu.Approve);
            var canFinal = CurrentUser.Has(Permissions.Menu.ApproveFinal);
            query = query.Where(m => m.Status == MenuStatus.Submitted && ((levels < 2 && canL1) || (levels >= 2 && ((m.ApprovalLevel == 0 && canL1) || (m.ApprovalLevel == 1 && canFinal)))));
        }
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(m => EF.Functions.ILike(m.Code, "%" + q.Q.Trim() + "%"));
        var total = await query.LongCountAsync(ct);
        query = q.Sort switch { "menuDate" => query.OrderBy(m => m.MenuDate), "code" => query.OrderBy(m => m.Code), _ => query.OrderByDescending(m => m.MenuDate).ThenBy(m => m.MealShift!.SortOrder) };
        var canCost = CanSeeCost;
        var rows = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(m => new
        {
            m.Id, m.Code, m.MenuDate, m.MealShiftId, shift = m.MealShift!.NameVi, shiftEn = m.MealShift.NameEn, m.SubjectGroupId, subject = m.SubjectGroup!.NameVi,
            m.CustomerId, customer = m.Customer!.NameVi, m.Servings, creationMode = m.CreationMode.ToString(), status = m.Status.ToString(), m.ApprovalLevel,
            m.EnergyKcal, costPerServing = canCost ? m.CostPerServing : null, m.NutritionScore, m.CriticalWarnings, m.OwnerUserId, m.PlanId,
            dishes = m.Items.OrderBy(i => i.SortOrder).Select(i => new { i.DishId, i.Dish!.NameVi, i.Dish.NameEn, type = i.Dish.DishType.ToString() }).ToList(), m.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        var dishIds = m.Items.Select(i => i.DishId).ToList();
        var dishes = await db.Set<Dish>().AsNoTracking().IgnoreQueryFilters().Where(d => dishIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var approvals = await db.Set<MenuApproval>().AsNoTracking().Where(a => a.MenuId == id).OrderBy(a => a.At).ToListAsync(ct);
        var shift = await db.Set<MealShift>().AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == m.MealShiftId, ct);
        JsonElement? snapshot = m.Snapshot is null ? null : JsonSerializer.Deserialize<JsonElement>(m.Snapshot);
        object? snapshotOut = snapshot;
        if (snapshot is { } s && !CanSeeCost)
        {
            // hide cost for users without cost permission (published menu for workers/customers)
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(s.GetRawText())!;
            dict.Remove("cost");
            if (dict.TryGetValue("warnings", out var w)) dict["warnings"] = JsonSerializer.SerializeToElement(w.EnumerateArray().Where(x => x.GetProperty("code").GetString() != WarningCodes.CostOver));
            snapshotOut = dict;
        }
        return Ok(new
        {
            m.Id, m.Code, m.PlanId, m.MenuDate, m.MealShiftId, shift = shift?.NameVi, shiftEn = shift?.NameEn, m.SubjectGroupId, m.CustomerId, m.RecommendationId, m.StructureTemplateId,
            m.Servings, budgetPerServing = CanSeeCost ? m.BudgetPerServing : null, creationMode = m.CreationMode.ToString(), status = m.Status.ToString(), m.ApprovalLevel,
            m.NutritionScore, m.EnergyKcal, costPerServing = CanSeeCost ? m.CostPerServing : null, m.CriticalWarnings, m.CriticalOverrideReason, m.CriticalOverrideBy,
            m.Note, m.OwnerUserId, m.PublishedAt, m.Version,
            explanation = m.GeneratorExplanation is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(m.GeneratorExplanation),
            items = m.Items.OrderBy(i => i.SortOrder).Select(i => new
            {
                i.Id, i.DishId, dishCode = dishes.GetValueOrDefault(i.DishId)?.Code, dishNameVi = dishes.GetValueOrDefault(i.DishId)?.NameVi, dishNameEn = dishes.GetValueOrDefault(i.DishId)?.NameEn,
                dishType = dishes.GetValueOrDefault(i.DishId)?.DishType.ToString(), cookingMethod = dishes.GetValueOrDefault(i.DishId)?.CookingMethod.ToString(),
                i.RecipeId, i.ServingFactor, slot = i.Slot.ToString(), i.SortOrder,
            }),
            snapshot = snapshotOut,
            approvals = approvals.Select(a => new { a.Id, action = a.Action.ToString(), a.Level, a.UserName, a.At, a.Comment, from = a.FromStatus.ToString(), to = a.ToStatus.ToString() }),
        });
    }

    /// <summary>Live computation for the manual builder (SPEC §7.4.4): nutrition, % compliance, cost, colored warnings.</summary>
    [HttpPost("compute"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> Compute([FromBody] MenuInput input, [FromQuery] Guid? menuId, CancellationToken ct)
    {
        var snap = await menus.ComputeAsync(input.MenuDate, input.SubjectGroupId, input.CustomerId, input.RecommendationId, input.StructureTemplateId, input.Servings,
            input.BudgetPerServing, input.Items.Select(i => (i.DishId, i.ServingFactor <= 0 ? 1 : i.ServingFactor, (Guid?)null)).ToList(), menuId, ct);
        return Ok(CanSeeCost ? snap : snap with { Cost = null! });
    }

    [HttpPost, HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> Create([FromBody] MenuInput input, CancellationToken ct)
    {
        var m = await menus.CreateAsync(input, MenuCreationMode.Manual, null, ct);
        return Ok(new { m.Id, m.Code });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.Menu.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MenuInput input, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await menus.UpdateAsync(m, input, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.Menu.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        if (m.Status != MenuStatus.Draft) throw new BusinessRuleException("menu.lockedApproved");
        db.Remove(m);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record CopyRequest(DateOnly MenuDate, Guid? MealShiftId, Guid? CustomerId, Guid? SubjectGroupId);

    [HttpPost("{id:guid}/copy"), HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> Copy(Guid id, [FromBody] CopyRequest req, CancellationToken ct)
    {
        var src = await LoadAsync(id, ct);
        var input = new MenuInput(req.MenuDate, req.MealShiftId ?? src.MealShiftId, req.SubjectGroupId ?? src.SubjectGroupId, req.CustomerId ?? src.CustomerId, src.RecommendationId,
            src.StructureTemplateId, src.Servings, src.BudgetPerServing, src.Note, src.PlanId, src.Items.OrderBy(i => i.SortOrder).Select(i => new MenuItemInput(i.DishId, i.ServingFactor, i.Slot)).ToList());
        var m = await menus.CreateAsync(input, MenuCreationMode.Copied, null, ct);
        return Ok(new { m.Id, m.Code });
    }

    [HttpPost("{id:guid}/recompute"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> Recompute(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await menus.RefreshSnapshotAsync(m, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- workflow

    public sealed record ActionRequest(string? Comment);

    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.Menu.Submit)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] ActionRequest req, CancellationToken ct) { await menus.SubmitAsync(await LoadAsync(id, ct), req.Comment, ct); return NoContent(); }

    /// <summary>Level-1 approval needs menu.approve; final approval (2-level mode) needs menu.approve_final — checked in the service.</summary>
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ActionRequest req, CancellationToken ct)
    {
        if (!CurrentUser.Has(Permissions.Menu.Approve) && !CurrentUser.Has(Permissions.Menu.ApproveFinal)) throw new ForbiddenException();
        await menus.ApproveAsync(await LoadAsync(id, ct), req.Comment, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ActionRequest req, CancellationToken ct)
    {
        if (!CurrentUser.Has(Permissions.Menu.Approve) && !CurrentUser.Has(Permissions.Menu.ApproveFinal)) throw new ForbiddenException();
        await menus.RejectOrReturnAsync(await LoadAsync(id, ct), true, req.Comment, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/return"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ActionRequest req, CancellationToken ct)
    {
        if (!CurrentUser.Has(Permissions.Menu.Approve) && !CurrentUser.Has(Permissions.Menu.ApproveFinal)) throw new ForbiddenException();
        await menus.RejectOrReturnAsync(await LoadAsync(id, ct), false, req.Comment, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish"), HasPermission(Permissions.Menu.Publish)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) { await menus.PublishAsync(await LoadAsync(id, ct), ct); return NoContent(); }

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Menu.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, [FromBody] ActionRequest req, CancellationToken ct) { await menus.CancelAsync(await LoadAsync(id, ct), req.Comment, ct); return NoContent(); }

    [HttpPost("{id:guid}/recall"), HasPermission(Permissions.Menu.Recall)]
    public async Task<IActionResult> Recall(Guid id, [FromBody] ActionRequest req, CancellationToken ct) { await menus.RecallAsync(await LoadAsync(id, ct), req.Comment, ct); return NoContent(); }

    public sealed record BulkRequest(Guid[] Ids, string Action, string? Comment);

    /// <summary>Bulk approve/publish (e.g. a whole week) — SPEC §7.4.5. Returns per-menu outcome.</summary>
    [HttpPost("bulk"), HasPermission(Permissions.Self)]
    public async Task<IActionResult> Bulk([FromBody] BulkRequest req, CancellationToken ct)
    {
        var results = new List<object>();
        foreach (var id in req.Ids.Distinct().Take(200))
        {
            try
            {
                var m = await LoadAsync(id, ct);
                switch (req.Action)
                {
                    case "approve" when CurrentUser.Has(Permissions.Menu.Approve) || CurrentUser.Has(Permissions.Menu.ApproveFinal): await menus.ApproveAsync(m, req.Comment, ct); break;
                    case "publish" when CurrentUser.Has(Permissions.Menu.Publish): await menus.PublishAsync(m, ct); break;
                    case "submit" when CurrentUser.Has(Permissions.Menu.Submit): await menus.SubmitAsync(m, req.Comment, ct); break;
                    default: throw new ForbiddenException();
                }
                results.Add(new { id, ok = true });
            }
            catch (AppException e)
            {
                db.ChangeTracker.Clear();
                results.Add(new { id, ok = false, error = L.T(e.Key, e.Args) });
            }
        }
        return Ok(results);
    }

    // ---------------------------------------------------------------- calendar helpers

    public sealed record MoveItemRequest(Guid FromMenuId, Guid ItemId, Guid ToMenuId);

    /// <summary>Drag and drop a dish between two editable menus of the calendar.</summary>
    [HttpPost("move-item"), HasPermission(Permissions.Menu.Edit)]
    public async Task<IActionResult> MoveItem([FromBody] MoveItemRequest req, CancellationToken ct)
    {
        var from = await LoadAsync(req.FromMenuId, ct);
        var to = await LoadAsync(req.ToMenuId, ct);
        MenuService.EnsureEditable(from);
        MenuService.EnsureEditable(to);
        var item = from.Items.FirstOrDefault(i => i.Id == req.ItemId) ?? throw new NotFoundException("menu item");
        from.Items.Remove(item);
        db.Remove(item);
        to.Items.Add(new MenuItem { MenuId = to.Id, DishId = item.DishId, ServingFactor = item.ServingFactor, Slot = item.Slot, SortOrder = to.Items.Count + 1 });
        await menus.RefreshSnapshotAsync(from, ct);
        await menus.RefreshSnapshotAsync(to, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Top 5 alternatives for one dish of a menu that improve the score (SPEC §7.4.3 "đổi món").</summary>
    [HttpGet("{id:guid}/suggestions"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> Suggestions(Guid id, [FromQuery] Guid itemId, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        var idx = m.Items.OrderBy(i => i.SortOrder).ToList().FindIndex(i => i.Id == itemId);
        if (idx < 0) throw new NotFoundException("menu item");
        var items = m.Items.OrderBy(i => i.SortOrder).ToList();
        var slotType = (await db.Set<Dish>().AsNoTracking().FirstAsync(d => d.Id == items[idx].DishId, ct)).DishType;
        var poolIds = await db.Set<Dish>().AsNoTracking().Where(d => d.IsActive && d.DishType == slotType && d.Recipes.Any(r => r.IsCurrent)).Select(d => d.Id).Take(500).ToListAsync(ct);
        var all = await engine.DishesAsync(poolIds.Concat(items.Select(i => i.DishId)), null, ct);
        var ne = await engine.NutritionEngineAsync(ct);
        DishCandidate Cand(Guid dishId, decimal factor)
        {
            var (dish, loss) = all[dishId];
            var n = ne.ComputeDish(dish, factor, loss);
            return new DishCandidate(dish, n.Nutrients, n.AnimalProteinG, CostEngine.DishCost(dish, factor).Cost, n.MissingFoodIngredients.Count > 0);
        }
        var current = items.Where(i => all.ContainsKey(i.DishId)).Select(i => Cand(i.DishId, i.ServingFactor)).ToList();
        var ctx = await menus.ContextAsync(m.SubjectGroupId, m.CustomerId, m.RecommendationId, m.BudgetPerServing, ct);
        var target = await engine.TargetAsync(ctx.RecommendationId, m.BudgetPerServing, ct) ?? throw new BusinessRuleException("menu.noRecommendation");
        var gen = new MenuGenerator(await settings.GetAsync<GeneratorSettings>(SettingKeys.Generator, ct));
        var suggestions = gen.SwapSuggestions(current, idx, poolIds.Where(all.ContainsKey).Select(p => Cand(p, 1)).ToList(), target, new HashSet<Guid>());
        var baseScore = gen.Score(current, target, new HashSet<Guid>()).Total;
        return Ok(suggestions.Select(s => new { s.Dish.Dish.DishId, s.Dish.Dish.Code, s.Dish.Dish.Name, s.Dish.Dish.NameEn, energy = s.Dish.Nutrients.GetValueOrDefault("ENERGY"), cost = CanSeeCost ? s.Dish.Cost : (decimal?)null, score = s.Score.Total, improvement = baseScore - s.Score.Total }));
    }

    // ---------------------------------------------------------------- automatic generation (SPEC §7.4.3, §6.4)

    public sealed record GenerateBody(GenerateRequest Request, bool Save, Dictionary<DateOnly, int>? Chosen);

    [HttpPost("generate"), HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> Generate([FromBody] GenerateBody body, CancellationToken ct)
    {
        var r = body.Request;
        var dayCount = r.ToDate.DayNumber - r.FromDate.DayNumber + 1;
        // heavy requests (> 3 shifts or a whole month with several shifts) run as a background job
        if (body.Save && r.MealShiftIds.Count > 3)
        {
            var jobId = Guid.NewGuid().ToString("N");
            await cache.SetAsync("gen:" + jobId, new GenerationStatus("Queued", null, null), TimeSpan.FromHours(2), ct);
            await jobs.EnqueueAsync(JobNames.GenerateMenus, new Dictionary<string, string> { ["id"] = jobId, ["user"] = UserId.ToString(), ["body"] = JsonSerializer.Serialize(body, MenuService.Json) }, ct);
            return Accepted(new { jobId, days = dayCount });
        }
        var (result, candidates, recId) = await menus.GenerateAsync(r, ct);
        if (body.Save)
        {
            var ids = await menus.SaveGeneratedAsync(r, result, recId, body.Chosen, ct);
            return Ok(new { saved = ids, problems = result.Problems.Select(Problem), result.ElapsedMs, result.Seed });
        }
        var canCost = CanSeeCost;
        object Prop(MenuProposal p) => new
        {
            dishes = p.Dishes.Select(d => new { d.Dish.DishId, d.Dish.Code, d.Dish.Name, d.Dish.NameEn, type = d.Dish.Type.ToString(), method = d.Dish.Method.ToString(), energy = d.Nutrients.GetValueOrDefault("ENERGY"), cost = canCost ? d.Cost : (decimal?)null }),
            score = p.Score, p.EnergyKcal, costPerServing = canCost ? p.CostPerServing : (decimal?)null, p.ProteinPct, p.LipidPct, p.GlucidPct, p.AnimalProteinPct,
        };
        return Ok(new
        {
            days = result.Days.Select(d => new { d.Date, best = Prop(d.Best), alternatives = d.Alternatives.Select(Prop), d.ExplanationVi, d.ExplanationEn }),
            problems = result.Problems.Select(Problem), result.ElapsedMs, result.Seed, candidateCount = candidates.Count, recommendationId = recId,
        });
    }

    public sealed record GenerationStatus(string Status, IReadOnlyList<Guid>? MenuIds, string? Error);

    [HttpGet("generate/{jobId}"), HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> GenerationJob(string jobId, CancellationToken ct)
        => Ok(await cache.GetAsync<GenerationStatus>("gen:" + jobId, ct) ?? throw new NotFoundException("job"));

    private static string Problem(string p)
    {
        var parts = p.Split('|');
        return L.Has(parts[0]) ? L.T(parts[0], parts.Skip(1).Cast<object>().ToArray()) : p;
    }

    // ---------------------------------------------------------------- published menus (SPEC §7.4.6)

    /// <summary>Published weekly menu for customers/workers: dishes and energy only, cost hidden.</summary>
    [HttpGet("published"), HasPermission(Permissions.Menu.View)]
    public async Task<IActionResult> Published([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? customerId, [FromQuery] Guid? subjectGroupId, CancellationToken ct)
    {
        if (to.DayNumber - from.DayNumber > 62) throw new AppException("error.validation");
        var q = Scoped().Where(m => m.Status == MenuStatus.Published && m.MenuDate >= from && m.MenuDate <= to);
        if (customerId is not null) q = q.Where(m => m.CustomerId == customerId);
        if (subjectGroupId is not null) q = q.Where(m => m.SubjectGroupId == subjectGroupId);
        var rows = await q.OrderBy(m => m.MenuDate).ThenBy(m => m.MealShift!.SortOrder).Select(m => new
        {
            m.Id, m.Code, m.MenuDate, shift = m.MealShift!.NameVi, shiftEn = m.MealShift.NameEn, shiftOrder = m.MealShift.SortOrder, customer = m.Customer!.NameVi, subject = m.SubjectGroup!.NameVi,
            m.EnergyKcal, dishes = m.Items.OrderBy(i => i.SortOrder).Select(i => new { i.Dish!.NameVi, i.Dish.NameEn, type = i.Dish.DishType.ToString() }).ToList(),
        }).ToListAsync(ct);
        var org = await db.Set<Organization>().AsNoTracking().Select(o => new { o.Name, o.NameEn, o.LogoFileId }).FirstOrDefaultAsync(ct);
        return Ok(new { organization = org, from, to, menus = rows });
    }
}

/// <summary>Background generation job for large requests (> 3 shifts).</summary>
public sealed class GenerateMenusJob(MenuService menus, ICacheService cache, BlueMeal.Api.Infrastructure.HttpCurrentUser user) : IAppJob
{
    public string Name => JobNames.GenerateMenus;

    public async Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct)
    {
        var id = data["id"];
        user.OverrideUserId = Guid.TryParse(data.GetValueOrDefault("user"), out var u) ? u : null;
        await cache.SetAsync("gen:" + id, new MenusController.GenerationStatus("Running", null, null), TimeSpan.FromHours(2), ct);
        try
        {
            var body = JsonSerializer.Deserialize<MenusController.GenerateBody>(data["body"], MenuService.Json)!;
            var ids = new List<Guid>();
            // one generation per shift with a different seed, each saved as Draft menus
            var i = 0;
            foreach (var shift in body.Request.MealShiftIds)
            {
                var r = body.Request with { MealShiftIds = [shift], Seed = (body.Request.Seed ?? 1) + i++ };
                var (result, _, rec) = await menus.GenerateAsync(r, ct);
                ids.AddRange(await menus.SaveGeneratedAsync(r, result, rec, body.Chosen, ct));
            }
            await cache.SetAsync("gen:" + id, new MenusController.GenerationStatus("Succeeded", ids, null), TimeSpan.FromHours(2), ct);
        }
        catch (Exception ex)
        {
            await cache.SetAsync("gen:" + id, new MenusController.GenerationStatus("Failed", null, ex is AppException ae ? L.T(ae.Key, ae.Args) : ex.Message), TimeSpan.FromHours(2), ct);
            throw;
        }
    }
}
