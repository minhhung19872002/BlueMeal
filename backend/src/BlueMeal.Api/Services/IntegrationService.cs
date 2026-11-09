using System.Text.Json;
using BlueMeal.Api.Reports;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using BlueMeal.Application.Integration;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Integration;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Services;

public sealed record SyncSummary(Guid LogId, bool Success, int Received, int Created, int Updated, int Skipped, int Conflicts, string? Error);

/// <summary>
/// Synchronization with the VDD nutrition data portal (SPEC §7.10.5): incremental by <c>modifiedSince</c>, upsert by code,
/// conflicts when a locally edited record also changed at the source, integration log for every run.
/// </summary>
public sealed class IntegrationService(AppDbContext db, IHttpClientFactory http, ICurrentUser user, TimeProvider clock, ReportService reports, ILogger<IntegrationService> log)
{
    public const string EntityFood = nameof(Food);
    public const string EntityRecommendation = nameof(Recommendation);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTimeOffset Now => clock.GetUtcNow();

    public async Task<VddApiNutritionDataProvider> ProviderAsync(IntegrationEndpoint ep, CancellationToken ct)
    {
        var units = await db.Set<Nutrient>().AsNoTracking().ToDictionaryAsync(n => n.Code, n => n.Unit, StringComparer.OrdinalIgnoreCase, ct);
        return new VddApiNutritionDataProvider(http.CreateClient(VddApiNutritionDataProvider.HttpClientName), ep, units, clock);
    }

    private static string Summarize(VddApiNutritionDataProvider p)
        => string.Join("\n", p.Calls.Take(40).Select(c => $"{c.Operation} {c.Url} → {c.StatusCode?.ToString() ?? "ERR"} {c.DurationMs} ms, {c.Records} rec"));

    /// <summary>Connectivity check: fetches the nutrient list.</summary>
    public async Task<(bool Ok, int Nutrients, string? Error, int Ms)> TestAsync(IntegrationEndpoint ep, CancellationToken ct)
    {
        var p = await ProviderAsync(ep, ct);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var n = await p.GetNutrientsAsync(ct);
            return (true, n.Count, null, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is AppException or MappingException)
        {
            return (false, 0, ex is AppException ae ? L.T(ae.Key, ae.Args) : ex.Message, (int)sw.ElapsedMilliseconds);
        }
    }

    public async Task<IReadOnlyList<SyncSummary>> SyncAllAsync(bool full, CancellationToken ct)
    {
        var ids = await db.Set<IntegrationEndpoint>().AsNoTracking().Where(e => e.IsEnabled && e.IsActive).OrderBy(e => e.SortOrder).Select(e => e.Id).ToListAsync(ct);
        var result = new List<SyncSummary>();
        foreach (var id in ids) result.Add(await SyncAsync(id, full, ct));
        return result;
    }

