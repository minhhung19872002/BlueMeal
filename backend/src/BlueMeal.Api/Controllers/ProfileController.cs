using BlueMeal.Api.Auth;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueMeal.Api.Controllers;

/// <summary>Personal profile (SPEC §7.1.12): password, MFA, language/theme, own login history, own sessions, UI preferences.</summary>
[Route("api/v1/profile"), HasPermission(Permissions.Self)]
public sealed class ProfileController(AuthService auth, AppDbContext db, UserManager<AppUser> users, TimeProvider clock) : ApiControllerBase
{
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record CodeRequest(string Code);
    public sealed record PreferencesRequest(string? Language, string? Theme, string? FullName, string? Email);

    [HttpPost("change-password"), AllowRestricted]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(UserId, req.CurrentPassword, req.NewPassword, SessionId, ct);
        return NoContent();
    }

    [HttpPost("mfa/setup"), AllowRestricted]
    public async Task<IActionResult> MfaSetup(CancellationToken ct)
    {
        var (secret, uri, png) = await auth.BeginMfaSetupAsync(UserId, ct);
        return Ok(new { secret, otpAuthUri = uri, qrPng = "data:image/png;base64," + png });
    }

    [HttpPost("mfa/enable"), AllowRestricted]
    public async Task<IActionResult> MfaEnable([FromBody] CodeRequest req, CancellationToken ct)
        => Ok(new { recoveryCodes = await auth.EnableMfaAsync(UserId, req.Code, ct) });

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> MfaDisable([FromBody] CodeRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString()) ?? throw new NotFoundException("user");
        if (!AuthService.VerifyTotp(user.MfaSecret, req.Code)) throw new AppException("auth.mfaInvalid", 400);
        await auth.DisableMfaAsync(UserId, byAdmin: false, ct);
        return NoContent();
    }

    [HttpPost("mfa/recovery-codes")]
    public async Task<IActionResult> RegenerateRecoveryCodes([FromBody] CodeRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString()) ?? throw new NotFoundException("user");
        if (!AuthService.VerifyTotp(user.MfaSecret, req.Code)) throw new AppException("auth.mfaInvalid", 400);
        return Ok(new { recoveryCodes = await auth.RegenerateRecoveryCodesAsync(UserId, ct) });
    }

    [HttpPost("email-otp"), AllowRestricted]
    public async Task<IActionResult> ToggleEmailOtp([FromQuery] bool enabled, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString()) ?? throw new NotFoundException("user");
        user.EmailOtpEnabled = enabled;
        await users.UpdateAsync(user);
        return NoContent();
    }

    [HttpPut("preferences"), AllowRestricted]
    public async Task<IActionResult> Preferences([FromBody] PreferencesRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString()) ?? throw new NotFoundException("user");
        if (req.Language is "vi" or "en") user.Language = req.Language;
        if (req.Theme is "light" or "dark") user.Theme = req.Theme;
        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName.Trim();
        if (req.Email is not null) { user.Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim(); user.NormalizedEmail = user.Email?.ToUpperInvariant(); }
        user.UpdatedAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return NoContent();
    }

    [HttpGet("login-history")]
    public async Task<IActionResult> LoginHistory([FromQuery(Name = "")] PageQuery q, CancellationToken ct)
    {
        var query = db.Set<LoginHistory>().AsNoTracking().Where(h => h.UserId == UserId).OrderByDescending(h => h.At);
        var total = await query.LongCountAsync(ct);
        var items = await query.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(h => new { h.Id, h.At, result = h.Result.ToString(), h.Ip, h.UserAgent, device = AuthService.DescribeDevice(h.UserAgent), h.Reason }).ToListAsync(ct);
        return Ok(new PagedResult<object>(items, q.Page, q.PageSize, total));
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> MySessions(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var sid = SessionId;
        var items = await db.Set<UserSession>().AsNoTracking().Where(s => s.UserId == UserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new { s.Id, s.Ip, s.Device, s.UserAgent, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, current = s.Id == sid }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeMySession(Guid id, CancellationToken ct)
    {
        var s = await db.Set<UserSession>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct) ?? throw new NotFoundException("session");
        await auth.RevokeSessionAsync(s.Id, "user_signout", ct);
        return NoContent();
    }

    /// <summary>Per-user UI preference (e.g. table column layout, favourite report filters) — SPEC §9, §7.9.</summary>
    [HttpGet("ui/{key}")]
    public async Task<IActionResult> GetUiPref(string key, CancellationToken ct)
    {
        var p = await db.Set<UserPreference>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == UserId && x.Key == key, ct);
        return Content(p?.Value ?? "null", "application/json");
    }

    [HttpPut("ui/{key}")]
    public async Task<IActionResult> SetUiPref(string key, [FromBody] System.Text.Json.JsonElement value, CancellationToken ct)
    {
        if (key.Length > 100) throw new AppException("error.validation");
        var raw = value.GetRawText();
        if (raw.Length > 50_000) throw new AppException("error.validation");
        var p = await db.Set<UserPreference>().FirstOrDefaultAsync(x => x.UserId == UserId && x.Key == key, ct);
        if (p is null) db.Add(p = new UserPreference { UserId = UserId, Key = key });
        p.Value = raw;
        p.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("ui")]
    public async Task<IActionResult> ListUiPrefs([FromQuery] string prefix, CancellationToken ct)
    {
        var items = await db.Set<UserPreference>().AsNoTracking().Where(x => x.UserId == UserId && x.Key.StartsWith(prefix ?? ""))
            .Select(x => new { x.Key, x.Value, x.UpdatedAt }).ToListAsync(ct);
        return Ok(items);
    }

    [HttpDelete("ui/{key}")]
    public async Task<IActionResult> DeleteUiPref(string key, CancellationToken ct)
    {
        await db.Set<UserPreference>().Where(x => x.UserId == UserId && x.Key == key).ExecuteDeleteAsync(ct);
        return NoContent();
    }
}
