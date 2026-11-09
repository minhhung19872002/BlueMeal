using BlueMeal.Api.Authorization;
using BlueMeal.Api.Services;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Menus;

public sealed record PlanInput(string Name, PlanPeriod Period, DateOnly FromDate, DateOnly ToDate, Guid? CustomerId, Guid? SubjectGroupId, Guid? RecommendationId,
    Guid? StructureTemplateId, IReadOnlyList<Guid>? MealShiftIds, decimal? BudgetPerServing, int? ExpectedServingsPerDay, int NoRepeatDays, PlanStatus Status, string? Note, uint? Version);

public sealed class PlanInputValidator : AbstractValidator<PlanInput>
{
    public PlanInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x).Must(x => x.ToDate >= x.FromDate && x.ToDate.DayNumber - x.FromDate.DayNumber <= 92).WithMessage("error.validation");
        RuleFor(x => x.NoRepeatDays).InclusiveBetween(0, 60);
        RuleFor(x => x.BudgetPerServing).GreaterThanOrEqualTo(0).When(x => x.BudgetPerServing is not null);
        RuleFor(x => x.ExpectedServingsPerDay).GreaterThanOrEqualTo(0).When(x => x.ExpectedServingsPerDay is not null);
    }
}

/// <summary>Weekly/monthly menu plans with a calendar (day × shift), week duplication and multi-customer application (SPEC §7.4.1).</summary>
[Route("api/v1/menu-plans")]
public sealed class MenuPlansController(AppDbContext db, MenuService menus) : ApiControllerBase
{
    private IQueryable<MenuPlan> Scoped()
    {
        var q = db.Set<MenuPlan>().AsNoTracking();
        if (CurrentUser.CustomerScope is { } scope) q = q.Where(p => p.CustomerId != null && scope.Contains(p.CustomerId.Value));
        return q;
    }

