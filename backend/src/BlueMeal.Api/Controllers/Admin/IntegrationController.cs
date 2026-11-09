using System.Security.Cryptography;
using System.Text.Json;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Controllers.PublicApi;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Integration;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Integration;
using BlueMeal.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers.Admin;

public sealed record EndpointInput(string Code, string NameVi, string? NameEn, string BaseUrl, IntegrationAuthType AuthType, EndpointCredentials? Credentials, int TimeoutSeconds,
    string? SyncCron, string? MappingJson, bool IsEnabled, bool IsActive, uint? Version);

public sealed class EndpointInputValidator : AbstractValidator<EndpointInput>
{
    public EndpointInputValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(500).Must(u => Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == Uri.UriSchemeHttps || x.Scheme == Uri.UriSchemeHttp))
            .WithMessage("validation.url");
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(3, 300);
        RuleFor(x => x.SyncCron).MaximumLength(100);
        RuleFor(x => x.MappingJson).Must(BeValidMapping).When(x => !string.IsNullOrWhiteSpace(x.MappingJson)).WithMessage("integration.badMapping");
    }

    private static bool BeValidMapping(string? json)
    {
        try { MappingConfig.Parse(json); return true; } catch (JsonException) { return false; }
    }
}

/// <summary>VDD integration administration (SPEC §7.10): endpoints, connectivity test, manual sync, logs, conflicts, push, public API clients.</summary>
[Route("api/v1/integration")]
public sealed class IntegrationController(AppDbContext db, IntegrationService integration, IAuditWriter audit, TimeProvider clock) : ApiControllerBase
{
    [HttpGet("endpoints"), HasPermission(Permissions.Integration.View)]
    public async Task<IActionResult> Endpoints(CancellationToken ct)
        => Ok((await db.Set<IntegrationEndpoint>().AsNoTracking().OrderBy(e => e.SortOrder).ThenBy(e => e.Code).ToListAsync(ct)).Select(View));

    /// <summary>Credentials are write-only: only which fields are set is returned.</summary>
    private static object View(IntegrationEndpoint e)
    {
        var c = EndpointCredentials.Parse(e.Credentials);
        return new
        {
            e.Id, e.Code, e.NameVi, e.NameEn, e.BaseUrl, authType = e.AuthType.ToString(), e.TimeoutSeconds, e.SyncCron, mappingJson = e.MappingJson ?? MappingConfig.DefaultJson, e.IsEnabled, e.IsActive,
            e.LastSyncAt, e.LastSuccessAt, e.Version,
            credentials = new
            {
                c.ApiKeyHeader, hasApiKey = !string.IsNullOrEmpty(c.ApiKey), c.Username, hasPassword = !string.IsNullOrEmpty(c.Password), c.TokenUrl, c.ClientId,
                hasClientSecret = !string.IsNullOrEmpty(c.ClientSecret), c.Scope,
            },
        };
    }

