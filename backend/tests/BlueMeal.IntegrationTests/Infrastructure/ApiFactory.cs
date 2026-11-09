using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueMeal.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OtpNet;
using Testcontainers.PostgreSql;

namespace BlueMeal.IntegrationTests.Infrastructure;

/// <summary>
/// Spins up a real PostgreSQL 16 (Testcontainers) with the same least-privilege roles as production
/// (bluemeal_owner runs migrations, bluemeal_app is used by the API) and hosts the API in-memory.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "Admin@Test2026!";
    public const string OwnerPassword = "owner_test_pwd";
    public const string AppPassword = "app_test_pwd";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("bluemeal")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string SuperConnectionString => _pg.GetConnectionString();
    public string OwnerConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Username = "bluemeal_owner", Password = OwnerPassword }.ToString();
    public string AppConnectionString => new NpgsqlConnectionStringBuilder(_pg.GetConnectionString()) { Username = "bluemeal_app", Password = AppPassword }.ToString();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var conn = new NpgsqlConnection(SuperConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Mirrors deploy/postgres/init.sql
        cmd.CommandText = $"""
            CREATE ROLE bluemeal_owner LOGIN CREATEDB PASSWORD '{OwnerPassword}';
            CREATE ROLE bluemeal_app LOGIN PASSWORD '{AppPassword}';
            ALTER DATABASE bluemeal OWNER TO bluemeal_owner;
            CREATE EXTENSION IF NOT EXISTS pgcrypto; CREATE EXTENSION IF NOT EXISTS unaccent; CREATE EXTENSION IF NOT EXISTS pg_trgm;
            CREATE SCHEMA app AUTHORIZATION bluemeal_owner;
            CREATE SCHEMA quartz AUTHORIZATION bluemeal_owner;
            GRANT CONNECT ON DATABASE bluemeal TO bluemeal_app;
            GRANT USAGE ON SCHEMA app, quartz TO bluemeal_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO bluemeal_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT USAGE, SELECT ON SEQUENCES TO bluemeal_app;
            ALTER DEFAULT PRIVILEGES FOR ROLE bluemeal_owner IN SCHEMA app GRANT EXECUTE ON FUNCTIONS TO bluemeal_app;
            """;
        await cmd.ExecuteNonQueryAsync();

        // VDD portal mock (tools/mock-vdd-api) on an in-memory TestServer
        var mb = WebApplication.CreateBuilder();
        mb.WebHost.UseTestServer();
        MockVddApi.MockServer.Configure(mb.Services, mb.Configuration);
        MockVdd = mb.Build();
        MockVddApi.MockServer.Map(MockVdd);
        await MockVdd.StartAsync();

        _ = Services; // build host → migrations + seed
    }

    /// <summary>In-process mock of the VDD portal; use <see cref="MockVddClient"/> for its admin endpoints.</summary>
    public WebApplication? MockVdd { get; private set; }
    public HttpClient MockVddClient() => MockVdd!.GetTestClient();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        if (MockVdd is not null) await MockVdd.DisposeAsync();
        await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migration", OwnerConnectionString);
        builder.UseSetting("ConnectionStrings:Redis", "");
        builder.UseSetting("Auth:JwtKey", "integration-test-jwt-key-0123456789abcdef0123");
        builder.UseSetting("Auth:CookieSecure", "false");
        builder.UseSetting("Security:EncryptionKeys", "t1:" + Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()));
        builder.UseSetting("Security:HmacKey", Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray()));
        builder.UseSetting("Jobs:Mode", "Inline");
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:SampleData", "true");
        builder.UseSetting("RateLimit:AuthPerMinute", "100000");
        builder.UseSetting("RateLimit:PerMinute", "100000");
        builder.UseSetting("Storage:BackupPath", BackupPath);
        builder.UseSetting("Backup:TimeoutMinutes", "1");
        builder.UseSetting("Storage:FilesPath", Path.Combine(Path.GetTempPath(), "bluemeal-it-files-" + Guid.NewGuid().ToString("N")));
        builder.UseSetting("Logging:FilePath", Path.Combine(Path.GetTempPath(), "bluemeal-it-logs", "log-.json"));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<BlueMeal.Application.Abstractions.IEmailSender>();
            s.AddSingleton<BlueMeal.Application.Abstractions.IEmailSender>(Emails);
            s.AddHttpClient(BlueMeal.Infrastructure.Integration.VddApiNutritionDataProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => MockVdd!.GetTestServer().CreateHandler());
        });
    }

    /// <summary>Shared backup volume (the test plays the backup sidecar).</summary>
    public string BackupPath { get; } = Path.Combine(Path.GetTempPath(), "bluemeal-it-backups-" + Guid.NewGuid().ToString("N"));

    /// <summary>Captures outgoing emails (stands in for Mailpit in tests).</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>New client with its own cookie jar (refresh cookie), Vietnamese by default.</summary>
    public HttpClient NewClient(string lang = "vi")
    {
        var jar = new CookieContainer();
        var c = CreateDefaultClient(new Uri("http://localhost"), new CookieJarHandler(jar));
        c.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(lang));
        Jars.AddOrUpdate(c, jar);
        return c;
    }

    /// <summary>Creates a user with the given role codes directly (no MFA requirement unless the role mandates it).</summary>
    public async Task<(string UserName, string Password)> CreateUserAsync(string userName, params string[] roles)
    {
        using var scope = Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<BlueMeal.Infrastructure.Persistence.AppDbContext>();
        const string pwd = "User@Test2026!";
        var u = new AppUser { UserName = userName, FullName = "Test " + userName, Email = userName + "@test.local", EmailConfirmed = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, PasswordChangedAt = DateTimeOffset.UtcNow, LockoutEnabled = true };
        var r = await um.CreateAsync(u, pwd);
        if (!r.Succeeded) throw new InvalidOperationException(string.Join(",", r.Errors.Select(e => e.Description)));
        foreach (var code in roles)
        {
            var role = db.Roles.First(x => x.Name == code);
            db.Set<UserRole>().Add(new UserRole { UserId = u.Id, RoleId = role.Id });
        }
        await db.SaveChangesAsync();
        return (userName, pwd);
    }

    /// <summary>Logs in a user that has no restriction (no MFA, no forced password change) and returns an authorized client.</summary>
    public async Task<HttpClient> LoginAsync(string userName, string password, string lang = "vi")
    {
        var c = NewClient(lang);
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { userName, password });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        if (body.GetProperty("restricted").GetBoolean()) throw new InvalidOperationException("restricted session");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return c;
    }

    private HttpClient? _admin;
    private string? _adminTotpSecret;
    public string AdminCurrentPassword { get; private set; } = AdminPassword;
    public string? AdminTotpSecret => _adminTotpSecret;

    /// <summary>Completes the mandatory first-login flow for "admin" (change password + MFA enrolment) once, returns a full-scope client.</summary>
    public async Task<HttpClient> AdminAsync()
    {
        if (_admin is not null) return _admin;
        var c = NewClient();
        var login = await (await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = "admin", password = AdminPassword })).Content.ReadFromJsonAsync<JsonElement>(Json);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        const string newPwd = "Admin@Changed2026!";
        (await c.PostAsJsonAsync("/api/v1/profile/change-password", new { currentPassword = AdminPassword, newPassword = newPwd })).EnsureSuccessStatusCode();
        AdminCurrentPassword = newPwd;
        var setup = await (await c.PostAsync("/api/v1/profile/mfa/setup", null)).Content.ReadFromJsonAsync<JsonElement>(Json);
        _adminTotpSecret = setup.GetProperty("secret").GetString()!;
        (await c.PostAsJsonAsync("/api/v1/profile/mfa/enable", new { code = Totp(_adminTotpSecret) })).EnsureSuccessStatusCode();
        await Refresh(c);
        _admin = c;
        return c;
    }

    public static string Totp(string secret) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

    /// <summary>Calls /auth/refresh with the CSRF header taken from the cookie jar; sets the new bearer token.</summary>
    public async Task<JsonElement> Refresh(HttpClient c)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        req.Headers.Add("X-BlueMeal-CSRF", CsrfFrom(c));
        var res = await c.SendAsync(req);
        if (res.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("refresh failed: " + res.StatusCode + " " + await res.Content.ReadAsStringAsync());
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return body;
    }

    public static string CsrfFrom(HttpClient c)
        => Jars.TryGetValue(c, out var jar) ? jar.GetAllCookies().FirstOrDefault(k => k.Name == "bm_csrf")?.Value ?? "" : "";

    public static CookieContainer JarOf(HttpClient c) => Jars.TryGetValue(c, out var j) ? j : throw new InvalidOperationException();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, CookieContainer> Jars = new();
}

/// <summary>Minimal browser-like cookie handling (respects Path) for the in-memory test server.</summary>
public sealed class CookieJarHandler(CookieContainer jar) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var header = jar.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(header)) request.Headers.Add("Cookie", header);
        var res = await base.SendAsync(request, ct);
        if (res.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var v in values) jar.SetCookies(request.RequestUri!, v);
        return res;
    }
}

public sealed class CapturingEmailSender : BlueMeal.Application.Abstractions.IEmailSender
{
    public System.Collections.Concurrent.ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();
    public bool IsConfigured => true;
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) { Sent.Enqueue((to, subject, htmlBody)); return Task.CompletedTask; }
}
