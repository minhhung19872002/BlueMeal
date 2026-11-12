using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.Api.Services;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using BlueMeal.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMeal.IntegrationTests;

/// <summary>Personal-data notice and consent (Luật 91/2025/QH15); automatic disabling of inactive accounts (cấp độ 2).</summary>
[Collection(nameof(ApiCollection))]
public sealed class PrivacyAndInactiveAccountTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
    }

    [Fact]
    public async Task Consent_is_required_recorded_and_requested_again_when_the_notice_changes()
    {
        var admin = await f.AdminAsync();
        var (u, pwd) = await f.CreateUserAsync("privacy_user1", "HEAD_CHEF");
        var c = await f.LoginAsync(u, pwd);

        (await J(await c.GetAsync("/api/v1/auth/me"))).GetProperty("privacyConsentRequired").GetBoolean().Should().BeTrue();
        var notice = await J(await c.GetAsync("/api/v1/profile/privacy-notice"));
        notice.GetProperty("enabled").GetBoolean().Should().BeTrue();
        notice.GetProperty("accepted").GetBoolean().Should().BeFalse();
        notice.GetProperty("contentVi").GetString().Should().Contain("91/2025/QH15");
        var version = notice.GetProperty("version").GetInt32();

        (await c.PostAsJsonAsync("/api/v1/profile/privacy-consent", new { version = version + 5, language = "vi" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await c.PostAsJsonAsync("/api/v1/profile/privacy-consent", new { version, language = "vi" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync("/api/v1/profile/privacy-consent", new { version, language = "vi" })).StatusCode.Should().Be(HttpStatusCode.NoContent); // idempotent
        (await J(await c.GetAsync("/api/v1/auth/me"))).GetProperty("privacyConsentRequired").GetBoolean().Should().BeFalse();

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var uid = await db.Users.Where(x => x.UserName == u).Select(x => x.Id).SingleAsync();
            var rows = await db.Set<PrivacyConsent>().Where(x => x.UserId == uid).ToListAsync();
            rows.Should().ContainSingle().Which.NoticeVersion.Should().Be(version);
            (await db.Set<AuditLog>().AnyAsync(a => a.Action == AuditAction.Consent && a.EntityId == uid.ToString())).Should().BeTrue();
            var detail = await J(await admin.GetAsync($"/api/v1/users/{uid}"));
            detail.GetProperty("privacyConsent").GetProperty("noticeVersion").GetInt32().Should().Be(version);
        }

        // saving the same text keeps the version; a new text bumps it and asks everybody again
        var current = await J(await admin.GetAsync("/api/v1/settings/privacy.notice"));
        var vi = current.GetProperty("contentVi").GetString()!;
        var en = current.GetProperty("contentEn").GetString()!;
        try
        {
            (await admin.PutAsJsonAsync("/api/v1/settings/privacy.notice", new { enabled = true, version = 999, contentVi = vi, contentEn = en })).EnsureSuccessStatusCode();
            (await J(await admin.GetAsync("/api/v1/settings/privacy.notice"))).GetProperty("version").GetInt32().Should().Be(version);

            (await admin.PutAsJsonAsync("/api/v1/settings/privacy.notice", new { enabled = true, contentVi = vi + "\n7. Bổ sung.", contentEn = en })).EnsureSuccessStatusCode();
            (await J(await admin.GetAsync("/api/v1/settings/privacy.notice"))).GetProperty("version").GetInt32().Should().Be(version + 1);
            (await J(await c.GetAsync("/api/v1/auth/me"))).GetProperty("privacyConsentRequired").GetBoolean().Should().BeTrue();
            (await c.PostAsJsonAsync("/api/v1/profile/privacy-consent", new { version, language = "vi" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

            (await admin.PutAsJsonAsync("/api/v1/settings/privacy.notice", new { enabled = true, contentVi = "  ", contentEn = en })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.PutAsJsonAsync("/api/v1/settings/privacy.notice", new { enabled = false, contentVi = vi + "\n7. Bổ sung.", contentEn = en })).EnsureSuccessStatusCode();
            (await J(await c.GetAsync("/api/v1/auth/me"))).GetProperty("privacyConsentRequired").GetBoolean().Should().BeFalse();
        }
        finally
        {
            // restore the original text (as a new version) for the other tests
            (await admin.PutAsJsonAsync("/api/v1/settings/privacy.notice", new { enabled = true, contentVi = vi, contentEn = en })).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Inactive_accounts_are_disabled_after_the_configured_days()
    {
        var admin = await f.AdminAsync();
        var (stale, pwd) = await f.CreateUserAsync("inactive_stale", "HEAD_CHEF");
        var (reactivated, _) = await f.CreateUserAsync("inactive_reactivated", "HEAD_CHEF");
        var (fresh, _) = await f.CreateUserAsync("inactive_fresh", "HEAD_CHEF");
        var (neverLoggedIn, _) = await f.CreateUserAsync("inactive_never", "HEAD_CHEF");
        var staleClient = await f.LoginAsync(stale, pwd);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        async Task<Guid> Id(string name) => await db.Users.Where(x => x.UserName == name).Select(x => x.Id).SingleAsync();
        await db.Users.Where(x => x.UserName == stale).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLoginAt, now.AddDays(-46)).SetProperty(x => x.CreatedAt, now.AddDays(-100)));
        await db.Users.Where(x => x.UserName == reactivated).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLoginAt, now.AddDays(-80)).SetProperty(x => x.CreatedAt, now.AddDays(-100)).SetProperty(x => x.ReactivatedAt, now.AddDays(-2)));
        await db.Users.Where(x => x.UserName == fresh).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLoginAt, now.AddDays(-44)).SetProperty(x => x.CreatedAt, now.AddDays(-100)));
        await db.Users.Where(x => x.UserName == neverLoggedIn).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastLoginAt, (DateTimeOffset?)null).SetProperty(x => x.CreatedAt, now.AddDays(-60)));

        var job = scope.ServiceProvider.GetServices<IAppJob>().OfType<InactiveAccountsJob>().Single();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var policy = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy);
        policy.InactiveDisableDays.Should().Be(45);

        // 0 switches the job off
        policy.InactiveDisableDays = 0;
        await settings.SetAsync(SettingKeys.PasswordPolicy, policy);
        await job.ExecuteAsync(new Dictionary<string, string>(), CancellationToken.None);
        (await db.Users.AsNoTracking().SingleAsync(x => x.UserName == stale)).IsActive.Should().BeTrue();

        policy.InactiveDisableDays = 45;
        await settings.SetAsync(SettingKeys.PasswordPolicy, policy);
        await job.ExecuteAsync(new Dictionary<string, string>(), CancellationToken.None);
        db.ChangeTracker.Clear();
        (await db.Users.SingleAsync(x => x.UserName == stale)).IsActive.Should().BeFalse();
        (await db.Users.SingleAsync(x => x.UserName == neverLoggedIn)).IsActive.Should().BeFalse();
        (await db.Users.SingleAsync(x => x.UserName == reactivated)).IsActive.Should().BeTrue();
        (await db.Users.SingleAsync(x => x.UserName == fresh)).IsActive.Should().BeTrue();
        var staleId = await Id(stale);
        (await db.Set<AuditLog>().AnyAsync(a => a.Action == AuditAction.Lock && a.EntityId == staleId.ToString() && a.Message!.StartsWith("inactive_45d"))).Should().BeTrue();

        // sessions are revoked and the password no longer signs in
        (await staleClient.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = stale, password = pwd })).IsSuccessStatusCode.Should().BeFalse();

        // an administrator re-enables the account: the inactivity clock restarts, so the next run keeps it
        (await admin.PostAsync($"/api/v1/users/{staleId}/unlock", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await job.ExecuteAsync(new Dictionary<string, string>(), CancellationToken.None);
        db.ChangeTracker.Clear();
        var back = await db.Users.SingleAsync(x => x.UserName == stale);
        back.IsActive.Should().BeTrue();
        back.ReactivatedAt.Should().NotBeNull();
        (await f.LoginAsync(stale, pwd)).Should().NotBeNull();
    }

    [Fact]
    public void The_last_active_administrator_is_never_disabled()
    {
        Guid a1 = Guid.NewGuid(), a2 = Guid.NewGuid(), u1 = Guid.NewGuid();
        InactiveAccountsJob.SelectToDisable([a1, u1], [a1]).Should().Equal(u1);
        InactiveAccountsJob.SelectToDisable([a1, a2, u1], [a1, a2]).Should().Equal(a1, u1);
        InactiveAccountsJob.SelectToDisable([a1], [a1, a2]).Should().Equal(a1);
        InactiveAccountsJob.SelectToDisable([u1], []).Should().Equal(u1);
    }
}
