using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.IntegrationTests.Infrastructure;
using ClosedXML.Excel;
using Npgsql;

namespace BlueMeal.IntegrationTests;

/// <summary>Generic catalog behaviour (SPEC §7 common features) exercised through departments/units/employees.</summary>
[Collection(nameof(ApiCollection))]
public sealed class CatalogTests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r) { r.EnsureSuccessStatusCode(); return await r.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json); }

    [Fact]
    public async Task Crud_search_without_accents_soft_delete_and_history()
    {
        var c = await f.AdminAsync();
        var created = await J(await c.PostAsJsonAsync("/api/v1/departments", new { code = "BEP-TT", nameVi = "Bếp trung tâm Đông Anh", nameEn = "Central kitchen", isActive = true, sortOrder = 1 }));
        var id = created.GetProperty("id").GetString();

        var search = await J(await c.GetAsync("/api/v1/departments?q=bep trung tam dong anh"));
        search.GetProperty("totalItems").GetInt64().Should().Be(1);

        // duplicate code → 409
        (await c.PostAsJsonAsync("/api/v1/departments", new { code = "BEP-TT", nameVi = "Trùng" })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // optimistic concurrency with xmin version
        var version = created.GetProperty("version").GetUInt32();
        (await c.PutAsJsonAsync($"/api/v1/departments/{id}", new { code = "BEP-TT", nameVi = "Bếp TT (sửa 1)", isActive = true, version })).EnsureSuccessStatusCode();
        var stale = await c.PutAsJsonAsync($"/api/v1/departments/{id}", new { code = "BEP-TT", nameVi = "Bếp TT (sửa cũ)", isActive = true, version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await c.PostAsync($"/api/v1/departments/{id}/lock", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await J(await c.GetAsync($"/api/v1/departments/{id}"))).GetProperty("isActive").GetBoolean().Should().BeFalse();

        var history = await J(await c.GetAsync($"/api/v1/departments/{id}/history"));
        history.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        (await c.DeleteAsync($"/api/v1/departments/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/v1/departments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // soft delete keeps the row
        await using var conn = new NpgsqlConnection(f.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT is_deleted FROM app.department WHERE id = '{id}'", conn);
        ((bool)(await cmd.ExecuteScalarAsync())!).Should().BeTrue();
    }

    [Fact]
    public async Task Unit_conversion_keeps_microgram_precision()
    {
        var c = await f.AdminAsync();
        var units = (await J(await c.GetAsync("/api/v1/units?pageSize=200"))).GetProperty("items").EnumerateArray()
            .ToDictionary(u => u.GetProperty("code").GetString()!, u => u.GetProperty("id").GetString());
        var r = await J(await c.GetAsync($"/api/v1/units/convert?value=2500&from={units["UG"]}&to={units["MG"]}"));
        r.GetProperty("result").GetDecimal().Should().Be(2.5m);
    }

    [Fact]
    public async Task Paging_is_server_side_and_capped_at_200()
    {
        var c = await f.AdminAsync();
        var page = await J(await c.GetAsync("/api/v1/provinces?page=1&pageSize=10&sort=-code"));
        page.GetProperty("items").GetArrayLength().Should().Be(10);
        page.GetProperty("totalItems").GetInt64().Should().Be(34);
        page.GetProperty("statusCounts").GetProperty("active").GetInt64().Should().Be(34);
        page.GetProperty("statusCounts").GetProperty("inactive").GetInt64().Should().Be(0);
        // counts ignore the state filter but keep the others (search)
        var filtered = await J(await c.GetAsync("/api/v1/provinces?q=ha noi&isActive=false"));
        filtered.GetProperty("totalItems").GetInt64().Should().Be(0);
        filtered.GetProperty("statusCounts").GetProperty("active").GetInt64().Should().Be(1);
        page.GetProperty("items")[0].GetProperty("code").GetString().Should().Be("96");
        // official commune-level list seeded from SeedData/dvhc-wards.csv
        (await J(await c.GetAsync("/api/v1/wards?pageSize=1"))).GetProperty("totalItems").GetInt64().Should().BeGreaterThanOrEqualTo(3321);
        var conDao = (await J(await c.GetAsync("/api/v1/wards?q=dac khu con dao"))).GetProperty("items")[0];
        conDao.GetProperty("code").GetString().Should().Be("26732");
        conDao.GetProperty("nameEn").GetString().Should().Be("Con Dao Special Zone");
        var big = await J(await c.GetAsync("/api/v1/provinces?pageSize=100000"));
        big.GetProperty("pageSize").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task Excel_template_import_preview_commit_and_export_round_trip()
    {
        var c = await f.AdminAsync();
        var vn = (await J(await c.GetAsync("/api/v1/provinces?q=ha noi"))).GetProperty("items")[0];
        var template = await c.GetAsync("/api/v1/wards/import/template");
        template.EnsureSuccessStatusCode();

        using var wb = new XLWorkbook(await template.Content.ReadAsStreamAsync());
        var ws = wb.Worksheet(1);
        // header: Mã | Tên (VI) | Tên (EN) | Mã tỉnh/thành | Loại | Đang sử dụng | Thứ tự
        string[][] rows =
        [
            ["P-BADINH", "Phường Ba Đình", "Ba Dinh Ward", "01", "Phường", "x", "1"],
            ["P-HOANKIEM", "Phường Hoàn Kiếm", "Hoan Kiem Ward", "01", "Phường", "x", "2"],
            ["", "Thiếu mã", "", "01", "Phường", "x", "3"],            // missing code
            ["X-BAD", "Sai tỉnh", "", "999", "Xã", "x", "4"],          // unknown province
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var col = 0; col < rows[r].Length; col++)
                ws.Cell(r + 2, col + 1).Value = rows[r][col];
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(ms.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "wards.xlsx");
        var preview = await J(await c.PostAsync("/api/v1/wards/import/preview", form));
        preview.GetProperty("totalRows").GetInt32().Should().Be(4);
        preview.GetProperty("validRows").GetInt32().Should().Be(2);
        preview.GetProperty("errorRows").GetInt32().Should().Be(2);
        preview.GetProperty("rows")[2].GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);

        var commit = await J(await c.PostAsJsonAsync("/api/v1/wards/import/commit", new { token = preview.GetProperty("token").GetString(), skipErrors = true }));
        commit.GetProperty("created").GetInt32().Should().Be(2);
        commit.GetProperty("skipped").GetInt32().Should().Be(2);

        var list = await J(await c.GetAsync($"/api/v1/wards?provinceId={vn.GetProperty("id").GetString()}&q=p-hoankiem"));
        list.GetProperty("totalItems").GetInt64().Should().Be(1, list.GetRawText());

        var export = await c.GetAsync("/api/v1/wards/export");
        export.EnsureSuccessStatusCode();
        using var exported = new XLWorkbook(await export.Content.ReadAsStreamAsync());
        exported.Worksheet(1).CellsUsed().Select(x => x.GetString()).Should().Contain("P-BADINH").And.Contain("01");
    }

    [Fact]
    public async Task Import_rejects_non_excel_content()
    {
        var c = await f.AdminAsync();
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("<script>alert(1)</script>"u8.ToArray()), "file", "evil.xlsx");
        (await c.PostAsync("/api/v1/wards/import/preview", form)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Employee_pii_is_encrypted_at_rest_masked_without_pii_view_and_searchable_by_hash()
    {
        var admin = await f.AdminAsync();
        var e = await J(await admin.PostAsJsonAsync("/api/v1/employees", new { code = "NV-PII-1", fullName = "Trần Thị Bảo Mật", phone = "0987654321", email = "baomat@test.vn", nationalId = "001234567890", gender = "Female", isActive = true }));
        e.GetProperty("phone").GetString().Should().Be("0987654321"); // admin has pii.view

        await using (var conn = new NpgsqlConnection(f.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT phone, email FROM app.employee WHERE code = 'NV-PII-1'", conn);
            await using var rd = await cmd.ExecuteReaderAsync();
            await rd.ReadAsync();
            rd.GetString(0).Should().StartWith("v1:").And.NotContain("0987654321");
            rd.GetString(1).Should().StartWith("v1:");
        }

        (await J(await admin.GetAsync("/api/v1/employees?phone=0987654321"))).GetProperty("totalItems").GetInt64().Should().Be(1);

        // A role with employee.view but without pii.view sees masked values
        var roleId = (await J(await admin.PostAsJsonAsync("/api/v1/roles", new { code = "HR_VIEWER", nameVi = "Xem nhân sự", requiresMfa = false, isActive = true }))).GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new { permissions = new[] { "employee.view" } })).EnsureSuccessStatusCode();
        var (u, pwd) = await f.CreateUserAsync("hrviewer", "HR_VIEWER");
        var viewer = await f.LoginAsync(u, pwd);
        var masked = (await J(await viewer.GetAsync("/api/v1/employees?q=bao mat"))).GetProperty("items")[0];
        masked.GetProperty("phone").GetString().Should().Be("*******321");
        masked.GetProperty("piiMasked").GetBoolean().Should().BeTrue();
        masked.GetProperty("email").GetString().Should().StartWith("b").And.Contain("***").And.EndWith("@test.vn");
    }

    [Fact]
    public async Task Users_can_be_created_locked_and_reset_by_admin()
    {
        var admin = await f.AdminAsync();
        var roles = await J(await admin.GetAsync("/api/v1/roles"));
        var chef = roles.EnumerateArray().First(r => r.GetProperty("code").GetString() == "HEAD_CHEF").GetProperty("id").GetString();
        var created = await J(await admin.PostAsJsonAsync("/api/v1/users", new { userName = "bep.truong", fullName = "Bếp Trưởng A", email = "bep@test.vn", roleIds = new[] { chef }, mfaRequired = false }));
        var temp = created.GetProperty("temporaryPassword").GetString()!;
        var uid = created.GetProperty("id").GetString();

        var c = f.NewClient();
        var login = await J(await c.PostAsJsonAsync("/api/v1/auth/login", new { userName = "bep.truong", password = temp }));
        login.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        login.GetProperty("restricted").GetBoolean().Should().BeTrue();

        (await admin.PostAsync($"/api/v1/users/{uid}/lock", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await f.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { userName = "bep.truong", password = temp })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.PostAsync($"/api/v1/users/{uid}/unlock", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reset = await J(await admin.PostAsync($"/api/v1/users/{uid}/reset-password", null));
        reset.GetProperty("temporaryPassword").GetString().Should().NotBe(temp);
    }

    [Fact]
    public async Task Last_system_admin_cannot_be_removed()
    {
        var admin = await f.AdminAsync();
        var me = await J(await admin.GetAsync("/api/v1/auth/me"));
        var id = me.GetProperty("id").GetString();
        var res = await admin.PostAsync($"/api/v1/users/{id}/lock", null);
        res.StatusCode.Should().Be((HttpStatusCode)422);
    }
}