    public async Task<SyncSummary> SyncAsync(Guid endpointId, bool full, CancellationToken ct)
    {
        var ep = await db.Set<IntegrationEndpoint>().FirstOrDefaultAsync(e => e.Id == endpointId, ct) ?? throw new NotFoundException("endpoint");
        if (!ep.IsEnabled) throw new BusinessRuleException("integration.disabled");
        var started = Now;
        var since = full ? null : ep.LastSuccessAt;
        var provider = await ProviderAsync(ep, ct);
        int received = 0, created = 0, updated = 0, skipped = 0, conflicts = 0;
        string? error = null;
        try
        {
            // reference data first: nutrients and food groups (no conflict handling — codes and names only)
            var nutrients = await provider.GetNutrientsAsync(ct);
            var localNutrients = await db.Set<Nutrient>().IgnoreQueryFilters().ToDictionaryAsync(n => n.Code, StringComparer.OrdinalIgnoreCase, ct);
            foreach (var n in nutrients)
            {
                received++;
                if (localNutrients.TryGetValue(n.Code, out var ln))
                {
                    if (ln.NameVi != n.NameVi || (n.NameEn != null && ln.NameEn != n.NameEn)) { ln.NameVi = n.NameVi; ln.NameEn = n.NameEn ?? ln.NameEn; updated++; } else skipped++;
                    continue;
                }
                var group = Enum.TryParse<NutrientGroup>(n.Group, true, out var g) ? g : NutrientGroup.Other;
                var nn = new Nutrient { Code = n.Code, NameVi = n.NameVi, NameEn = n.NameEn, Unit = n.Unit, Group = group, Decimals = group == NutrientGroup.Energy ? 0 : group == NutrientGroup.Macro ? 1 : 2, SortOrder = 900 };
                db.Add(nn);
                localNutrients[n.Code] = nn;
                created++;
            }
            var groups = await provider.GetFoodGroupsAsync(since, ct);
            var localGroups = await db.Set<FoodGroup>().IgnoreQueryFilters().ToDictionaryAsync(x => x.Code, StringComparer.OrdinalIgnoreCase, ct);
            foreach (var x in groups)
            {
                received++;
                if (!localGroups.TryGetValue(x.Code, out var lg))
                {
                    lg = new FoodGroup { Code = x.Code, NameVi = x.NameVi, NameEn = x.NameEn, IsAnimalSource = x.IsAnimalSource, SortOrder = 900 };
                    db.Add(lg);
                    localGroups[x.Code] = lg;
                    created++;
                }
                else if (lg.NameVi != x.NameVi || lg.IsAnimalSource != x.IsAnimalSource) { lg.NameVi = x.NameVi; lg.NameEn = x.NameEn ?? lg.NameEn; lg.IsAnimalSource = x.IsAnimalSource; updated++; }
                else skipped++;
            }
            foreach (var x in groups.Where(x => x.ParentCode != null && localGroups.ContainsKey(x.ParentCode)))
                localGroups[x.Code].ParentId = localGroups[x.ParentCode!].Id;
            await db.SaveChangesAsync(ct);

            // foods, page by page
            const int pageSize = 200;
            for (var page = 1; ; page++)
            {
                var batch = await provider.GetFoodsAsync(since, page, pageSize, ct);
                if (batch.Items.Count == 0) break;
                var codes = batch.Items.Select(f => f.Code).ToList();
                var locals = await db.Set<Food>().IgnoreQueryFilters().Include(f => f.Nutrients).Where(f => codes.Contains(f.Code)).ToDictionaryAsync(f => f.Code, StringComparer.OrdinalIgnoreCase, ct);
                foreach (var ext in batch.Items)
                {
                    received++;
                    if (!localGroups.TryGetValue(ext.GroupCode, out var grp)) { skipped++; continue; }
                    var hash = NutritionMapper.Hash(ext);
                    if (!locals.TryGetValue(ext.Code, out var food))
                    {
                        food = new Food { Code = ext.Code, Source = DataSource.VddApi, IsLocked = true };
                        ApplyFood(food, ext, grp.Id, localNutrients, hash);
                        db.Add(food);
                        created++;
                    }
                    else if (food.RemoteHash == hash) { food.LastSyncedAt = Now; skipped++; }
                    else if (food.Source == DataSource.VddApi && !food.LocallyModified && !food.IsDeleted) { ApplyFood(food, ext, grp.Id, localNutrients, hash); updated++; }
                    else
                    {
                        await ConflictAsync(ep.Id, EntityFood, food.Id, food.Code, FoodSnapshot(food, localGroups, localNutrients), ext, ct);
                        conflicts++;
                    }
                }
                await db.SaveChangesAsync(ct);
                if (page * pageSize >= batch.TotalItems) break;
            }

            // recommendations
            var recs = await provider.GetRecommendationsAsync(since, ct);
            var recCodes = recs.Select(r => r.Code).ToList();
            var localRecs = await db.Set<Recommendation>().IgnoreQueryFilters().Include(r => r.Nutrients).Where(r => recCodes.Contains(r.Code)).ToDictionaryAsync(r => r.Code, StringComparer.OrdinalIgnoreCase, ct);
            foreach (var ext in recs)
            {
                received++;
                var hash = NutritionMapper.Hash(ext);
                if (!localRecs.TryGetValue(ext.Code, out var rec))
                {
                    rec = new Recommendation { Code = ext.Code, Source = DataSource.VddApi };
                    ApplyRecommendation(rec, ext, localNutrients, hash);
                    db.Add(rec);
                    created++;
                }
                else if (rec.RemoteHash == hash) { rec.LastSyncedAt = Now; skipped++; }
                else if (rec.Source == DataSource.VddApi && !rec.LocallyModified && !rec.IsDeleted) { ApplyRecommendation(rec, ext, localNutrients, hash); updated++; }
                else
                {
                    await ConflictAsync(ep.Id, EntityRecommendation, rec.Id, rec.Code, RecommendationSnapshot(rec, localNutrients), ext, ct);
                    conflicts++;
                }
            }
            ep.LastSuccessAt = started;
        }
        catch (Exception ex) when (ex is AppException or MappingException)
        {
            error = ex is AppException ae ? L.T(ae.Key, ae.Args) : ex.Message;
            log.LogWarning("VDD sync of {Endpoint} failed: {Error}", ep.Code, error);
            db.ChangeTracker.Clear();
            ep = await db.Set<IntegrationEndpoint>().FirstAsync(e => e.Id == endpointId, ct);
        }
        ep.LastSyncAt = Now;
        var entry = new IntegrationLog
        {
            EndpointId = ep.Id, Direction = "Outbound", Operation = full ? "FullSync" : "Sync", StartedAt = started, DurationMs = (int)(Now - started).TotalMilliseconds, Success = error is null,
            StatusCode = provider.Calls.LastOrDefault()?.StatusCode, RecordsReceived = received, RecordsCreated = created, RecordsUpdated = updated, Conflicts = conflicts,
            RequestSummary = $"modifiedSince={since?.ToString("O") ?? "-"}\n{Summarize(provider)}",
            ResponseSummary = JsonSerializer.Serialize(new { received, created, updated, skipped, conflicts }, Json), Error = error, TriggeredBy = user.UserId,
        };
        db.Add(entry);
        await db.SaveChangesAsync(ct);
        return new SyncSummary(entry.Id, error is null, received, created, updated, skipped, conflicts, error);
    }

