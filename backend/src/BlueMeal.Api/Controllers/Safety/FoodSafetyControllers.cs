using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Safety;

public sealed record Step1Input(DateTimeOffset InspectedAt, Guid? ReceiptId, Guid? ReceiptLineId, Guid IngredientId, Guid? PartnerId, decimal? Quantity, string? LotNo, DateOnly? ExpiryDate,
    Dictionary<string, JsonElement>? Criteria, string? SensoryNote, bool OriginDocsOk, string? OriginDocs, decimal? Temperature, QualityResult Result, string? Action, string? Inspector, uint? Version);

public sealed record Step2Input(Guid? MenuId, Guid? DishId, DateOnly InspectDate, string? Hygiene, string? Participants, DateTimeOffset? CookStart, DateTimeOffset? CookEnd,
    Dictionary<string, JsonElement>? Criteria, string? SensoryNote, QualityResult Result, string? Action, string? Inspector, uint? Version);

public sealed record Step3Input(Guid? MenuId, Guid? DishId, DateOnly InspectDate, DateTimeOffset? PortionTime, decimal? Temperature, string? Utensils,
    Dictionary<string, JsonElement>? Criteria, string? SensoryNote, QualityResult Result, string? Action, string? Inspector, uint? Version);

public sealed class Step1InputValidator : AbstractValidator<Step1Input>
{
    public Step1InputValidator()
    {
        RuleFor(x => x.IngredientId).NotEmpty();
        RuleFor(x => x.Temperature).InclusiveBetween(-60, 100).When(x => x.Temperature != null);
        RuleFor(x => x.Action).NotEmpty().When(x => x.Result == QualityResult.Fail).WithMessage("validation.actionRequired");
        RuleFor(x => x.SensoryNote).MaximumLength(1000);
        RuleFor(x => x.Action).MaximumLength(1000);
    }
}

public sealed class Step2InputValidator : AbstractValidator<Step2Input>
{
    public Step2InputValidator()
    {
        RuleFor(x => x).Must(x => x.CookEnd == null || x.CookStart == null || x.CookEnd >= x.CookStart).WithMessage("validation.dateRange");
        RuleFor(x => x.Action).NotEmpty().When(x => x.Result == QualityResult.Fail).WithMessage("validation.actionRequired");
        RuleFor(x => x.Participants).MaximumLength(1000);
    }
}

public sealed class Step3InputValidator : AbstractValidator<Step3Input>
{
    public Step3InputValidator()
    {
        RuleFor(x => x.Temperature).InclusiveBetween(-60, 150).When(x => x.Temperature != null);
        RuleFor(x => x.Action).NotEmpty().When(x => x.Result == QualityResult.Fail).WithMessage("validation.actionRequired");
    }
}

/// <summary>Three-step food inspection logs (kiểm thực 3 bước, Bộ Y tế) with configurable criteria — SPEC §5.6.</summary>
[Route("api/v1/inspections")]
public sealed class InspectionsController(AppDbContext db, ISettingsService settings) : ApiControllerBase
{
    private string InspectorName(string? given) => string.IsNullOrWhiteSpace(given) ? CurrentUser.FullName ?? CurrentUser.UserName ?? "-" : given.Trim();
    private static string? Json(Dictionary<string, JsonElement>? c) => c is null ? null : JsonSerializer.Serialize(c);

    /// <summary>Configured criteria for the three forms (A-23); editable in system settings.</summary>
    [HttpGet("form"), HasPermission(Permissions.Inspection.View)]
    public async Task<IActionResult> Form(CancellationToken ct) => Ok(await settings.GetAsync<InspectionFormSettings>(SettingKeys.Inspection, ct));

    // ------------------------------------------------------------------ step 1

