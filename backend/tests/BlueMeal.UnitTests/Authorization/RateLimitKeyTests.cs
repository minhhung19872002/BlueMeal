using System.Net;
using System.Security.Claims;
using BlueMeal.Api.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace BlueMeal.UnitTests.Authorization;

/// <summary>Regression: the per-user limiter keyed on "sub", which the JWT handler renames — all users shared one bucket (found by the k6 load test).</summary>
public class RateLimitKeyTests
{
    private static HttpContext Ctx(params Claim[] claims)
    {
        var c = new DefaultHttpContext();
        c.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        if (claims.Length > 0) c.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
        return c;
    }

    [Fact]
    public void Each_authenticated_user_has_its_own_partition()
    {
        var a = RateLimitKeys.ForRequest(Ctx(new Claim(ClaimTypes.NameIdentifier, "user-a")));
        var b = RateLimitKeys.ForRequest(Ctx(new Claim(ClaimTypes.NameIdentifier, "user-b")));
        a.Should().Be("u:user-a");
        b.Should().Be("u:user-b");
    }

    [Fact]
    public void Raw_sub_claim_is_also_accepted()
        => RateLimitKeys.ForRequest(Ctx(new Claim("sub", "user-c"))).Should().Be("u:user-c");

    [Fact]
    public void Anonymous_requests_are_partitioned_by_ip()
        => RateLimitKeys.ForRequest(Ctx()).Should().Be("ip:10.0.0.5");
}
