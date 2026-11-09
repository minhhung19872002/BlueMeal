using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;

namespace BlueMeal.IntegrationTests;

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

[Collection(nameof(ApiCollection))]
public sealed class AuthTests(ApiFactory f)
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);

    [Fact]
    public async Task Health_endpoints_respond()
    {
        var c = f.NewClient();
        (await c.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Errors_are_problem_details_localized_by_accept_language()
    {
        var vi = await f.NewClient("vi").PostAsJsonAsync("/api/v1/auth/login", new { userName = "nobody", password = "x" });
        vi.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        vi.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var pvi = await Json(vi);
        pvi.GetProperty("detail").GetString().Should().Be("Tên đăng nhập hoặc mật khẩu không đúng.");
        pvi.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();

        var en = await Json(await f.NewClient("en").PostAsJsonAsync("/api/v1/auth/login", new { userName = "nobody", password = "x" }));
        en.GetProperty("detail").GetString().Should().Be("Invalid username or password.");
    }

    [Fact]
    public async Task Unknown_and_wrong_password_return_the_same_neutral_message()
    {
        var (u, _) = await f.CreateUserAsync("neutral1", "NUTRITIONIST");
        var a = await Json(await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = "Wrong@123456" }));
        var b = await Json(await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = "does-not-exist", password = "Wrong@123456" }));
        a.GetProperty("detail").GetString().Should().Be(b.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Account_is_locked_after_5_failures_and_captcha_required_after_3()
    {
        var (u, pwd) = await f.CreateUserAsync("lockme", "NUTRITIONIST");
        var c = f.NewClient();
        for (var i = 0; i < 3; i++)
            (await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = "Bad@Password1" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Json(await c.GetAsync($"/api/v1/auth/captcha-status?userName={u}"))).GetProperty("required").GetBoolean().Should().BeTrue();
        // without captcha → rejected with captchaRequired
        var noCaptcha = await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd });
        noCaptcha.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(noCaptcha)).GetProperty("code").GetString().Should().Be("auth.captchaRequired");

        // two more failures with solved captchas → locked (5th failure)
        for (var i = 0; i < 2; i++)
        {
            var (id, answer) = await SolveCaptcha(c);
            var r = await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = "Bad@Password1", captchaId = id, captchaAnswer = answer });
            if (i == 1) { r.StatusCode.Should().Be((HttpStatusCode)423); (await Json(r)).GetProperty("code").GetString().Should().Be("auth.locked"); }
        }
        // even the correct password is refused while locked
        var (id2, ans2) = await SolveCaptcha(c);
        var locked = await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd, captchaId = id2, captchaAnswer = ans2 });
        locked.StatusCode.Should().Be((HttpStatusCode)423);
    }

    /// <summary>Captcha answers are only stored hashed; tests read the challenge text from the SVG.</summary>
    private static async Task<(string Id, string Answer)> SolveCaptcha(HttpClient c)
    {
        var ch = await Json(await c.GetAsync("/api/v1/auth/captcha"));
        var svg = ch.GetProperty("svg").GetString()!;
        var text = string.Concat(System.Text.RegularExpressions.Regex.Matches(svg, ">([^<]{1})</text>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
        var m2 = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)([+−])(\d+)");
        var a = int.Parse(m2.Groups[1].Value); var b = int.Parse(m2.Groups[3].Value);
        return (ch.GetProperty("id").GetString()!, (m2.Groups[2].Value == "+" ? a + b : a - b).ToString());
    }

    [Fact]
    public async Task Admin_first_login_is_restricted_until_password_changed_and_mfa_enabled()
    {
        // A fresh admin-like account: SYS_ADMIN role mandates MFA
        var (u, pwd) = await f.CreateUserAsync("admin2", "SYS_ADMIN");
        var c = f.NewClient();
        var login = await Json(await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd }));
        login.GetProperty("restricted").GetBoolean().Should().BeTrue();
        login.GetProperty("mfaSetupRequired").GetBoolean().Should().BeTrue();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());

        // restricted token cannot use business APIs
        var blocked = await c.GetAsync("/api/v1/users");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(blocked)).GetProperty("code").GetString().Should().Be("restricted_session");
        (await c.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // enroll TOTP
        var setup = await Json(await c.PostAsync("/api/v1/profile/mfa/setup", null));
        var secret = setup.GetProperty("secret").GetString()!;
        (await c.PostAsJsonAsync("/api/v1/profile/mfa/enable", new { code = "000000" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var enabled = await Json(await c.PostAsJsonAsync("/api/v1/profile/mfa/enable", new { code = ApiFactory.Totp(secret) }));
        enabled.GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
        var refreshed = await f.Refresh(c);
        refreshed.GetProperty("restricted").GetBoolean().Should().BeFalse();
        (await c.GetAsync("/api/v1/users")).StatusCode.Should().Be(HttpStatusCode.OK);

        // next login requires the second factor
        var c2 = f.NewClient();
        var step1 = await Json(await c2.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd }));
        step1.GetProperty("mfaRequired").GetBoolean().Should().BeTrue();
        step1.GetProperty("accessToken").ValueKind.Should().Be(JsonValueKind.Null);
        var mfaToken = step1.GetProperty("mfaToken").GetString();
        (await c2.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { mfaToken, code = "123456", method = "totp" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var ok = await c2.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { mfaToken, code = ApiFactory.Totp(secret), method = "totp" });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // recovery code works exactly once
        var code = enabled.GetProperty("recoveryCodes")[0].GetString();
        var c3 = f.NewClient();
        var s3 = await Json(await c3.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd }));
        (await c3.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { mfaToken = s3.GetProperty("mfaToken").GetString(), code, method = "recovery" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var c4 = f.NewClient();
        var s4 = await Json(await c4.PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd }));
        (await c4.PostAsJsonAsync("/api/v1/auth/mfa/verify", new { mfaToken = s4.GetProperty("mfaToken").GetString(), code, method = "recovery" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_token_rotates_and_reuse_revokes_the_session()
    {
        var (u, pwd) = await f.CreateUserAsync("rotate1", "NUTRITIONIST");
        var c = await f.LoginAsync(u, pwd);
        var jar = ApiFactory.JarOf(c);
        var first = jar.GetAllCookies().First(k => k.Name == "bm_rt").Value;
        await f.Refresh(c);
        var second = jar.GetAllCookies().First(k => k.Name == "bm_rt").Value;
        second.Should().NotBe(first);

        // within the 30 s grace window a concurrent refresh with the previous token is tolerated (page reload race)…
        var racer = f.NewClient();
        var race = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        race.Headers.Add("Cookie", $"bm_rt={first}; bm_csrf=x");
        race.Headers.Add("X-BlueMeal-CSRF", "x");
        (await racer.SendAsync(race)).StatusCode.Should().Be(HttpStatusCode.OK);
        // …but replaying it after the grace window is treated as theft and revokes the session
        await using (var conn = new Npgsql.NpgsqlConnection(f.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("UPDATE app.user_session SET rotated_at = now() - interval '5 minutes' WHERE previous_token_hash IS NOT NULL", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        var attacker = f.NewClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("Cookie", $"bm_rt={first}; bm_csrf=x");
        req.Headers.Add("X-BlueMeal-CSRF", "x");
        (await attacker.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        // the legitimate session is now revoked too
        (await c.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_requires_csrf_header()
    {
        var (u, pwd) = await f.CreateUserAsync("csrf1", "NUTRITIONIST");
        var c = await f.LoginAsync(u, pwd);
        var res = await c.PostAsync("/api/v1/auth/refresh", null);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(res)).GetProperty("code").GetString().Should().Be("auth.csrf");
    }

    [Fact]
    public async Task Refresh_cookie_is_httponly_samesite_strict()
    {
        var (u, pwd) = await f.CreateUserAsync("cookie1", "NUTRITIONIST");
        var res = await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = pwd });
        var cookies = res.Headers.GetValues("Set-Cookie").ToList();
        cookies.Should().Contain(x => x.StartsWith("bm_rt=") && x.Contains("httponly", StringComparison.OrdinalIgnoreCase) && x.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase) && x.Contains("path=/api/v1/auth"));
        (await Json(res)).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Logout_and_admin_force_logout_revoke_sessions()
    {
        var (u, pwd) = await f.CreateUserAsync("logout1", "NUTRITIONIST");
        var c = await f.LoginAsync(u, pwd);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        req.Headers.Add("X-BlueMeal-CSRF", ApiFactory.CsrfFrom(c));
        (await c.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var c2 = await f.LoginAsync(u, pwd);
        var admin = await f.AdminAsync();
        var me = await Json(await c2.GetAsync("/api/v1/auth/me"));
        var userId = me.GetProperty("id").GetString();
        var revoked = await Json(await admin.DeleteAsync($"/api/v1/sessions/user/{userId}"));
        revoked.GetProperty("revoked").GetInt32().Should().BeGreaterThan(0);
        (await c2.GetAsync("/api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_policy_and_history_are_enforced()
    {
        var (u, pwd) = await f.CreateUserAsync("pwd1", "NUTRITIONIST");
        var c = await f.LoginAsync(u, pwd);
        (await c.PostAsJsonAsync("/api/v1/profile/change-password", new { currentPassword = pwd, newPassword = "short" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/v1/profile/change-password", new { currentPassword = pwd, newPassword = "nouppercase1!x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reuse = await c.PostAsJsonAsync("/api/v1/profile/change-password", new { currentPassword = pwd, newPassword = pwd });
        reuse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(reuse)).GetProperty("code").GetString().Should().Be("auth.passwordReused");
        (await c.PostAsJsonAsync("/api/v1/profile/change-password", new { currentPassword = pwd, newPassword = "Brand@New2026x" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Forgot_password_is_neutral_and_reset_token_is_single_use()
    {
        var c = f.NewClient();
        var a = await Json(await c.PostAsJsonAsync("/api/v1/auth/forgot-password", new { userNameOrEmail = "ghost@nowhere" }));
        a.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
        (await c.PostAsJsonAsync("/api/v1/auth/reset-password", new { token = "invalid", newPassword = "Brand@New2026x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (u, _) = await f.CreateUserAsync("forgot1", "NUTRITIONIST");
        var b = await Json(await c.PostAsJsonAsync("/api/v1/auth/forgot-password", new { userNameOrEmail = u }));
        b.GetProperty("message").GetString().Should().Be(a.GetProperty("message").GetString()); // identical response
        var mail = f.Emails.Sent.Last(m => m.To == u + "@test.local");
        var token = System.Text.RegularExpressions.Regex.Match(mail.Body, "token=([^\"&<]+)").Groups[1].Value;
        token = Uri.UnescapeDataString(token);
        (await c.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Reset@Pass2026x" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.PostAsJsonAsync("/api/v1/auth/reset-password", new { token, newPassword = "Reset@Pass2026y" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = u, password = "Reset@Pass2026x" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