    // ------------------------------------------------------------------ apply / snapshot helpers

    private void ApplyFood(Food food, ExtFood ext, Guid groupId, IReadOnlyDictionary<string, Nutrient> nutrients, string hash)
    {
        food.NameVi = ext.NameVi;
        food.NameEn = ext.NameEn;
        food.FoodGroupId = groupId;
        food.RefuseRate = Math.Clamp(ext.RefuseRate, 0, 100);
        food.SourceVersion = ext.Version;
        food.RemoteHash = hash;
        food.LastSyncedAt = Now;
        food.Source = DataSource.VddApi;
        food.LocallyModified = false;
        food.IsLocked = true;
        foreach (var (code, value) in ext.Nutrients)
        {
            if (!nutrients.TryGetValue(code, out var n)) continue;
            var line = food.Nutrients.FirstOrDefault(x => x.NutrientId == n.Id);
            if (line is null) food.Nutrients.Add(new FoodNutrient { FoodId = food.Id, NutrientId = n.Id, ValuePer100gEdible = value });
            else line.ValuePer100gEdible = value;
        }
    }

    private static ExtFood FoodSnapshot(Food f, IReadOnlyDictionary<string, FoodGroup> groups, IReadOnlyDictionary<string, Nutrient> nutrients)
    {
        var byId = nutrients.Values.ToDictionary(n => n.Id, n => n.Code);
        return new ExtFood(f.Code, f.NameVi, f.NameEn, groups.Values.FirstOrDefault(g => g.Id == f.FoodGroupId)?.Code ?? "", f.RefuseRate,
            f.Nutrients.Where(x => byId.ContainsKey(x.NutrientId)).OrderBy(x => byId[x.NutrientId]).ToDictionary(x => byId[x.NutrientId], x => x.ValuePer100gEdible), f.SourceVersion, f.UpdatedAt);
    }

