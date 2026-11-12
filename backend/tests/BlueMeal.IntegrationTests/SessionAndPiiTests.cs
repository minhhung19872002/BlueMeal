using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Persistence;
using BlueMeal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace BlueMeal.IntegrationTests;

/// <summary>SPEC §10.1/§10.4/§10.6 checks referenced by docs/03_ThietKe_ATTT_CapDo2.md: sessions, password expiry, TOTP replay, upload validation, personal data rights.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SessionAndPiiTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private async Task<T> InDb<T>(Func<AppDbContext, Task<T>> fn)
    {
        using var scope = f.Services.CreateScope();
        return await fn(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Concurrent_session_limit_revokes_the_oldest_session()
    {
        var (u, p) = await f.CreateUserAsync("sess_limit", "HEAD_CHEF");
        var clients = new List<HttpClient>();
        for (var i = 0; i < 4; i++) clients.Add(await f.LoginAsync(u, p)); // default MaxConcurrentSessions = 3
        (await clients[0].GetAsync("/api/v1/profile/sessions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await clients[3].GetAsync("/api/v1/profile/sessions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Idle_session_expires_after_the_configured_timeout()
    {
        var (u, p) = await f.CreateUserAsync("sess_idle", "HEAD_CHEF");
        var c = await f.LoginAsync(u, p);
        (await c.GetAsync("/api/v1/profile/sessions")).StatusCode.Should().Be(HttpStatusCode.OK);
        // simulate 16 minutes without activity (default idle timeout 15): the sliding cache entry (TTL = idle timeout) is gone and last_seen is old
        var sessionIds = await InDb(async db =>
        {
            var uid = await db.Users.Where(x => x.UserName == u).Select(x => x.Id).FirstAsync();
            await db.Set<UserSession>().Where(s => s.UserId == uid).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow.AddMinutes(-16)));
            return await db.Set<UserSession>().Where(s => s.UserId == uid).Select(s => s.Id).ToListAsync();
        });
        using (var scope = f.Services.CreateScope())
        {
            var cache = scope.ServiceProvider.GetRequiredService<BlueMeal.Application.Abstractions.ICacheService>();
            foreach (var sid in sessionIds) await cache.RemoveAsync(BlueMeal.Api.Auth.AuthService.SessionKey(sid));
        }
        (await c.GetAsync("/api/v1/profile/sessions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_password_gives_a_restricted_session()
    {
        var (u, p) = await f.CreateUserAsync("pwd_expired", "HEAD_CHEF");
        await InDb(db => db.Users.Where(x => x.UserName == u).ExecuteUpdateAsync(s => s.SetProperty(x => x.PasswordChangedAt, DateTimeOffset.UtcNow.AddDays(-91))));
        var c = f.NewClient();
        var login = await J(await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = p }));
        login.GetProperty("restricted").GetBoolean().Should().BeTrue();
        login.GetProperty("user").GetProperty("passwordExpired").GetBoolean().Should().BeTrue();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        (await c.GetAsync("/api/v1/dishes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_totp_code_cannot_be_replayed_at_login()
    {
        await f.AdminAsync();
        var code = new Totp(Base32Encoding.ToBytes(f.AdminTotpSecret!)).ComputeTotp(DateTime.UtcNow.AddSeconds(-30));
        async Task<HttpStatusCode> LoginWith(string totp)
        {
            var c = f.NewClient();
            var login = await J(await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = f.AdminCurrentPassword }));
            login.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
            return (await c.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { mfaToken = login.GetProperty("mfaToken").GetString(), code = totp })).StatusCode;
        }
        (await LoginWith(code)).Should().Be(HttpStatusCode.OK);
        (await LoginWith(code)).Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_rejects_content_that_does_not_match_the_extension()
    {
        var c = await f.AdminAsync();
        using var form = new MultipartFormDataContent();
        var fake = new ByteArrayContent("MZ\u0090\0\u0003 this is an executable"u8.ToArray());
        fake.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fake, "file", "logo.png");
        form.Add(new StringContent("Dish"), "ownerType");
        var r = await c.PostAsync("/api/v1/files", form);
        ((int)r.StatusCode).Should().BeInRange(400, 422);
    }

    [Fact]
    public async Task Personal_data_export_and_anonymization_are_audited()
    {
        var c = await f.AdminAsync();
        var e = await J(await c.PostAsJsonAsync("/api/v1/employees", new { code = "NV-DSAR-1", fullName = "Lê Văn Quyền Riêng", phone = "0912345678", email = "quyenrieng@test.vn", nationalId = "079123456789", gender = "Male", isActive = true }));
        var id = e.GetProperty("id").GetString();
        var data = await c.GetAsync($"/api/v1/employees/{id}/personal-data");
        data.StatusCode.Should().Be(HttpStatusCode.OK);
        (await data.Content.ReadAsStringAsync()).Should().Contain("0912345678");
        (await c.PostAsync($"/api/v1/employees/{id}/anonymize", null)).IsSuccessStatusCode.Should().BeTrue();
        var after = await J(await c.GetAsync($"/api/v1/employees/{id}"));
        after.GetRawText().Should().NotContain("Quyền Riêng").And.NotContain("0912345678");
        var audit = await InDb(db => db.Set<AuditLog>().Where(a => a.EntityId == id && a.Action == BlueMeal.Domain.Enums.AuditAction.PiiAccess).CountAsync());
        audit.Should().BeGreaterThan(0);
    }
}