    private static string MergeCredentials(string? existing, EndpointCredentials? input)
    {
        var c = EndpointCredentials.Parse(existing);
        if (input is not null)
        {
            c.ApiKeyHeader = string.IsNullOrWhiteSpace(input.ApiKeyHeader) ? c.ApiKeyHeader : input.ApiKeyHeader;
            if (!string.IsNullOrEmpty(input.ApiKey)) c.ApiKey = input.ApiKey;
            c.Username = input.Username ?? c.Username;
            if (!string.IsNullOrEmpty(input.Password)) c.Password = input.Password;
            c.TokenUrl = input.TokenUrl ?? c.TokenUrl;
            c.ClientId = input.ClientId ?? c.ClientId;
            if (!string.IsNullOrEmpty(input.ClientSecret)) c.ClientSecret = input.ClientSecret;
            c.Scope = input.Scope ?? c.Scope;
        }
        return JsonSerializer.Serialize(c, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static void Apply(IntegrationEndpoint e, EndpointInput i)
    {
        e.Code = i.Code.Trim(); e.NameVi = i.NameVi.Trim(); e.NameEn = i.NameEn; e.BaseUrl = i.BaseUrl.Trim(); e.AuthType = i.AuthType; e.TimeoutSeconds = i.TimeoutSeconds;
        e.SyncCron = i.SyncCron; e.MappingJson = string.IsNullOrWhiteSpace(i.MappingJson) ? null : i.MappingJson; e.IsEnabled = i.IsEnabled; e.IsActive = i.IsActive;
        e.Credentials = MergeCredentials(e.Credentials, i.Credentials);
    }

    [HttpPost("endpoints"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> Create([FromBody] EndpointInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        if (await db.Set<IntegrationEndpoint>().AnyAsync(e => e.Code == input.Code, ct)) throw new ConflictException("error.duplicateCode", input.Code);
        var e = new IntegrationEndpoint();
        Apply(e, input);
        db.Add(e);
        await db.SaveChangesAsync(ct);
        return Ok(View(e));
    }

    [HttpPut("endpoints/{id:guid}"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> Update(Guid id, [FromBody] EndpointInput input, CancellationToken ct)
    {
        await ValidateAsync(input, ct);
        var e = await db.Set<IntegrationEndpoint>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("endpoint");
        if (input.Version is { } v) db.Entry(e).Property(x => x.Version).OriginalValue = v;
        Apply(e, input);
        await db.SaveChangesAsync(ct);
        VddApiNutritionDataProvider.Reset(e.Id);
        return Ok(View(e));
    }

    [HttpPost("endpoints/{id:guid}/test"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> Test(Guid id, CancellationToken ct)
    {
        var e = await db.Set<IntegrationEndpoint>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("endpoint");
        var (ok, n, error, ms) = await integration.TestAsync(e, ct);
        return Ok(new { ok, nutrients = n, error, durationMs = ms });
    }

    public sealed record SyncInput(Guid? EndpointId, bool Full);

    /// <summary>Manual synchronization (button on the Foods and Integration screens).</summary>
    [HttpPost("sync"), HasPermission(Permissions.Integration.Sync)]
    public async Task<IActionResult> Sync([FromBody] SyncInput? input, CancellationToken ct)
    {
        IReadOnlyList<SyncSummary> result = input?.EndpointId is { } id ? [await integration.SyncAsync(id, input.Full, ct)] : await integration.SyncAllAsync(input?.Full ?? false, ct);
        if (result.Count == 0) throw new BusinessRuleException("integration.noEndpoint");
        await audit.WriteAsync(AuditAction.Update, "IntegrationSync", input?.EndpointId?.ToString(), null, result, result.All(r => r.Success), ct: ct);
        return Ok(result);
    }

    public sealed record PushInput(DateOnly From, DateOnly To);

    [HttpPost("endpoints/{id:guid}/push"), HasPermission(Permissions.Integration.Sync)]
    public async Task<IActionResult> Push(Guid id, [FromBody] PushInput input, CancellationToken ct) => Ok(await integration.PushAsync(id, input.From, input.To, ct));

    [HttpGet("logs"), HasPermission(Permissions.Integration.View)]
    public async Task<IActionResult> Logs([FromQuery(Name = "")] PageQuery q, [FromQuery] Guid? endpointId, [FromQuery] bool? success, CancellationToken ct)
    {
        var query = db.Set<IntegrationLog>().AsNoTracking();
        if (endpointId is not null) query = query.Where(l => l.EndpointId == endpointId);
        if (success is not null) query = query.Where(l => l.Success == success);
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(l => l.StartedAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(l => new
        {
            l.Id, l.EndpointId, endpoint = db.Set<IntegrationEndpoint>().Where(e => e.Id == l.EndpointId).Select(e => e.Code).FirstOrDefault(), l.Direction, l.Operation, l.StartedAt, l.DurationMs, l.Success,
            l.StatusCode, l.RecordsReceived, l.RecordsCreated, l.RecordsUpdated, l.Conflicts, l.RequestSummary, l.ResponseSummary, l.Error,
            triggeredBy = db.Set<AppUser>().Where(u => u.Id == l.TriggeredBy).Select(u => u.UserName).FirstOrDefault(),
        }).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows, q.Page, q.PageSize, total));
    }

    [HttpGet("conflicts"), HasPermission(Permissions.Integration.View)]
    public async Task<IActionResult> Conflicts([FromQuery(Name = "")] PageQuery q, [FromQuery] SyncConflictStatus? status, CancellationToken ct)
    {
        var query = db.Set<SyncConflict>().AsNoTracking().Where(c => c.Status == (status ?? SyncConflictStatus.Pending));
        var total = await query.LongCountAsync(ct);
        var rows = await query.OrderByDescending(c => c.DetectedAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return Ok(new PagedResult<object>(rows.Select(c => (object)new
        {
            c.Id, c.EntityType, c.EntityId, c.EntityCode, c.DetectedAt, status = c.Status.ToString(), c.ResolvedAt,
            local = JsonSerializer.Deserialize<JsonElement>(c.LocalJson), remote = JsonSerializer.Deserialize<JsonElement>(c.RemoteJson),
        }).ToList(), q.Page, q.PageSize, total));
    }

    public sealed record ResolveInput(string Keep);

    [HttpPost("conflicts/{id:guid}/resolve"), HasPermission(Permissions.Integration.Resolve)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveInput input, CancellationToken ct)
    {
        if (input.Keep is not ("local" or "remote")) throw new AppException("error.validation");
        await integration.ResolveAsync(id, input.Keep == "remote", ct);
        await audit.WriteAsync(AuditAction.Update, nameof(SyncConflict), id.ToString(), null, new { keep = input.Keep }, ct: ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ public API clients (A-26)

    [HttpGet("clients"), HasPermission(Permissions.Integration.View)]
    public async Task<IActionResult> Clients(CancellationToken ct)
        => Ok(await db.Set<ApiClient>().AsNoTracking().OrderBy(c => c.ClientId).Select(c => new { c.Id, c.ClientId, c.Name, c.Scopes, c.IsActive, c.LastUsedAt, c.CreatedAt }).ToListAsync(ct));

    public sealed record ClientInput(string Name, string[] Scopes);

    /// <summary>Creates a client; the secret is shown once in the response and stored hashed.</summary>
    [HttpPost("clients"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> CreateClient([FromBody] ClientInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 200 || input.Scopes.Length == 0 || input.Scopes.Any(s => !PublicScopes.All.Contains(s))) throw new AppException("error.validation");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var client = new ApiClient { ClientId = "bm_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(), Name = input.Name.Trim(), Scopes = string.Join(' ', input.Scopes.Distinct()) };
        client.SecretHash = new PasswordHasher<ApiClient>().HashPassword(client, secret);
        db.Add(client);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Create, nameof(ApiClient), client.Id.ToString(), null, new { client.ClientId, client.Scopes }, ct: ct);
        return Ok(new { client.Id, client.ClientId, clientSecret = secret, client.Scopes });
    }

    [HttpPost("clients/{id:guid}/rotate"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> RotateClient(Guid id, CancellationToken ct)
    {
        var client = await db.Set<ApiClient>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("client");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        client.SecretHash = new PasswordHasher<ApiClient>().HashPassword(client, secret);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Update, nameof(ApiClient), id.ToString(), null, new { rotated = clock.GetUtcNow() }, ct: ct);
        return Ok(new { client.Id, client.ClientId, clientSecret = secret });
    }

    [HttpPost("clients/{id:guid}/toggle"), HasPermission(Permissions.Integration.Edit)]
    public async Task<IActionResult> ToggleClient(Guid id, CancellationToken ct)
    {
        var client = await db.Set<ApiClient>().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("client");
        client.IsActive = !client.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { client.IsActive });
    }
}
