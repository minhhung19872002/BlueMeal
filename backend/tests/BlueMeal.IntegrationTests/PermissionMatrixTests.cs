using System.Net;
using System.Net.Http.Json;
using BlueMeal.Application.Authorization;
using BlueMeal.IntegrationTests.Infrastructure;
using Npgsql;

namespace BlueMeal.IntegrationTests;

/// <summary>SPEC §10.2: automated permission-matrix test — every seeded role × a representative set of endpoints.</summary>
[Collection(nameof(ApiCollection))]
public sealed class PermissionMatrixTests(ApiFactory f)
{
    /// <summary>(HTTP method, url, required permission)</summary>
    public static readonly (string Method, string Url, string Permission)[] Endpoints =
    [
        ("GET", "/api/v1/users", Permissions.User.View),
        ("GET", "/api/v1/roles", Permissions.Role.View),
        ("GET", "/api/v1/sessions", Permissions.Session.View),
        ("GET", "/api/v1/permissions", Permissions.Permission.View),
        ("GET", "/api/v1/settings", Permissions.SystemInfo.Setting),
        ("GET", "/api/v1/audit-logs", Permissions.Audit.View),
        ("GET", "/api/v1/organization", Permissions.Organization.View),
        ("GET", "/api/v1/departments", Permissions.Department.View),
        ("GET", "/api/v1/employees", Permissions.Employee.View),
        ("GET", "/api/v1/provinces", Permissions.CommonCatalog.View),
        ("GET", "/api/v1/units", Permissions.CommonCatalog.View),
        ("GET", "/api/v1/system/version", Permissions.SystemInfo.Version),
    ];

    public static IEnumerable<object[]> Roles() => DefaultRoles.All.Select(r => new object[] { r.Code });

    [Theory]
    [MemberData(nameof(Roles))]
    public async Task Role_can_only_reach_endpoints_it_is_granted(string roleCode)
    {
        var role = DefaultRoles.All.Single(r => r.Code == roleCode);
        var (u, pwd) = await f.CreateUserAsync("matrix_" + roleCode.ToLowerInvariant(), roleCode);
        if (DefaultRoles.MfaMandatory.Contains(roleCode)) await DisableMfaRequirementForTest(u);
        var c = await f.LoginAsync(u, pwd);
        foreach (var (method, url, perm) in Endpoints)
        {
            var res = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));
            var expected = role.Grants(perm) ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
            res.StatusCode.Should().Be(expected, $"{roleCode} {method} {url} requires {perm}");
        }
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var c = f.NewClient();
        foreach (var (method, url, _) in Endpoints)
            (await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
    }

    [Fact]
    public async Task Write_endpoints_are_denied_without_permission()
    {
        var (u, pwd) = await f.CreateUserAsync("matrix_writer", DefaultRoles.Storekeeper);
        var c = await f.LoginAsync(u, pwd);
        (await c.PostAsJsonAsync("/api/v1/departments", new { code = "X", nameVi = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync("/api/v1/roles", new { code = "HACK", nameVi = "Hack" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PutAsJsonAsync("/api/v1/settings/security.password", new { minLength = 8 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>MFA-mandatory roles would get a restricted session; for matrix testing we flag the role off for this one user via its role table copy.</summary>
    private async Task DisableMfaRequirementForTest(string userName)
    {
        // Create a test-only clone role without MFA and move the user to it, keeping identical permissions.
        await using var conn = new NpgsqlConnection(f.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            WITH u AS (SELECT id FROM app.app_user WHERE user_name = @u),
                 r AS (SELECT ur.role_id FROM app.user_role ur JOIN u ON ur.user_id = u.id LIMIT 1),
                 nr AS (INSERT INTO app.app_role (id, name, normalized_name, name_vi, is_system, requires_mfa, is_active, concurrency_stamp)
                        SELECT gen_random_uuid(), 'T_' || @u, upper('T_' || @u), 'test', false, false, true, 'x' RETURNING id),
                 cp AS (INSERT INTO app.role_permission (role_id, permission_id) SELECT nr.id, rp.permission_id FROM app.role_permission rp, nr, r WHERE rp.role_id = r.role_id)
            UPDATE app.user_role SET role_id = (SELECT id FROM nr) WHERE user_id = (SELECT id FROM u);
            """;
        cmd.Parameters.AddWithValue("u", userName);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>SPEC §10.3: the application DB user cannot modify or delete audit log rows (append-only).</summary>
[Collection(nameof(ApiCollection))]
public sealed class AuditLogTests(ApiFactory f)
{
    [Fact]
    public async Task Audit_log_is_append_only_for_application_role()
    {
        await f.AdminAsync(); // ensures audit rows exist
        await using var conn = new NpgsqlConnection(f.AppConnectionString);
        await conn.OpenAsync();
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM app.audit_log", conn))
            ((long)(await count.ExecuteScalarAsync())!).Should().BeGreaterThan(0);
        var update = async () => { await using var c = new NpgsqlCommand("UPDATE app.audit_log SET message = 'tamper'", conn); await c.ExecuteNonQueryAsync(); };
        await update.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        var delete = async () => { await using var c = new NpgsqlCommand("DELETE FROM app.audit_log", conn); await c.ExecuteNonQueryAsync(); };
        await delete.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
        var truncate = async () => { await using var c = new NpgsqlCommand("TRUNCATE app.audit_log", conn); await c.ExecuteNonQueryAsync(); };
        await truncate.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task Changes_are_audited_with_json_diff_and_masked_sensitive_fields()
    {
        var admin = await f.AdminAsync();
        var create = await admin.PostAsJsonAsync("/api/v1/employees", new { code = "NV-AUD-1", fullName = "Nguyễn Văn Kiểm", phone = "0912345678", email = "kiem@test.vn", gender = "Male", isActive = true });
        create.EnsureSuccessStatusCode();
        var emp = await create.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ApiFactory.Json);
        var id = emp.GetProperty("id").GetString();
        var hist = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/v1/audit-logs?entityType=Employee&entityId={id}&action=Create", ApiFactory.Json);
        var item = hist.GetProperty("items")[0];
        item.GetProperty("action").GetString().Should().Be("Create");
        var nv = item.GetProperty("newValues").GetString()!;
        var doc = System.Text.Json.JsonDocument.Parse(nv).RootElement;
        doc.GetProperty("code").GetString().Should().Be("NV-AUD-1");
        doc.GetProperty("phone").GetString().Should().Be("***");
        doc.GetProperty("email").GetString().Should().Be("***");
        nv.Should().NotContain("0912345678");
    }
}
