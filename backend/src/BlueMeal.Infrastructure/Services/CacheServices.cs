using System.Collections.Concurrent;
using System.Text.Json;
using BlueMeal.Application.Abstractions;
using StackExchange.Redis;

namespace BlueMeal.Infrastructure.Services;

public sealed class RedisCacheService(IConnectionMultiplexer redis) : ICacheService
{
    private const string Prefix = "bm:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var v = await Db.StringGetAsync(Prefix + key);
        return v.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(v!, Json);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        => Db.StringSetAsync(Prefix + key, JsonSerializer.Serialize(value, Json), ttl);

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        var v = await Db.StringGetAsync(Prefix + key);
        if (!v.IsNullOrEmpty) return JsonSerializer.Deserialize<T>(v!, Json)!;
        var created = await factory();
        await SetAsync(key, created, ttl, ct);
        return created;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => Db.KeyDeleteAsync(Prefix + key);

    public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Db.StringIncrementAsync(Prefix + "region:" + region);

    public async Task<string> RegionKeyAsync(string region, string key, CancellationToken ct = default)
    {
        var v = await Db.StringGetAsync(Prefix + "region:" + region);
        return $"{region}:{(v.IsNullOrEmpty ? "0" : v.ToString())}:{key}";
    }

    public Task<bool> TrySetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
        => Db.StringSetAsync(Prefix + key, value, ttl, When.NotExists);

    public async Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var v = await Db.StringIncrementAsync(Prefix + key);
        if (v == 1) await Db.KeyExpireAsync(Prefix + key, ttl);
        return v;
    }
}

/// <summary>In-process fallback cache (single node / tests). Not suitable for multi-replica deployments.</summary>
public sealed class MemoryCacheService(TimeProvider clock) : ICacheService
{
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset Expires)> _store = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private bool TryGetRaw(string key, out string value)
    {
        if (_store.TryGetValue(key, out var e) && e.Expires > clock.GetUtcNow()) { value = e.Value; return true; }
        _store.TryRemove(key, out _);
        value = "";
        return false;
    }

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
        => Task.FromResult(TryGetRaw(key, out var v) ? JsonSerializer.Deserialize<T>(v, Json) : default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[key] = (JsonSerializer.Serialize(value, Json), clock.GetUtcNow() + ttl);
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken ct = default)
    {
        if (TryGetRaw(key, out var v)) return JsonSerializer.Deserialize<T>(v, Json)!;
        var created = await factory();
        await SetAsync(key, created, ttl, ct);
        return created;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) { _store.TryRemove(key, out _); return Task.CompletedTask; }

    public Task InvalidateRegionAsync(string region, CancellationToken ct = default)
    {
        var k = "region:" + region;
        var cur = TryGetRaw(k, out var v) ? long.Parse(v) : 0;
        _store[k] = ((cur + 1).ToString(), DateTimeOffset.MaxValue);
        return Task.CompletedTask;
    }

    public Task<string> RegionKeyAsync(string region, string key, CancellationToken ct = default)
        => Task.FromResult($"{region}:{(TryGetRaw("region:" + region, out var v) ? v : "0")}:{key}");

    public Task<bool> TrySetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        if (TryGetRaw(key, out _)) return Task.FromResult(false);
        return Task.FromResult(_store.TryAdd(key, (value, clock.GetUtcNow() + ttl)) || false);
    }

    public Task<long> IncrementAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_store)
        {
            var cur = TryGetRaw(key, out var v) ? long.Parse(v) : 0;
            var exp = _store.TryGetValue(key, out var e) ? e.Expires : clock.GetUtcNow() + ttl;
            _store[key] = ((cur + 1).ToString(), exp);
            return Task.FromResult(cur + 1);
        }
    }
}