    [HttpGet("step1"), HasPermission(Permissions.Inspection.View)]
    public async Task<IActionResult> Step1List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] QualityResult? result, [FromQuery] Guid? receiptId, CancellationToken ct)
    {
        var query = db.Set<FoodInspectionStep1>().AsNoTracking();
        if (from is { } f) { var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)); query = query.Where(x => x.InspectedAt >= start); }
        if (to is { } t) { var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)); query = query.Where(x => x.InspectedAt < end); }
        if (result is not null) query = query.Where(x => x.Result == result);
        if (receiptId is not null) query = query.Where(x => x.ReceiptId == receiptId);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.InspectedAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new
        {
            x.Id, x.InspectedAt, x.ReceiptId, receiptNo = db.Set<StockReceipt>().Where(r => r.Id == x.ReceiptId).Select(r => r.DocNo).FirstOrDefault(), x.IngredientId,
            ingredientCode = x.Ingredient!.Code, ingredientName = x.Ingredient.NameVi, ingredientNameEn = x.Ingredient.NameEn,
            partner = db.Set<Partner>().Where(p => p.Id == x.PartnerId).Select(p => p.NameVi).FirstOrDefault(), x.PartnerId, x.Quantity, x.LotNo, x.ExpiryDate, x.Temperature,
            x.OriginDocsOk, x.OriginDocs, x.SensoryNote, result = x.Result.ToString(), x.Action, x.Inspector, x.Criteria, x.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpPost("step1"), HasPermission(Permissions.Inspection.Create)]
    public async Task<IActionResult> Step1Create([FromBody] Step1Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = new FoodInspectionStep1();
        Apply1(e, i);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return Ok(new { e.Id });
    }

    [HttpPut("step1/{id:guid}"), HasPermission(Permissions.Inspection.Edit)]
    public async Task<IActionResult> Step1Update(Guid id, [FromBody] Step1Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = await db.Set<FoodInspectionStep1>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("inspection");
        if (i.Version is { } v) db.Entry(e).Property(x => x.Version).OriginalValue = v;
        Apply1(e, i);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Apply1(FoodInspectionStep1 e, Step1Input i)
    {
        e.InspectedAt = i.InspectedAt; e.ReceiptId = i.ReceiptId; e.ReceiptLineId = i.ReceiptLineId; e.IngredientId = i.IngredientId; e.PartnerId = i.PartnerId; e.Quantity = i.Quantity;
        e.LotNo = i.LotNo; e.ExpiryDate = i.ExpiryDate; e.Criteria = Json(i.Criteria); e.SensoryNote = i.SensoryNote; e.OriginDocsOk = i.OriginDocsOk; e.OriginDocs = i.OriginDocs;
        e.Temperature = i.Temperature; e.Result = i.Result; e.Action = i.Action; e.Inspector = InspectorName(i.Inspector); e.InspectorUserId ??= CurrentUser.UserId;
    }

    [HttpDelete("step1/{id:guid}"), HasPermission(Permissions.Inspection.Delete)]
    public Task<IActionResult> Step1Delete(Guid id, CancellationToken ct) => DeleteAsync<FoodInspectionStep1>(id, ct);

    // ------------------------------------------------------------------ step 2 and 3

    [HttpGet("step2"), HasPermission(Permissions.Inspection.View)]
    public async Task<IActionResult> Step2List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? menuId, CancellationToken ct)
    {
        var query = db.Set<FoodInspectionStep2>().AsNoTracking();
        if (from is not null) query = query.Where(x => x.InspectDate >= from);
        if (to is not null) query = query.Where(x => x.InspectDate <= to);
        if (menuId is not null) query = query.Where(x => x.MenuId == menuId);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.InspectDate).ThenByDescending(x => x.CookStart).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new
        {
            x.Id, x.MenuId, menuCode = db.Set<Menu>().Where(m => m.Id == x.MenuId).Select(m => m.Code).FirstOrDefault(), x.DishId, dishName = x.Dish!.NameVi, dishNameEn = x.Dish.NameEn,
            x.InspectDate, x.Hygiene, x.Participants, x.CookStart, x.CookEnd, x.SensoryNote, result = x.Result.ToString(), x.Action, x.Inspector, x.Criteria, x.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpPost("step2"), HasPermission(Permissions.Inspection.Create)]
    public async Task<IActionResult> Step2Create([FromBody] Step2Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = new FoodInspectionStep2();
        Apply2(e, i);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return Ok(new { e.Id });
    }

    [HttpPut("step2/{id:guid}"), HasPermission(Permissions.Inspection.Edit)]
    public async Task<IActionResult> Step2Update(Guid id, [FromBody] Step2Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = await db.Set<FoodInspectionStep2>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("inspection");
        if (i.Version is { } v) db.Entry(e).Property(x => x.Version).OriginalValue = v;
        Apply2(e, i);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Apply2(FoodInspectionStep2 e, Step2Input i)
    {
        e.MenuId = i.MenuId; e.DishId = i.DishId; e.InspectDate = i.InspectDate; e.Hygiene = i.Hygiene; e.Participants = i.Participants; e.CookStart = i.CookStart; e.CookEnd = i.CookEnd;
        e.Criteria = Json(i.Criteria); e.SensoryNote = i.SensoryNote; e.Result = i.Result; e.Action = i.Action; e.Inspector = InspectorName(i.Inspector);
    }

    [HttpDelete("step2/{id:guid}"), HasPermission(Permissions.Inspection.Delete)]
    public Task<IActionResult> Step2Delete(Guid id, CancellationToken ct) => DeleteAsync<FoodInspectionStep2>(id, ct);

    [HttpGet("step3"), HasPermission(Permissions.Inspection.View)]
    public async Task<IActionResult> Step3List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? menuId, CancellationToken ct)
    {
        var query = db.Set<FoodInspectionStep3>().AsNoTracking();
        if (from is not null) query = query.Where(x => x.InspectDate >= from);
        if (to is not null) query = query.Where(x => x.InspectDate <= to);
        if (menuId is not null) query = query.Where(x => x.MenuId == menuId);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.InspectDate).ThenByDescending(x => x.PortionTime).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new
        {
            x.Id, x.MenuId, menuCode = db.Set<Menu>().Where(m => m.Id == x.MenuId).Select(m => m.Code).FirstOrDefault(), x.DishId, dishName = x.Dish!.NameVi, dishNameEn = x.Dish.NameEn,
            x.InspectDate, x.PortionTime, x.Temperature, x.Utensils, x.SensoryNote, result = x.Result.ToString(), x.Action, x.Inspector, x.Criteria, x.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpPost("step3"), HasPermission(Permissions.Inspection.Create)]
    public async Task<IActionResult> Step3Create([FromBody] Step3Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = new FoodInspectionStep3();
        Apply3(e, i);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return Ok(new { e.Id });
    }

    [HttpPut("step3/{id:guid}"), HasPermission(Permissions.Inspection.Edit)]
    public async Task<IActionResult> Step3Update(Guid id, [FromBody] Step3Input i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var e = await db.Set<FoodInspectionStep3>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("inspection");
        if (i.Version is { } v) db.Entry(e).Property(x => x.Version).OriginalValue = v;
        Apply3(e, i);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void Apply3(FoodInspectionStep3 e, Step3Input i)
    {
        e.MenuId = i.MenuId; e.DishId = i.DishId; e.InspectDate = i.InspectDate; e.PortionTime = i.PortionTime; e.Temperature = i.Temperature; e.Utensils = i.Utensils;
        e.Criteria = Json(i.Criteria); e.SensoryNote = i.SensoryNote; e.Result = i.Result; e.Action = i.Action; e.Inspector = InspectorName(i.Inspector);
    }

    [HttpDelete("step3/{id:guid}"), HasPermission(Permissions.Inspection.Delete)]
    public Task<IActionResult> Step3Delete(Guid id, CancellationToken ct) => DeleteAsync<FoodInspectionStep3>(id, ct);

    private async Task<IActionResult> DeleteAsync<T>(Guid id, CancellationToken ct) where T : Entity
    {
        var e = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("inspection");
        db.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Prefill for steps 2/3: dishes of the menus of a day (one row per menu × dish).</summary>
    [HttpGet("day-dishes"), HasPermission(Permissions.Inspection.View)]
    public async Task<IActionResult> DayDishes([FromQuery] DateOnly date, CancellationToken ct)
        => Ok(await (from i in db.Set<MenuItem>().AsNoTracking()
                     join m in db.Set<Menu>().AsNoTracking() on i.MenuId equals m.Id
                     where m.MenuDate == date && (m.Status == MenuStatus.Approved || m.Status == MenuStatus.Published)
                     orderby m.MealShift!.SortOrder, i.SortOrder
                     select new { i.MenuId, menuCode = m.Code, shift = m.MealShift!.NameVi, i.DishId, dishName = i.Dish!.NameVi, dishNameEn = i.Dish.NameEn }).ToListAsync(ct));
}

// ---------------------------------------------------------------------------------------------- food samples

public sealed record SampleInput(Guid? MenuId, Guid DishId, DateTimeOffset SampledAt, decimal WeightG, decimal? StorageTemp, string? Sampler, DateTimeOffset? PlannedDisposeAt, string? Note, uint? Version);
public sealed record DisposeInput(DateTimeOffset? DisposedAt, string? Disposer, string? Note);

public sealed class SampleInputValidator : AbstractValidator<SampleInput>
{
    public SampleInputValidator()
    {
        RuleFor(x => x.DishId).NotEmpty();
        RuleFor(x => x.WeightG).GreaterThan(0).LessThan(10_000);
        RuleFor(x => x.StorageTemp).InclusiveBetween(-30, 30).When(x => x.StorageTemp != null);
        RuleFor(x => x).Must(x => x.PlannedDisposeAt == null || x.PlannedDisposeAt > x.SampledAt).WithMessage("validation.dateRange");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

/// <summary>Food sample retention book (sổ lưu mẫu thức ăn): planned disposal ≥ configured hours (default 24 h) — SPEC §5.6.</summary>
[Route("api/v1/food-samples")]
public sealed class FoodSamplesController(AppDbContext db, ISettingsService settings, IDocNumberGenerator docs, TimeProvider clock) : ApiControllerBase
{
    [HttpGet, HasPermission(Permissions.Sample.View)]
    public async Task<IActionResult> List([FromQuery(Name = "")] PageQuery q, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? state, [FromQuery] Guid? menuId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.Set<FoodSample>().AsNoTracking();
        if (from is { } f) { var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)); query = query.Where(x => x.SampledAt >= start); }
        if (to is { } t) { var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)); query = query.Where(x => x.SampledAt < end); }
        if (menuId is not null) query = query.Where(x => x.MenuId == menuId);
        query = state switch
        {
            "kept" => query.Where(x => x.DisposedAt == null && x.PlannedDisposeAt > now),
            "due" => query.Where(x => x.DisposedAt == null && x.PlannedDisposeAt <= now),
            "disposed" => query.Where(x => x.DisposedAt != null),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(q.Q)) query = query.Where(x => EF.Functions.ILike(x.Code, "%" + q.Q + "%"));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(x => x.SampledAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(x => new
        {
            x.Id, x.Code, x.MenuId, menuCode = db.Set<Menu>().Where(m => m.Id == x.MenuId).Select(m => m.Code).FirstOrDefault(), x.DishId, dishName = x.Dish!.NameVi, dishNameEn = x.Dish.NameEn,
            x.SampledAt, x.WeightG, x.StorageTemp, x.Sampler, x.PlannedDisposeAt, x.DisposedAt, x.Disposer, x.Note,
            state = x.DisposedAt != null ? "disposed" : x.PlannedDisposeAt <= now ? "due" : "kept", x.Version,
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    private async Task<SampleSettings> Cfg(CancellationToken ct) => await settings.GetAsync<SampleSettings>(SettingKeys.Sample, ct);

    /// <summary>Retention rules shown on the sample screen (minimum hours and weight).</summary>
    [HttpGet("rules"), HasPermission(Permissions.Sample.View)]
    public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await Cfg(ct));

    private static void EnsureRules(SampleInput i, SampleSettings cfg)
    {
        if (i.WeightG < cfg.MinWeightG) throw new BusinessRuleException("sample.weightTooLow", cfg.MinWeightG);
        if (i.PlannedDisposeAt is { } p && p < i.SampledAt.AddHours(cfg.RetentionHours)) throw new BusinessRuleException("sample.tooEarly", cfg.RetentionHours);
    }

    [HttpPost, HasPermission(Permissions.Sample.Create)]
    public async Task<IActionResult> Create([FromBody] SampleInput i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var cfg = await Cfg(ct);
        EnsureRules(i, cfg);
        var date = DateOnly.FromDateTime(i.SampledAt.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var s = new FoodSample
        {
            Code = await docs.NextAsync("LM", date, ct), MenuId = i.MenuId, DishId = i.DishId, SampledAt = i.SampledAt, WeightG = i.WeightG, StorageTemp = i.StorageTemp,
            Sampler = string.IsNullOrWhiteSpace(i.Sampler) ? CurrentUser.FullName ?? CurrentUser.UserName ?? "-" : i.Sampler.Trim(),
            PlannedDisposeAt = i.PlannedDisposeAt ?? i.SampledAt.AddHours(cfg.RetentionHours), Note = i.Note,
        };
        db.Add(s);
        await db.SaveChangesAsync(ct);
        return Ok(new { s.Id, s.Code, s.PlannedDisposeAt });
    }

    [HttpPut("{id:guid}"), HasPermission(Permissions.Sample.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SampleInput i, CancellationToken ct)
    {
        await ValidateAsync(i, ct);
        var s = await db.Set<FoodSample>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("sample");
        if (s.DisposedAt is not null) throw new BusinessRuleException("sample.alreadyDisposed");
        var cfg = await Cfg(ct);
        EnsureRules(i, cfg);
        if (i.Version is { } v) db.Entry(s).Property(x => x.Version).OriginalValue = v;
        s.MenuId = i.MenuId; s.DishId = i.DishId; s.SampledAt = i.SampledAt; s.WeightG = i.WeightG; s.StorageTemp = i.StorageTemp; s.Note = i.Note;
        if (!string.IsNullOrWhiteSpace(i.Sampler)) s.Sampler = i.Sampler.Trim();
        s.PlannedDisposeAt = i.PlannedDisposeAt ?? i.SampledAt.AddHours(cfg.RetentionHours);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Records the disposal; not allowed before the minimum retention time.</summary>
    [HttpPost("{id:guid}/dispose"), HasPermission(Permissions.Sample.Dispose)]
    public async Task<IActionResult> Dispose(Guid id, [FromBody] DisposeInput i, CancellationToken ct)
    {
        var s = await db.Set<FoodSample>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("sample");
        if (s.DisposedAt is not null) throw new BusinessRuleException("sample.alreadyDisposed");
        var cfg = await Cfg(ct);
        var at = i.DisposedAt ?? clock.GetUtcNow();
        if (at < s.SampledAt.AddHours(cfg.RetentionHours)) throw new BusinessRuleException("sample.tooEarly", cfg.RetentionHours);
        s.DisposedAt = at;
        s.Disposer = string.IsNullOrWhiteSpace(i.Disposer) ? CurrentUser.FullName ?? CurrentUser.UserName ?? "-" : i.Disposer.Trim();
        if (!string.IsNullOrWhiteSpace(i.Note)) s.Note = string.IsNullOrWhiteSpace(s.Note) ? i.Note : $"{s.Note}\n{i.Note}";
        await db.SaveChangesAsync(ct);
        // the disposal resolves the open W-SAMPLE-DISPOSE alert of this sample
        await db.Set<Alert>().Where(a => a.DedupeKey == $"W-SAMPLE-DISPOSE:{id}" && a.Status != AlertStatus.Resolved)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.Status, AlertStatus.Resolved).SetProperty(a => a.ResolvedAt, clock.GetUtcNow()).SetProperty(a => a.ResolvedBy, CurrentUser.UserId), ct);
        return NoContent();
    }
}