    [HttpGet, HasPermission(Permissions.MenuPlan.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] PlanStatus? status, CancellationToken ct)
    {
        var query = Scoped();
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(p => EF.Functions.ILike(EF.Functions.Unaccent(p.Name), EF.Functions.Unaccent("%" + q.Q + "%")));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(p => p.FromDate).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(p => new
        {
            p.Id, p.Name, period = p.Period.ToString(), p.FromDate, p.ToDate, p.CustomerId, customer = db.Set<Customer>().Where(c => c.Id == p.CustomerId).Select(c => c.NameVi).FirstOrDefault(),
            p.SubjectGroupId, subject = db.Set<SubjectGroup>().Where(s => s.Id == p.SubjectGroupId).Select(s => s.NameVi).FirstOrDefault(), status = p.Status.ToString(),
            p.BudgetPerServing, p.ExpectedServingsPerDay, p.NoRepeatDays, menuCount = db.Set<Menu>().Count(m => m.PlanId == p.Id), p.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpGet("{id:guid}"), HasPermission(Permissions.MenuPlan.View)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var p = await Scoped().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        return Ok(new
        {
            p.Id, p.Name, period = p.Period.ToString(), p.FromDate, p.ToDate, p.CustomerId, p.SubjectGroupId, p.RecommendationId, p.StructureTemplateId,
            mealShiftIds = Split(p.MealShiftIds), p.BudgetPerServing, p.ExpectedServingsPerDay, p.NoRepeatDays, status = p.Status.ToString(), p.Note, p.Version,
        });
    }

    private static List<Guid> Split(string? csv) => (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();

    private static void Apply(MenuPlan p, PlanInput i)
    {
        p.Name = i.Name.Trim(); p.Period = i.Period; p.FromDate = i.FromDate; p.ToDate = i.ToDate; p.CustomerId = i.CustomerId; p.SubjectGroupId = i.SubjectGroupId;
        p.RecommendationId = i.RecommendationId; p.StructureTemplateId = i.StructureTemplateId; p.MealShiftIds = i.MealShiftIds is null ? null : string.Join(',', i.MealShiftIds);
        p.BudgetPerServing = i.BudgetPerServing; p.ExpectedServingsPerDay = i.ExpectedServingsPerDay; p.NoRepeatDays = i.NoRepeatDays; p.Status = i.Status; p.Note = i.Note;
    }

    [HttpPost, HasPermission(Permissions.MenuPlan.Create)]
    public async Task<IActionResult> Create([FromBody] PlanInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var p = new MenuPlan { OwnerUserId = CurrentUser.UserId };
        Apply(p, input);
        db.Add(p);
        await db.SaveChangesAsync(ct);
        return Ok(new { p.Id });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.MenuPlan.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] PlanInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var p = await db.Set<MenuPlan>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        if (input.Version is { } v) db.Entry(p).Property(x => x.Version).OriginalValue = v;
        Apply(p, input);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.MenuPlan.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await db.Set<MenuPlan>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        if (await db.Set<Menu>().AnyAsync(m => m.PlanId == id && m.Status != MenuStatus.Draft && m.Status != MenuStatus.Cancelled, ct)) throw new ConflictException("error.inUse");
        await db.Set<Menu>().Where(m => m.PlanId == id && m.Status == MenuStatus.Draft).ExecuteDeleteAsync(ct);
        db.Remove(p);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Calendar grid: each day × shift cell with its menus (status color, energy, dishes).</summary>
    [HttpGet("{id:guid}/calendar"), HasPermission(Permissions.MenuPlan.View)]
    public async Task<IActionResult> Calendar(Guid id, CancellationToken ct)
    {
        var p = await Scoped().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        var shiftIds = Split(p.MealShiftIds);
        var shifts = await db.Set<MealShift>().AsNoTracking().Where(s => shiftIds.Count == 0 || shiftIds.Contains(s.Id)).OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.Code, s.NameVi, s.NameEn }).ToListAsync(ct);
        var canCost = CurrentUser.Has(Permissions.Menu.ViewCost) || CurrentUser.Has(Permissions.Cost.View);
        var cells = await db.Set<Menu>().AsNoTracking().Where(m => m.PlanId == id || (m.MenuDate >= p.FromDate && m.MenuDate <= p.ToDate && m.SubjectGroupId == p.SubjectGroupId && m.CustomerId == p.CustomerId && m.PlanId == null))
            .Where(m => m.Status != MenuStatus.Cancelled)
            .Select(m => new
            {
                m.Id, m.Code, m.MenuDate, m.MealShiftId, status = m.Status.ToString(), m.EnergyKcal, costPerServing = canCost ? m.CostPerServing : null, m.NutritionScore, m.CriticalWarnings, m.Servings,
                items = m.Items.OrderBy(i => i.SortOrder).Select(i => new { i.Id, i.DishId, i.Dish!.NameVi, i.Dish.NameEn, type = i.Dish.DishType.ToString() }).ToList(),
            }).ToListAsync(ct);
        return Ok(new { plan = new { p.Id, p.Name, p.FromDate, p.ToDate, p.SubjectGroupId, p.CustomerId, p.StructureTemplateId, p.RecommendationId, p.BudgetPerServing, p.ExpectedServingsPerDay }, shifts, cells });
    }

    public sealed record CopyWeekRequest(DateOnly SourceWeekStart, DateOnly TargetWeekStart);

    /// <summary>Duplicate one week of menus to another week (Draft copies).</summary>
    [HttpPost("{id:guid}/copy-week"), HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> CopyWeek(Guid id, [FromBody] CopyWeekRequest req, CancellationToken ct)
    {
        var p = await Scoped().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        var src = await db.Set<Menu>().AsNoTracking().Include(m => m.Items)
            .Where(m => m.PlanId == id && m.MenuDate >= req.SourceWeekStart && m.MenuDate < req.SourceWeekStart.AddDays(7) && m.Status != MenuStatus.Cancelled).ToListAsync(ct);
        var offset = req.TargetWeekStart.DayNumber - req.SourceWeekStart.DayNumber;
        var ids = new List<Guid>();
        foreach (var m in src)
        {
            var input = new MenuInput(m.MenuDate.AddDays(offset), m.MealShiftId, m.SubjectGroupId, m.CustomerId, m.RecommendationId, m.StructureTemplateId, m.Servings, m.BudgetPerServing, m.Note, id,
                m.Items.OrderBy(i => i.SortOrder).Select(i => new MenuItemInput(i.DishId, i.ServingFactor, i.Slot)).ToList());
            ids.Add((await menus.CreateAsync(input, MenuCreationMode.Copied, null, ct)).Id);
        }
        return Ok(new { created = ids.Count, ids });
    }

    public sealed record ApplyCustomersRequest(Guid[] CustomerIds);

    /// <summary>Apply the plan's menus to several customers (copies with CustomerId set).</summary>
    [HttpPost("{id:guid}/apply-customers"), HasPermission(Permissions.Menu.Create)]
    public async Task<IActionResult> ApplyCustomers(Guid id, [FromBody] ApplyCustomersRequest req, CancellationToken ct)
    {
        var p = await Scoped().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("plan");
        var src = await db.Set<Menu>().AsNoTracking().Include(m => m.Items).Where(m => m.PlanId == id && m.Status != MenuStatus.Cancelled).ToListAsync(ct);
        var created = 0;
        foreach (var customer in req.CustomerIds.Distinct())
        {
            if (customer == p.CustomerId) continue;
            var c = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == customer, ct) ?? throw new NotFoundException("customer");
            foreach (var m in src)
            {
                var input = new MenuInput(m.MenuDate, m.MealShiftId, m.SubjectGroupId ?? c.DefaultSubjectGroupId, customer, m.RecommendationId, m.StructureTemplateId, m.Servings, m.BudgetPerServing, m.Note, id,
                    m.Items.OrderBy(i => i.SortOrder).Select(i => new MenuItemInput(i.DishId, i.ServingFactor, i.Slot)).ToList());
                await menus.CreateAsync(input, MenuCreationMode.Copied, null, ct);
                created++;
            }
        }
        return Ok(new { created });
    }
}

public sealed record MealOrderInput(Guid CustomerId, DateOnly OrderDate, Guid MealShiftId, Guid? SubjectGroupId, int RegisteredServings, int? ActualServings, MealOrderStatus Status, string? Note, uint? Version);

public sealed class MealOrderInputValidator : AbstractValidator<MealOrderInput>
{
    public MealOrderInputValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.MealShiftId).NotEmpty();
        RuleFor(x => x.RegisteredServings).InclusiveBetween(0, 100_000);
        RuleFor(x => x.ActualServings).InclusiveBetween(0, 100_000).When(x => x.ActualServings is not null);
    }
}

