using System.Security.Cryptography;
using System.Text.Json;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Persistence;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace BlueMeal.Infrastructure.Services;

/// <summary>Stores uploads on the files volume using random names; content served only through authorized API.</summary>
public sealed class LocalFileStorage(string root) : IFileStorage
{
    public string RootPath { get; } = Directory.CreateDirectory(root).FullName;

    public async Task<(string StoredName, long Size, string Sha256)> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var name = $"{DateTime.UtcNow:yyyyMM}/{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{extension.ToLowerInvariant()}";
        var full = Path.Combine(RootPath, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        using var sha = SHA256.Create();
        await using (var cs = new CryptoStream(fs, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(cs, ct);
        }
        return (name, fs.Length, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    public Stream OpenRead(string storedName)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, storedName));
        if (!full.StartsWith(RootPath, StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return File.OpenRead(full);
    }

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, storedName));
        if (full.StartsWith(RootPath, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }
}

public sealed class SettingsService(AppDbContext db, ICacheService cache, IEncryptionService enc, ICurrentUser user, TimeProvider clock) : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new()
    {
        var raw = await cache.GetOrCreateAsync("setting:" + key, async () =>
            (await db.Set<AppSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct))?.Value ?? "", TimeSpan.FromMinutes(10), ct);
        var value = string.IsNullOrEmpty(raw) ? new T() : JsonSerializer.Deserialize<T>(raw, Json) ?? new T();
        if (value is SmtpSettings smtp && smtp.Password is not null) smtp.Password = enc.Decrypt(smtp.Password);
        return value;
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        // SMTP password is encrypted at rest; the caller's object is not mutated.
        var json = JsonSerializer.Serialize(value, Json);
        if (value is SmtpSettings s2 && s2.Password is not null)
        {
            var clone = JsonSerializer.Deserialize<SmtpSettings>(json, Json)!;
            clone.Password = s2.Password.StartsWith("v1:", StringComparison.Ordinal) ? s2.Password : enc.Encrypt(s2.Password);
            json = JsonSerializer.Serialize(clone, Json);
        }
        var row = await db.Set<AppSetting>().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.Set<AppSetting>().Add(row = new AppSetting { Key = key });
        row.Value = json;
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedBy = user.UserId;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("setting:" + key, ct);
    }
}

/// <summary>Document numbers PREFIX-YYYYMMDD-#### using a PostgreSQL upsert counter (safe across replicas).</summary>
public sealed class DocNumberGenerator(AppDbContext db) : IDocNumberGenerator
{
    public async Task<string> NextAsync(string prefix, DateOnly date, CancellationToken ct = default)
    {
        var key = $"{prefix}-{date:yyyyMMdd}";
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = "INSERT INTO app.doc_counter(key, value) VALUES (@k, 1) ON CONFLICT (key) DO UPDATE SET value = app.doc_counter.value + 1 RETURNING value";
            var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = key; cmd.Parameters.Add(p);
            var v = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            return $"{key}-{v:0000}";
        }
        finally { if (shouldClose) await conn.CloseAsync(); }
    }
}

public sealed class AuditWriter(AppDbContext db, ICurrentUser user, TimeProvider clock) : IAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task WriteAsync(AuditAction action, string? entityType = null, string? entityId = null, object? oldValues = null,
        object? newValues = null, bool success = true, string? message = null, CancellationToken ct = default)
    {
        db.Set<AuditLog>().Add(new AuditLog
        {
            At = clock.GetUtcNow(), UserId = user.UserId, UserName = user.UserName, Ip = user.Ip,
            UserAgent = user.UserAgent is { Length: > 500 } ua ? ua[..500] : user.UserAgent, TraceId = user.TraceId,
            Action = action, EntityType = entityType, EntityId = entityId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
            Success = success, Message = message is { Length: > 2000 } m ? m[..2000] : message,
        });
        await db.SaveChangesAsync(ct);
    }
}

public sealed class NotificationService(AppDbContext db, IEmailSender email, TimeProvider clock, ILogger<NotificationService> log) : INotificationService
{
    public async Task NotifyUsersAsync(IEnumerable<Guid> userIds, NotificationType type, string titleKey, object[] args, string? link, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var (vi, en) = L.Messages.TryGetValue(titleKey, out var m) ? m : (titleKey, titleKey);
        var titleVi = string.Format(vi, args);
        var titleEn = string.Format(en, args);
        foreach (var id in ids)
            db.Set<Notification>().Add(new Notification { UserId = id, Type = type, TitleVi = titleVi, TitleEn = titleEn, Link = link, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);

        if (!email.IsConfigured) return;
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Email != null && u.EmailConfirmed).Select(u => new { u.Email, u.Language }).ToListAsync(ct);
        foreach (var u in users)
        {
            try { await email.SendAsync(u.Email!, u.Language == "en" ? titleEn : titleVi, $"<p>{System.Net.WebUtility.HtmlEncode(u.Language == "en" ? titleEn : titleVi)}</p>{(link is null ? "" : $"<p>{System.Net.WebUtility.HtmlEncode(link)}</p>")}", ct); }
            catch (Exception ex) { log.LogWarning(ex, "Failed to send notification email"); }
        }
    }

    public async Task NotifyPermissionAsync(string permission, NotificationType type, string titleKey, object[] args, string? link, CancellationToken ct = default)
    {
        var userIds = await (from ur in db.Set<UserRole>()
                             join rp in db.Set<RolePermission>() on ur.RoleId equals rp.RoleId
                             join p in db.Set<Permission>() on rp.PermissionId equals p.Id
                             join u in db.Users on ur.UserId equals u.Id
                             where p.Code == permission && u.IsActive
                             select ur.UserId).Distinct().ToListAsync(ct);
        await NotifyUsersAsync(userIds, type, titleKey, args, link, ct);
    }
}

public sealed class SmtpEmailSender(ISettingsService settings, ILogger<SmtpEmailSender> log) : IEmailSender
{
    private SmtpSettings? _cached;
    private SmtpSettings Settings => _cached ??= settings.GetAsync<SmtpSettings>(SettingKeys.Smtp).GetAwaiter().GetResult();

    public bool IsConfigured => Settings is { Enabled: true, Host.Length: > 0 };

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var s = Settings;
        if (!IsConfigured) throw new AppException("auth.smtpNotConfigured");
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(s.FromName, s.FromAddress));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();
        using var client = new SmtpClient();
        var secure = s.UseSsl ? (s.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None;
        await client.ConnectAsync(s.Host!, s.Port, secure, ct); // IsConfigured guarantees a host
        if (!string.IsNullOrEmpty(s.UserName)) await client.AuthenticateAsync(s.UserName, s.Password ?? "", ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
        log.LogInformation("Email sent to {To}: {Subject}", MaskEmail(to), subject);
    }

    private static string MaskEmail(string e) { var i = e.IndexOf('@'); return i <= 1 ? "***" : e[0] + "***" + e[i..]; }
}
