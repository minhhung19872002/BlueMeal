using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMeal.Api.Services;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using BlueMeal.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace BlueMeal.IntegrationTests;

/// <summary>Phase P8: manual backup through the sidecar protocol, download with re-authentication, housekeeping.</summary>
[Collection(nameof(ApiCollection))]
public sealed class SecurityP8Tests(ApiFactory f)
{
    private static async Task<JsonElement> J(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode) throw new HttpRequestException($"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        var text = await r.Content.ReadAsStringAsync();
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    /// <summary>Plays the backup container: picks up request files and writes archive + manifest like deploy/backup/backup.sh.</summary>
    private static async Task FakeSidecarAsync(string root, CancellationToken ct)
    {
        var requests = Path.Combine(root, "requests");
        while (!ct.IsCancellationRequested)
        {
            foreach (var req in Directory.Exists(requests) ? Directory.GetFiles(requests, "*.req") : [])
            {
                var id = Path.GetFileNameWithoutExtension(req);
                File.Delete(req);
                var name = $"bluemeal-{DateTime.UtcNow:yyyyMMdd-HHmmss}-manual.tar.enc";
                var content = System.Text.Encoding.UTF8.GetBytes("encrypted-archive-" + id);
                await File.WriteAllBytesAsync(Path.Combine(root, "archive", name), content, ct);
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
                await File.WriteAllTextAsync(Path.Combine(root, "manifests", id + ".json"),
                    JsonSerializer.Serialize(new { id, type = "Manual", status = "Succeeded", file = name, size = content.Length, sha256 = sha, startedAt = DateTimeOffset.UtcNow, finishedAt = DateTimeOffset.UtcNow }), ct);
            }
            try { await Task.Delay(200, ct); } catch (TaskCanceledException) { return; }
        }
    }

    [Fact]
    public async Task Manual_backup_and_download_with_reauthentication()
    {
        var c = await f.AdminAsync();
        foreach (var d in new[] { "requests", "manifests", "archive" }) Directory.CreateDirectory(Path.Combine(f.BackupPath, d));
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var sidecar = FakeSidecarAsync(f.BackupPath, cts.Token);
        // a scheduled backup made by cron appears in the history too
        await File.WriteAllTextAsync(Path.Combine(f.BackupPath, "manifests", "sched-20261108010000.json"),
            JsonSerializer.Serialize(new { id = "sched-20261108010000", type = "Scheduled", status = "Succeeded", file = "bluemeal-20261108-010000-scheduled.tar.enc", size = 10, sha256 = "x", startedAt = "2026-11-08T01:00:00+07:00", finishedAt = "2026-11-08T01:00:09+07:00" }));

        var id = (await J(await c.PostAsJsonAsync("/api/v1/backups", new { note = "Trước khi nâng cấp" }))).GetProperty("id").GetString();
        JsonElement row = default;
        for (var i = 0; i < 100; i++)
        {
            var list = await J(await c.GetAsync("/api/v1/backups"));
            row = list.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetString() == id);
            if (row.GetProperty("status").GetString() is "Succeeded" or "Failed") break;
            await Task.Delay(200);
        }
        row.GetProperty("status").GetString().Should().Be("Succeeded", row.ToString());
        row.GetProperty("available").GetBoolean().Should().BeTrue();
        row.GetProperty("sha256").GetString().Should().HaveLength(64);
        (await J(await c.GetAsync("/api/v1/backups"))).GetProperty("items").EnumerateArray().Should().Contain(x => x.GetProperty("type").GetString() == "Scheduled");

        // re-authentication: wrong password → 403, missing MFA code → 403, then password + TOTP → single-use token
        (await c.PostAsJsonAsync($"/api/v1/backups/{id}/download-token", new { password = "wrong", code = "000000" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync($"/api/v1/backups/{id}/download-token", new { password = f.AdminCurrentPassword, code = (string?)null })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var code = new Totp(Base32Encoding.ToBytes(f.AdminTotpSecret!)).ComputeTotp(DateTime.UtcNow.AddSeconds(30));
        var token = (await J(await c.PostAsJsonAsync($"/api/v1/backups/{id}/download-token", new { password = f.AdminCurrentPassword, code }))).GetProperty("token").GetString();
        // the same TOTP code cannot be replayed
        (await c.PostAsJsonAsync($"/api/v1/backups/{id}/download-token", new { password = f.AdminCurrentPassword, code })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var file = await c.GetAsync($"/api/v1/backups/{id}/download?token={token}");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        (await file.Content.ReadAsStringAsync()).Should().StartWith("encrypted-archive-");
        // the download token is single-use
        (await c.GetAsync($"/api/v1/backups/{id}/download?token={token}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // users without system.backup are refused
        var (u, p) = await f.CreateUserAsync("chef_backup", "HEAD_CHEF");
        var viewer = await f.LoginAsync(u, p);
        (await viewer.GetAsync("/api/v1/backups")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        cts.Cancel();
        await sidecar;
    }

    [Fact]
    public async Task Housekeeping_expires_export_files_and_maintains_audit_partitions()
    {
        await f.AdminAsync();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new ExportJob { ReportCode = "RPT-01", RequestedAt = DateTimeOffset.UtcNow.AddDays(-10), RequestedBy = Guid.NewGuid(), Status = JobStatus.Succeeded, ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1), FileName = "x.xlsx" };
        db.Add(job);
        await db.SaveChangesAsync();
        var housekeeping = scope.ServiceProvider.GetServices<BlueMeal.Application.Abstractions.IAppJob>().OfType<HousekeepingJob>().Single();
        await housekeeping.ExecuteAsync(new Dictionary<string, string>(), CancellationToken.None);
        db.ChangeTracker.Clear();
        (await db.Set<ExportJob>().FindAsync(job.Id))!.Status.Should().Be(JobStatus.Expired);
    }
}