/// <summary>Meal orders by customers (SPEC §7.4.7); customer representatives are scoped to their customers.</summary>
[Route("api/v1/meal-orders")]
public sealed class MealOrdersController(AppDbContext db) : ApiControllerBase
{
    private IQueryable<MealOrder> Scoped()
    {
        var q = db.Set<MealOrder>().AsNoTracking();
        if (CurrentUser.CustomerScope is { } scope) q = q.Where(o => scope.Contains(o.CustomerId));
        return q;
    }

    private void EnsureScope(Guid customerId)
    {
        if (CurrentUser.CustomerScope is { } scope && !scope.Contains(customerId)) throw new NotFoundException("customer");
    }

    [HttpGet, HasPermission(Permissions.MealOrder.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId, [FromQuery] Guid? mealShiftId, CancellationToken ct)
    {
        var query = Scoped();
        if (from is not null) query = query.Where(o => o.OrderDate >= from);
        if (to is not null) query = query.Where(o => o.OrderDate <= to);
        if (customerId is not null) query = query.Where(o => o.CustomerId == customerId);
        if (mealShiftId is not null) query = query.Where(o => o.MealShiftId == mealShiftId);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(o => o.OrderDate).ThenBy(o => o.Customer!.Code).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(o => new
        {
            o.Id, o.CustomerId, customer = o.Customer!.NameVi, o.OrderDate, o.MealShiftId, shift = db.Set<MealShift>().Where(s => s.Id == o.MealShiftId).Select(s => s.NameVi).FirstOrDefault(),
            o.SubjectGroupId, o.RegisteredServings, o.ActualServings, status = o.Status.ToString(), o.MenuId, o.Note, o.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpPost, HasPermission(Permissions.MealOrder.Create)]
    public async Task<IActionResult> Create([FromBody] MealOrderInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        EnsureScope(input.CustomerId);
        var existing = await db.Set<MealOrder>().FirstOrDefaultAsync(o => o.CustomerId == input.CustomerId && o.OrderDate == input.OrderDate && o.MealShiftId == input.MealShiftId && o.SubjectGroupId == input.SubjectGroupId, ct);
        if (existing is not null) throw new ConflictException("error.duplicateCode", $"{input.OrderDate:dd/MM/yyyy}");
        var o = new MealOrder { CustomerId = input.CustomerId, OrderDate = input.OrderDate, MealShiftId = input.MealShiftId, SubjectGroupId = input.SubjectGroupId,
            RegisteredServings = input.RegisteredServings, ActualServings = input.ActualServings, Status = input.Status, Note = input.Note };
        o.MenuId = await MatchMenu(o, ct);
        db.Add(o);
        await db.SaveChangesAsync(ct);
        return Ok(new { o.Id });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.MealOrder.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MealOrderInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var o = await db.Set<MealOrder>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("order");
        EnsureScope(o.CustomerId);
        EnsureScope(input.CustomerId);
        if (o.Status == MealOrderStatus.Closed && !CurrentUser.Has(Permissions.Menu.Edit)) throw new BusinessRuleException("error.locked");
        if (input.Version is { } v) db.Entry(o).Property(x => x.Version).OriginalValue = v;
        o.RegisteredServings = input.RegisteredServings; o.ActualServings = input.ActualServings; o.Note = input.Note; o.SubjectGroupId = input.SubjectGroupId;
        o.Status = input.Status == MealOrderStatus.Confirmed && o.Status == MealOrderStatus.Confirmed && input.RegisteredServings != o.RegisteredServings ? MealOrderStatus.Adjusted : input.Status;
        o.MenuId = await MatchMenu(o, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}"), HasPermission(Permissions.MealOrder.Delete)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var o = await db.Set<MealOrder>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("order");
        EnsureScope(o.CustomerId);
        db.Remove(o);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Guid?> MatchMenu(MealOrder o, CancellationToken ct)
        => await db.Set<Menu>().AsNoTracking().Where(m => m.MenuDate == o.OrderDate && m.MealShiftId == o.MealShiftId && m.CustomerId == o.CustomerId && m.Status != MenuStatus.Cancelled)
            .OrderByDescending(m => m.Status == MenuStatus.Published).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);

    /// <summary>Totals of servings per day × shift × customer, used to compute quantities (SPEC §7.4.7).</summary>
    [HttpGet("summary"), HasPermission(Permissions.MealOrder.View)]
    public async Task<IActionResult> Summary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var rows = await Scoped().Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Status != MealOrderStatus.Cancelled)
            .GroupBy(o => new { o.OrderDate, o.MealShiftId })
            .Select(g => new { g.Key.OrderDate, g.Key.MealShiftId, registered = g.Sum(x => x.RegisteredServings), actual = g.Sum(x => x.ActualServings ?? x.RegisteredServings), customers = g.Count() })
            .OrderBy(x => x.OrderDate).ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Copies order totals to the matching menus' servings (Draft/Returned menus only; approved menus keep their locked numbers).</summary>
    [HttpPost("apply-to-menus"), HasPermission(Permissions.Menu.Edit)]
    public async Task<IActionResult> ApplyToMenus([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var orders = await Scoped().Where(o => o.OrderDate >= from && o.OrderDate <= to && o.Status != MealOrderStatus.Cancelled).ToListAsync(ct);
        var updated = 0;
        foreach (var g in orders.GroupBy(o => (o.OrderDate, o.MealShiftId, o.CustomerId)))
        {
            var menu = await db.Set<Menu>().FirstOrDefaultAsync(m => m.MenuDate == g.Key.OrderDate && m.MealShiftId == g.Key.MealShiftId && m.CustomerId == g.Key.CustomerId
                                                                   && (m.Status == MenuStatus.Draft || m.Status == MenuStatus.Returned || m.Status == MenuStatus.Submitted || m.Status == MenuStatus.Approved || m.Status == MenuStatus.Published), ct);
            if (menu is null) continue;
            menu.Servings = g.Sum(x => x.ActualServings ?? x.RegisteredServings);
            updated++;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { updated });
    }
}