    private void ApplyRecommendation(Recommendation r, ExtRecommendation ext, IReadOnlyDictionary<string, Nutrient> nutrients, string hash)
    {
        r.NameVi = ext.NameVi; r.NameEn = ext.NameEn; r.Reference = ext.Reference; r.ReferenceYear = ext.ReferenceYear; r.EnergyPerDayKcal = ext.EnergyPerDayKcal; r.MealSharePercent = ext.MealSharePercent;
        r.ProteinPctMin = ext.ProteinPctMin; r.ProteinPctMax = ext.ProteinPctMax; r.LipidPctMin = ext.LipidPctMin; r.LipidPctMax = ext.LipidPctMax; r.GlucidPctMin = ext.GlucidPctMin; r.GlucidPctMax = ext.GlucidPctMax;
        r.Source = DataSource.VddApi; r.RemoteHash = hash; r.LastSyncedAt = Now; r.LocallyModified = false;
        // remote nutrient targets are per day; BlueMeal stores per meal (× meal share)
        var share = ext.MealSharePercent / 100m;
        foreach (var n in ext.Nutrients)
        {
            if (!nutrients.TryGetValue(n.Code, out var nut)) continue;
            var line = r.Nutrients.FirstOrDefault(x => x.NutrientId == nut.Id);
            if (line is null) { line = new RecommendationNutrient { RecommendationId = r.Id, NutrientId = nut.Id }; r.Nutrients.Add(line); }
            line.MinPerMeal = n.Min is { } mn ? Math.Round(mn * share, 2) : null;
            line.MaxPerMeal = n.Max is { } mx ? Math.Round(mx * share, 2) : null;
        }
    }

    private static ExtRecommendation RecommendationSnapshot(Recommendation r, IReadOnlyDictionary<string, Nutrient> nutrients)
    {
        var byId = nutrients.Values.ToDictionary(n => n.Id, n => n.Code);
        var share = r.MealSharePercent == 0 ? 1 : r.MealSharePercent / 100m;
        return new ExtRecommendation(r.Code, r.NameVi, r.NameEn, r.Reference, r.ReferenceYear, r.EnergyPerDayKcal, r.MealSharePercent, r.ProteinPctMin, r.ProteinPctMax, r.LipidPctMin, r.LipidPctMax,
            r.GlucidPctMin, r.GlucidPctMax, r.Nutrients.Where(n => byId.ContainsKey(n.NutrientId)).Select(n => new ExtRecommendationNutrient(byId[n.NutrientId],
                n.MinPerMeal is { } mn ? Math.Round(mn / share, 2) : null, n.MaxPerMeal is { } mx ? Math.Round(mx / share, 2) : null)).ToList(), r.UpdatedAt);
    }

    private async Task ConflictAsync<T>(Guid endpointId, string entityType, Guid entityId, string code, T local, T remote, CancellationToken ct)
    {
        var existing = await db.Set<SyncConflict>().FirstOrDefaultAsync(c => c.EntityType == entityType && c.EntityId == entityId && c.Status == SyncConflictStatus.Pending, ct);
        if (existing is null)
        {
            existing = new SyncConflict { EndpointId = endpointId, EntityType = entityType, EntityId = entityId, EntityCode = code };
            db.Add(existing);
        }
        existing.LocalJson = JsonSerializer.Serialize(local, Json);
        existing.RemoteJson = JsonSerializer.Serialize(remote, Json);
        existing.DetectedAt = Now;
    }

