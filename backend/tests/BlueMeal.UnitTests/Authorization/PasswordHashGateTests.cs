using BlueMeal.Api.Auth;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BlueMeal.UnitTests.Authorization;

public class PasswordHashGateTests
{
    private static IConfiguration Config(string? limit) => new ConfigurationBuilder()
        .AddInMemoryCollection(limit is null ? [] : new Dictionary<string, string?> { ["Auth:MaxConcurrentPasswordHashes"] = limit }).Build();

    [Fact]
    public void Default_limit_is_a_quarter_of_the_cores_and_at_least_two()
        => PasswordHashGate.Limit(Config(null)).Should().Be(Math.Max(2, Environment.ProcessorCount / 4));

    [Fact]
    public void Configured_limit_wins() => PasswordHashGate.Limit(Config("3")).Should().Be(3);

    [Fact]
    public async Task Never_runs_more_than_the_limit_at_once()
    {
        var gate = new PasswordHashGate(Config("2"));
        var running = 0; var peak = 0;
        var tasks = Enumerable.Range(0, 10).Select(_ => gate.RunAsync(async () =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
            return true;
        }, CancellationToken.None));
        (await Task.WhenAll(tasks)).Should().AllBeEquivalentTo(true);
        peak.Should().Be(2);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = target) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}
