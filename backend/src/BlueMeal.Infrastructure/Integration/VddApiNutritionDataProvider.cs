using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlueMeal.Application.Common;
using BlueMeal.Application.Integration;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace BlueMeal.Infrastructure.Integration;

/// <summary>Credential bag stored encrypted in <c>integration_endpoint.credentials</c>.</summary>
public sealed class EndpointCredentials
{
    public string? ApiKey { get; set; }
    public string ApiKeyHeader { get; set; } = "X-Api-Key";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }

    public static EndpointCredentials Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? new EndpointCredentials() : JsonSerializer.Deserialize<EndpointCredentials>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new EndpointCredentials();
}

/// <summary>Outcome of one HTTP call, for the integration log.</summary>
public sealed record CallInfo(string Operation, string Url, int? StatusCode, int DurationMs, int Records);

/// <summary>
/// HTTP implementation of <see cref="INutritionDataProvider"/> (SPEC §7.10.2): API key / Basic / OAuth2 client credentials,
/// Polly retry (exponential, transient errors) + circuit breaker + per-attempt timeout; payloads mapped by <see cref="NutritionMapper"/>.
/// </summary>
public sealed class VddApiNutritionDataProvider : INutritionDataProvider
{
    public const string HttpClientName = "vdd";
    private static readonly ConcurrentDictionary<Guid, ResiliencePipeline<HttpResponseMessage>> Pipelines = new();
    private static readonly ConcurrentDictionary<Guid, (string Token, DateTimeOffset Expires)> Tokens = new();

    private readonly HttpClient _http;
    private readonly IntegrationEndpoint _ep;
    private readonly MappingConfig _map;
    private readonly EndpointCredentials _cred;
    private readonly IReadOnlyDictionary<string, string> _localUnits;
    private readonly TimeProvider _clock;

    public List<CallInfo> Calls { get; } = [];