    /// <summary>Resolves a conflict: keep the local values (remember the remote version) or accept the remote values.</summary>
    public async Task ResolveAsync(Guid conflictId, bool acceptRemote, CancellationToken ct)
    {
        var c = await db.Set<SyncConflict>().FirstOrDefaultAsync(x => x.Id == conflictId, ct) ?? throw new NotFoundException("conflict");
        if (c.Status != SyncConflictStatus.Pending) throw new BusinessRuleException("integration.conflictResolved");
        var nutrients = await db.Set<Nutrient>().IgnoreQueryFilters().ToDictionaryAsync(n => n.Code, StringComparer.OrdinalIgnoreCase, ct);
        if (c.EntityType == EntityFood)
        {
            var remote = JsonSerializer.Deserialize<ExtFood>(c.RemoteJson, Json)!;
            var food = await db.Set<Food>().IgnoreQueryFilters().Include(f => f.Nutrients).FirstOrDefaultAsync(f => f.Id == c.EntityId, ct) ?? throw new NotFoundException("food");
            var hash = NutritionMapper.Hash(remote);
            if (acceptRemote)
            {
                var group = await db.Set<FoodGroup>().IgnoreQueryFilters().FirstOrDefaultAsync(g => g.Code == remote.GroupCode, ct) ?? throw new NotFoundException("food group");
                ApplyFood(food, remote, group.Id, nutrients, hash);
            }
            else food.RemoteHash = hash; // same remote version will not raise the conflict again
        }
        else if (c.EntityType == EntityRecommendation)
        {
            var remote = JsonSerializer.Deserialize<ExtRecommendation>(c.RemoteJson, Json)!;
            var rec = await db.Set<Recommendation>().IgnoreQueryFilters().Include(r => r.Nutrients).FirstOrDefaultAsync(r => r.Id == c.EntityId, ct) ?? throw new NotFoundException("recommendation");
            var hash = NutritionMapper.Hash(remote);
            if (acceptRemote) ApplyRecommendation(rec, remote, nutrients, hash);
            else rec.RemoteHash = hash;
        }
        c.Status = acceptRemote ? SyncConflictStatus.AcceptedRemote : SyncConflictStatus.KeptLocal;
        c.ResolvedAt = Now;
        c.ResolvedBy = user.UserId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Optional push of aggregated nutrition figures (RPT-06 by month) to the portal.</summary>
    public async Task<PushResult> PushAsync(Guid endpointId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var ep = await db.Set<IntegrationEndpoint>().FirstOrDefaultAsync(e => e.Id == endpointId, ct) ?? throw new NotFoundException("endpoint");
        var r = await reports.RunTrustedAsync("RPT-06", new ReportFilter { From = from, To = to, Mode = "month" }, ct);
        static decimal D(object? v) => v is null ? 0 : Convert.ToDecimal(v);
        var rows = r.Rows.Select(x => new NutritionSummaryRow((string)x["period"]!, x["subject"] as string, x["customer"] as string, (int)D(x["menus"]), 0, D(x["energy"]),
            x["target"] is null ? null : D(x["target"]), D(x["p"]), D(x["l"]), D(x["g"]))).ToList();
        var org = await db.Set<Organization>().AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "BlueMeal";
        var provider = await ProviderAsync(ep, ct);
        var started = Now;
        PushResult result;
        string? error = null;
        try { result = await provider.PushAggregatedReportAsync(new NutritionSummaryPayload(org, from, to, rows), ct); }
        catch (AppException ex) { error = L.T(ex.Key, ex.Args); result = new PushResult(false, null, error); }
        db.Add(new IntegrationLog
        {
            EndpointId = ep.Id, Direction = "Outbound", Operation = "PushAggregatedReport", StartedAt = started, DurationMs = (int)(Now - started).TotalMilliseconds, Success = result.Accepted,
            StatusCode = provider.Calls.LastOrDefault()?.StatusCode, RecordsReceived = 0, RecordsCreated = rows.Count, RequestSummary = $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}, {rows.Count} rows\n{Summarize(provider)}",
            ResponseSummary = JsonSerializer.Serialize(result, Json), Error = error, TriggeredBy = user.UserId,
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}

/// <summary>Scheduled synchronization (02:00 by default, Jobs:Cron:vdd-sync).</summary>
public sealed class VddSyncJob(IntegrationService integration) : IAppJob
{
    public string Name => JobNames.VddSync;
    public Task ExecuteAsync(IReadOnlyDictionary<string, string> data, CancellationToken ct) => integration.SyncAllAsync(data.GetValueOrDefault("full") == "true", ct);
}