    public VddApiNutritionDataProvider(HttpClient http, IntegrationEndpoint endpoint, IReadOnlyDictionary<string, string> localUnits, TimeProvider clock)
    {
        _http = http;
        _ep = endpoint;
        _map = MappingConfig.Parse(endpoint.MappingJson);
        _cred = EndpointCredentials.Parse(endpoint.Credentials);
        _localUnits = localUnits;
        _clock = clock;
        _http.BaseAddress = new Uri(endpoint.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = Timeout.InfiniteTimeSpan; // the pipeline enforces timeouts
    }

    /// <summary>Removes the cached circuit/token of an endpoint (after its configuration changed).</summary>
    public static void Reset(Guid endpointId)
    {
        Pipelines.TryRemove(endpointId, out _);
        Tokens.TryRemove(endpointId, out _);
    }

    private ResiliencePipeline<HttpResponseMessage> Pipeline => Pipelines.GetOrAdd(_ep.Id, _ => new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true, Delay = TimeSpan.FromMilliseconds(300),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().Handle<TimeoutRejectedException>()
                .HandleResult(r => r.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)r.StatusCode >= 500),
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = 0.5, MinimumThroughput = 6, SamplingDuration = TimeSpan.FromSeconds(60), BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().Handle<TimeoutRejectedException>().HandleResult(r => (int)r.StatusCode >= 500),
        })
        .AddTimeout(TimeSpan.FromSeconds(Math.Clamp(_ep.TimeoutSeconds, 3, 300)))
        .Build());

    private string Path(string key) => (_map.Paths.TryGetValue(key, out var p) ? p : key).TrimStart('/');

    private async Task AuthorizeAsync(HttpRequestMessage req, CancellationToken ct)
    {
        switch (_ep.AuthType)
        {
            case IntegrationAuthType.ApiKey when !string.IsNullOrEmpty(_cred.ApiKey):
                req.Headers.TryAddWithoutValidation(_cred.ApiKeyHeader, _cred.ApiKey);
                break;
            case IntegrationAuthType.Basic:
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_cred.Username}:{_cred.Password}")));
                break;
            case IntegrationAuthType.OAuth2ClientCredentials:
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
                break;
        }
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (Tokens.TryGetValue(_ep.Id, out var t) && t.Expires > _clock.GetUtcNow().AddSeconds(30)) return t.Token;
        var url = string.IsNullOrWhiteSpace(_cred.TokenUrl) ? Path("token") : _cred.TokenUrl!;
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = _cred.ClientId ?? "", ["client_secret"] = _cred.ClientSecret ?? "", ["scope"] = _cred.Scope ?? "" }),
        };
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new IntegrationException("integration.authFailed", (int)res.StatusCode);
        var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        var token = doc.GetProperty("access_token").GetString() ?? throw new IntegrationException("integration.authFailed", 0);
        var expires = doc.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600;
        Tokens[_ep.Id] = (token, _clock.GetUtcNow().AddSeconds(expires));
        return token;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string operation, string url, object? body, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage res;
        try
        {
            res = await Pipeline.ExecuteAsync(async token =>
            {
                using var req = new HttpRequestMessage(method, url);
                if (body is not null) req.Content = JsonContent.Create(body);
                await AuthorizeAsync(req, token);
                var r = await _http.SendAsync(req, token);
                if (r.StatusCode == HttpStatusCode.Unauthorized && _ep.AuthType == IntegrationAuthType.OAuth2ClientCredentials) Tokens.TryRemove(_ep.Id, out _);
                return r;
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            Calls.Add(new CallInfo(operation, url, null, (int)started.ElapsedMilliseconds, 0));
            throw new IntegrationException("integration.circuitOpen", 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Calls.Add(new CallInfo(operation, url, null, (int)started.ElapsedMilliseconds, 0));
            throw new IntegrationException("integration.unreachable", 0, ex.Message);
        }
        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                Calls.Add(new CallInfo(operation, url, (int)res.StatusCode, (int)started.ElapsedMilliseconds, 0));
                throw new IntegrationException(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "integration.authFailed" : "integration.httpError", (int)res.StatusCode);
            }
            var doc = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
            Calls.Add(new CallInfo(operation, url, (int)res.StatusCode, (int)started.ElapsedMilliseconds, NutritionMapper.Items(doc, _map).Count()));
            return doc;
        }
    }

    private string Q(string path, DateTimeOffset? modifiedSince, int? page = null, int? pageSize = null)
    {
        var q = new List<string>();
        if (modifiedSince is { } m) q.Add($"{_map.Query.GetValueOrDefault("modifiedSince", "modified_since")}={Uri.EscapeDataString(m.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
        if (page is { } p) q.Add($"{_map.Query.GetValueOrDefault("page", "page")}={p}");
        if (pageSize is { } s) q.Add($"{_map.Query.GetValueOrDefault("pageSize", "page_size")}={s}");
        return q.Count == 0 ? path : $"{path}?{string.Join('&', q)}";
    }

    public async Task<IReadOnlyList<ExtNutrient>> GetNutrientsAsync(CancellationToken ct)
        => NutritionMapper.Nutrients(await SendAsync(HttpMethod.Get, "GetNutrients", Path("nutrients"), null, ct), _map);

    public async Task<IReadOnlyList<ExtFoodGroup>> GetFoodGroupsAsync(DateTimeOffset? modifiedSince, CancellationToken ct)
        => NutritionMapper.FoodGroups(await SendAsync(HttpMethod.Get, "GetFoodGroups", Q(Path("foodGroups"), modifiedSince), null, ct), _map);

    public async Task<PagedResult<ExtFood>> GetFoodsAsync(DateTimeOffset? modifiedSince, int page, int pageSize, CancellationToken ct)
        => NutritionMapper.Foods(await SendAsync(HttpMethod.Get, "GetFoods", Q(Path("foods"), modifiedSince, page, pageSize), null, ct), _map, _localUnits, page, pageSize);

    public async Task<IReadOnlyList<ExtRecommendation>> GetRecommendationsAsync(DateTimeOffset? modifiedSince, CancellationToken ct)
        => NutritionMapper.Recommendations(await SendAsync(HttpMethod.Get, "GetRecommendations", Q(Path("recommendations"), modifiedSince), null, ct), _map);

    public async Task<PushResult> PushAggregatedReportAsync(NutritionSummaryPayload payload, CancellationToken ct)
    {
        var doc = await SendAsync(HttpMethod.Post, "PushAggregatedReport", Path("push"), payload, ct);
        return new PushResult(NutritionMapper.Get(doc, "accepted") is { ValueKind: JsonValueKind.True }, NutritionMapper.Get(doc, "receipt_id")?.GetString(), NutritionMapper.Get(doc, "message")?.GetString());
    }
}

/// <summary>Integration failure carrying a localizable key (rendered as 502 Bad Gateway).</summary>
public sealed class IntegrationException(string key, int statusCode, string? detail = null) : AppException(key, 502, statusCode, detail ?? "")
{
    public int RemoteStatus { get; } = statusCode;
    public string? Detail { get; } = detail;
}
