using System.Security.Cryptography;
using System.Text;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using BlueMeal.Application.Settings;
using BlueMeal.Domain.Entities;
using BlueMeal.Domain.Enums;
using BlueMeal.Infrastructure.Identity;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using QRCoder;

namespace BlueMeal.Api.Auth;

public sealed record LoginRequest(string UserName, string Password, string? CaptchaId, string? CaptchaAnswer);
public sealed record MfaVerifyRequest(string MfaToken, string Code, string Method = "totp");
public sealed record AuthUserDto(Guid Id, string UserName, string FullName, string? Email, string Language, string Theme, string[] Roles,
    string[] Permissions, bool MfaEnabled, bool MfaRequired, bool MustChangePassword, bool PasswordExpired, Guid[]? WarehouseScope, Guid[]? CustomerScope,
    bool PrivacyConsentRequired = false);
public sealed record LoginResponse(bool MfaRequired, string? MfaToken, string[]? MfaMethods, string? AccessToken, DateTimeOffset? ExpiresAt,
    bool Restricted, bool MustChangePassword, bool MfaSetupRequired, AuthUserDto? User, bool CaptchaRequired = false);

/// <summary>Issued session tokens: the refresh token goes to an HttpOnly cookie, never to JS.</summary>
public sealed record SessionTokens(LoginResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    TokenService tokens,
    ICacheService cache,
    ISettingsService settings,
    IAuditWriter audit,
    IEmailSender email,
    PermissionService permissionService,
    CaptchaService captcha,
    TimeProvider clock,
    PasswordHashGate hashGate,
    IConfiguration config,
    ILogger<AuthService> log)
{
    private const string MfaPrefix = "mfa:";
    private static readonly TimeSpan RotationGrace = TimeSpan.FromSeconds(30);
    private DateTimeOffset Now => clock.GetUtcNow();

    // ------------------------------------------------------------------ login

    public async Task<bool> CaptchaRequiredAsync(string userName, string? ip, CancellationToken ct)
    {
        var policy = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        var byUser = await cache.GetAsync<long?>(FailKey("u", userName), ct) ?? 0;
        var byIp = await cache.GetAsync<long?>(FailKey("ip", ip ?? ""), ct) ?? 0;
        // Per-account threshold; per-IP threshold is higher because many workers share one NAT address.
        return byUser >= policy.CaptchaAfterFailures || byIp >= policy.CaptchaAfterFailures * policy.CaptchaIpMultiplier;
    }

    public async Task<SessionTokens?> LoginAsync(LoginRequest req, string? ip, string? userAgent, CancellationToken ct)
    {
        var policy = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        var name = (req.UserName ?? "").Trim();

        if (await CaptchaRequiredAsync(name, ip, ct) && !await captcha.VerifyAsync(req.CaptchaId, req.CaptchaAnswer, ct))
            throw new AppException(string.IsNullOrEmpty(req.CaptchaId) ? "auth.captchaRequired" : "auth.captchaInvalid", 400) { Errors = new Dictionary<string, string[]> { ["captcha"] = ["required"] } };

        var user = await users.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == name.ToUpperInvariant(), ct);
        if (user is null)
        {
            // Constant-ish time: hash a dummy password to avoid user enumeration by timing.
            await hashGate.RunAsync(() => Task.FromResult(users.PasswordHasher.HashPassword(new AppUser { FullName = "x" }, req.Password ?? "")), ct);
            await RegisterFailure(null, name, ip, userAgent, LoginResult.Failed, "unknown_user", ct);
            throw new AppException("auth.invalidCredentials", 401);
        }
        if (!user.IsActive)
        {
            await RegisterFailure(user, name, ip, userAgent, LoginResult.Disabled, "disabled", ct);
            throw new AppException("auth.invalidCredentials", 401);
        }
        if (user.LockoutEnd is { } le && le > Now)
        {
            await RegisterFailure(user, name, ip, userAgent, LoginResult.Locked, "locked", ct);
            throw new AppException("auth.locked", 423, Math.Max(1, (int)Math.Ceiling((le - Now).TotalMinutes)));
        }
        if (!await hashGate.RunAsync(() => users.CheckPasswordAsync(user, req.Password ?? ""), ct))
        {
            var count = await cache.IncrementAsync(FailKey("acct", user.Id.ToString()), TimeSpan.FromMinutes(policy.LockoutMinutes), ct);
            if (count >= policy.MaxFailedAttempts)
            {
                user.LockoutEnd = Now.AddMinutes(policy.LockoutMinutes);
                await users.UpdateAsync(user);
                await cache.RemoveAsync(FailKey("acct", user.Id.ToString()), ct);
                await RegisterFailure(user, name, ip, userAgent, LoginResult.Locked, "lockout", ct);
                throw new AppException("auth.locked", 423, policy.LockoutMinutes);
            }
            await RegisterFailure(user, name, ip, userAgent, LoginResult.Failed, "bad_password", ct);
            throw new AppException("auth.invalidCredentials", 401);
        }

        await cache.RemoveAsync(FailKey("acct", user.Id.ToString()), ct);
        await cache.RemoveAsync(FailKey("u", name), ct);

        if (user.TwoFactorEnabled && !string.IsNullOrEmpty(user.MfaSecret))
        {
            var mfaToken = TokenService.NewOpaqueToken();
            await cache.SetAsync(MfaPrefix + mfaToken, user.Id.ToString(), TimeSpan.FromMinutes(5), ct);
            await RecordLogin(user.Id, name, LoginResult.MfaRequired, ip, userAgent, null, ct);
            var methods = new List<string> { "totp", "recovery" };
            if (user.EmailOtpEnabled && email.IsConfigured && !string.IsNullOrEmpty(user.Email)) methods.Add("email");
            return new SessionTokens(new LoginResponse(true, mfaToken, methods.ToArray(), null, null, false, false, false, null), "", Now);
        }

        return await IssueSessionAsync(user, ip, userAgent, ct);
    }

    public async Task<SessionTokens> VerifyMfaAsync(MfaVerifyRequest req, string? ip, string? userAgent, CancellationToken ct)
    {
        var idStr = await cache.GetAsync<string>(MfaPrefix + req.MfaToken, ct);
        if (idStr is null || !Guid.TryParse(idStr, out var userId)) throw new AppException("auth.mfaInvalid", 401);
        var attempts = await cache.IncrementAsync(MfaPrefix + "att:" + req.MfaToken, TimeSpan.FromMinutes(5), ct);
        if (attempts > 5) { await cache.RemoveAsync(MfaPrefix + req.MfaToken, ct); throw new AppException("auth.mfaInvalid", 401); }

        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new AppException("auth.mfaInvalid", 401);
        var ok = req.Method switch
        {
            "recovery" => await UseRecoveryCodeAsync(user.Id, req.Code, ct),
            "email" => await VerifyOneTimeAsync(user.Id, "EmailOtp", req.Code, ct),
            _ => VerifyTotp(user.MfaSecret, req.Code) && await ConsumeTotpAsync(user.Id, req.Code, ct),
        };
        if (!ok)
        {
            await RecordLogin(user.Id, user.UserName!, LoginResult.MfaFailed, ip, userAgent, req.Method, ct);
            await audit.WriteAsync(AuditAction.LoginFailed, nameof(AppUser), user.Id.ToString(), success: false, message: "mfa_failed", ct: ct);
            throw new AppException("auth.mfaInvalid", 401);
        }
        await cache.RemoveAsync(MfaPrefix + req.MfaToken, ct);
        return await IssueSessionAsync(user, ip, userAgent, ct);
    }

    public async Task SendEmailOtpAsync(string mfaToken, CancellationToken ct)
    {
        var idStr = await cache.GetAsync<string>(MfaPrefix + mfaToken, ct);
        if (idStr is null) throw new AppException("auth.mfaInvalid", 401);
        var user = await users.FindByIdAsync(idStr) ?? throw new AppException("auth.mfaInvalid", 401);
        if (!user.EmailOtpEnabled || string.IsNullOrEmpty(user.Email) || !email.IsConfigured) throw new AppException("auth.smtpNotConfigured", 400);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
        db.Set<OneTimeToken>().Add(new OneTimeToken { UserId = user.Id, Purpose = "EmailOtp", TokenHash = TokenService.Sha256(user.Id + ":" + code), ExpiresAt = Now.AddMinutes(5) });
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email!, "BlueMeal — Mã xác thực / Verification code",
            $"<p>Mã xác thực đăng nhập BlueMeal của bạn: <b>{code}</b> (hiệu lực 5 phút).</p><p>Your BlueMeal sign-in code: <b>{code}</b> (valid for 5 minutes).</p>", ct);
    }

    /// <summary>Creates a session, enforcing the concurrent session limit, and returns tokens.</summary>
    public async Task<SessionTokens> IssueSessionAsync(AppUser user, string? ip, string? userAgent, CancellationToken ct)
    {
        var sp = await settings.GetAsync<SessionPolicySettings>(SettingKeys.SessionPolicy, ct);
        var active = await db.Set<UserSession>().Where(s => s.UserId == user.Id && s.RevokedAt == null && s.ExpiresAt > Now)
            .OrderBy(s => s.LastSeenAt).ToListAsync(ct);
        var excess = active.Count - (sp.MaxConcurrentSessions - 1);
        foreach (var old in active.Take(Math.Max(0, excess))) await RevokeSessionInternal(old, "session_limit");

        var refresh = TokenService.NewOpaqueToken();
        var (restricted, mustChange, mfaSetup) = await RestrictionAsync(user, ct);
        var session = new UserSession
        {
            UserId = user.Id, RefreshTokenHash = TokenService.Sha256(refresh), Ip = ip, UserAgent = Trunc(userAgent, 500),
            Device = DescribeDevice(userAgent), CreatedAt = Now, LastSeenAt = Now, ExpiresAt = Now.AddDays(sp.RefreshTokenDays),
            IsMfaSetupOnly = restricted,
        };
        db.Set<UserSession>().Add(session);
        user.LastLoginAt = Now;
        await db.SaveChangesAsync(ct);
        await cache.SetAsync(SessionKey(session.Id), "1", TimeSpan.FromMinutes(sp.IdleTimeoutMinutes), ct);

        await RecordLogin(user.Id, user.UserName!, LoginResult.Success, ip, userAgent, null, ct);
        await audit.WriteAsync(AuditAction.Login, nameof(AppUser), user.Id.ToString(), message: $"session {session.Id}", ct: ct);

        var (access, exp) = tokens.CreateAccessToken(user, session.Id, restricted, sp.AccessTokenMinutes);
        var dto = await BuildUserDtoAsync(user, ct);
        return new SessionTokens(new LoginResponse(false, null, null, access, exp, restricted, mustChange, mfaSetup, dto), refresh, session.ExpiresAt);
    }

    public async Task<SessionTokens> RefreshAsync(string refreshToken, string? ip, string? userAgent, CancellationToken ct)
    {
        var hash = TokenService.Sha256(refreshToken);
        var session = await db.Set<UserSession>().FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct);
        if (session is null)
        {
            var previous = await db.Set<UserSession>().FirstOrDefaultAsync(s => s.PreviousTokenHash == hash && s.RevokedAt == null, ct);
            // Concurrent refresh (e.g. page reload while a refresh was in flight): accept the just-rotated token
            // within a short grace window and rotate again.
            if (previous is not null && previous.RotatedAt is { } rot && Now - rot < RotationGrace) session = previous;
            else
            {
                // Re-use of a rotated token outside the grace window → likely theft: revoke the whole session (A-07)
                if (previous is not null) { await RevokeSessionInternal(previous, "refresh_reuse"); await db.SaveChangesAsync(ct); }
                throw new AppException("error.unauthorized", 401);
            }
        }
        var sp = await settings.GetAsync<SessionPolicySettings>(SettingKeys.SessionPolicy, ct);
        if (!session.IsActive(Now)) throw new AppException("auth.sessionRevoked", 401);
        if (!await SessionAliveAsync(session, sp, ct))
        {
            await RevokeSessionInternal(session, "idle_timeout");
            await db.SaveChangesAsync(ct);
            throw new AppException("auth.sessionRevoked", 401);
        }
        var user = await users.FindByIdAsync(session.UserId.ToString());
        if (user is null || !user.IsActive) { await RevokeSessionInternal(session, "user_disabled"); await db.SaveChangesAsync(ct); throw new AppException("auth.disabled", 401); }

        var newRefresh = TokenService.NewOpaqueToken();
        if (session.RefreshTokenHash == hash) session.PreviousTokenHash = session.RefreshTokenHash; // keep the original previous hash on grace re-rotation
        session.RefreshTokenHash = TokenService.Sha256(newRefresh);
        session.RotatedAt = Now;
        session.LastSeenAt = Now;
        session.Ip = ip ?? session.Ip;
        var (restricted, mustChange, mfaSetup) = await RestrictionAsync(user, ct);
        session.IsMfaSetupOnly = restricted;
        await db.SaveChangesAsync(ct);
        await cache.SetAsync(SessionKey(session.Id), "1", TimeSpan.FromMinutes(sp.IdleTimeoutMinutes), ct);

        var (access, exp) = tokens.CreateAccessToken(user, session.Id, restricted, sp.AccessTokenMinutes);
        return new SessionTokens(new LoginResponse(false, null, null, access, exp, restricted, mustChange, mfaSetup, await BuildUserDtoAsync(user, ct)), newRefresh, session.ExpiresAt);
    }

    /// <summary>Session is alive if the sliding Redis key exists, otherwise falls back to DB last_seen.</summary>
    private async Task<bool> SessionAliveAsync(UserSession s, SessionPolicySettings sp, CancellationToken ct)
        => await cache.GetAsync<string>(SessionKey(s.Id), ct) is not null || s.LastSeenAt.AddMinutes(sp.IdleTimeoutMinutes) >= Now;

    public async Task LogoutAsync(Guid? sessionId, string? refreshToken, CancellationToken ct)
    {
        UserSession? s = null;
        if (sessionId is not null) s = await db.Set<UserSession>().FirstOrDefaultAsync(x => x.Id == sessionId, ct);
        if (s is null && !string.IsNullOrEmpty(refreshToken))
        {
            var h = TokenService.Sha256(refreshToken);
            s = await db.Set<UserSession>().FirstOrDefaultAsync(x => x.RefreshTokenHash == h, ct);
        }
        if (s is null) return;
        await RevokeSessionInternal(s, "logout");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Logout, nameof(AppUser), s.UserId.ToString(), message: $"session {s.Id}", ct: ct);
    }

    public async Task<int> RevokeUserSessionsAsync(Guid userId, string reason, Guid? exceptSession, CancellationToken ct)
    {
        var list = await db.Set<UserSession>().Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSession).ToListAsync(ct);
        foreach (var s in list) await RevokeSessionInternal(s, reason);
        await db.SaveChangesAsync(ct);
        return list.Count;
    }

    public async Task RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        var s = await db.Set<UserSession>().FirstOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw new NotFoundException("session");
        await RevokeSessionInternal(s, reason);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.SessionRevoke, nameof(UserSession), s.Id.ToString(), message: reason, ct: ct);
    }

    private async Task RevokeSessionInternal(UserSession s, string reason)
    {
        if (s.RevokedAt is null) { s.RevokedAt = Now; s.RevokedReason = reason; }
        await cache.RemoveAsync(SessionKey(s.Id));
    }

    // ------------------------------------------------------------------ request-time session validation

    /// <summary>Validates sid on every request (revocation + idle timeout). Touches last_seen at most once per minute.</summary>
    public async Task<bool> ValidateSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var sp = await settings.GetAsync<SessionPolicySettings>(SettingKeys.SessionPolicy, ct);
        if (await cache.GetAsync<string>(SessionKey(sessionId), ct) is null)
        {
            var s = await db.Set<UserSession>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionId, ct);
            if (s is null || !s.IsActive(Now) || s.LastSeenAt.AddMinutes(sp.IdleTimeoutMinutes) < Now) return false;
        }
        await cache.SetAsync(SessionKey(sessionId), "1", TimeSpan.FromMinutes(sp.IdleTimeoutMinutes), ct);
        if (await cache.TrySetAsync("sess-touch:" + sessionId, "1", TimeSpan.FromSeconds(60), ct))
            await db.Set<UserSession>().Where(x => x.Id == sessionId).ExecuteUpdateAsync(u => u.SetProperty(x => x.LastSeenAt, Now), ct);
        return true;
    }

    // ------------------------------------------------------------------ password

    public async Task ValidatePasswordPolicyAsync(AppUser user, string password, CancellationToken ct)
    {
        var p = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        var ok = password.Length >= p.MinLength
                 && (!p.RequireUpper || password.Any(char.IsUpper))
                 && (!p.RequireLower || password.Any(char.IsLower))
                 && (!p.RequireDigit || password.Any(char.IsDigit))
                 && (!p.RequireSpecial || password.Any(c => !char.IsLetterOrDigit(c)));
        if (!ok) throw new AppException("auth.passwordPolicy", 400, p.MinLength) { Errors = new Dictionary<string, string[]> { ["newPassword"] = [L.T("auth.passwordPolicy", p.MinLength)] } };
        if (p.HistoryCount > 0)
        {
            var history = await db.Set<PasswordHistory>().AsNoTracking().Where(h => h.UserId == user.Id)
                .OrderByDescending(h => h.CreatedAt).Take(p.HistoryCount).Select(h => h.PasswordHash).ToListAsync(ct);
            if (user.PasswordHash is not null) history.Add(user.PasswordHash);
            if (history.Any(h => users.PasswordHasher.VerifyHashedPassword(user, h, password) != PasswordVerificationResult.Failed))
                throw new AppException("auth.passwordReused", 400, p.HistoryCount) { Errors = new Dictionary<string, string[]> { ["newPassword"] = [L.T("auth.passwordReused", p.HistoryCount)] } };
        }
    }

    /// <summary>Sets a password (policy + history), stores history, revokes sessions except the given one.</summary>
    public async Task SetPasswordAsync(AppUser user, string newPassword, bool mustChangeNext, Guid? keepSession, CancellationToken ct)
    {
        await ValidatePasswordPolicyAsync(user, newPassword, ct);
        if (user.PasswordHash is not null)
            db.Set<PasswordHistory>().Add(new PasswordHistory { UserId = user.Id, PasswordHash = user.PasswordHash, CreatedAt = Now });
        user.PasswordHash = users.PasswordHasher.HashPassword(user, newPassword);
        user.MustChangePassword = mustChangeNext;
        user.PasswordChangedAt = Now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await users.UpdateAsync(user);
        await RevokeUserSessionsAsync(user.Id, "password_changed", keepSession, ct);
        await audit.WriteAsync(AuditAction.PasswordChange, nameof(AppUser), user.Id.ToString(), ct: ct);
    }

    public async Task ChangePasswordAsync(Guid userId, string current, string next, Guid? sessionId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("user");
        if (!await users.CheckPasswordAsync(user, current))
            throw new AppException("auth.passwordWrong", 400) { Errors = new Dictionary<string, string[]> { ["currentPassword"] = [L.T("auth.passwordWrong")] } };
        await SetPasswordAsync(user, next, false, sessionId, ct);
    }

    public async Task ForgotPasswordAsync(string userNameOrEmail, string resetBaseUrl, CancellationToken ct)
    {
        var key = (userNameOrEmail ?? "").Trim().ToUpperInvariant();
        var user = await users.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == key || u.NormalizedEmail == key, ct);
        if (user is null || !user.IsActive || string.IsNullOrEmpty(user.Email) || !email.IsConfigured)
        {
            log.LogInformation("Forgot password requested for non-deliverable account");
            return; // neutral response (anti-enumeration)
        }
        var token = TokenService.NewOpaqueToken();
        db.Set<OneTimeToken>().Add(new OneTimeToken { UserId = user.Id, Purpose = "PasswordReset", TokenHash = TokenService.Sha256(token), ExpiresAt = Now.AddMinutes(15) });
        await db.SaveChangesAsync(ct);
        var link = $"{resetBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
        await email.SendAsync(user.Email!, "BlueMeal — Đặt lại mật khẩu / Password reset",
            $"<p>Bạn (hoặc ai đó) vừa yêu cầu đặt lại mật khẩu BlueMeal. Liên kết có hiệu lực 15 phút và chỉ dùng 1 lần:</p><p><a href=\"{link}\">{link}</a></p>" +
            $"<p>Someone requested a BlueMeal password reset. The link is valid for 15 minutes and can be used once.</p>", ct);
    }

    public async Task ResetPasswordAsync(string token, string newPassword, CancellationToken ct)
    {
        var hash = TokenService.Sha256(token ?? "");
        var ott = await db.Set<OneTimeToken>().FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == "PasswordReset", ct);
        if (ott is null || ott.UsedAt is not null || ott.ExpiresAt < Now) throw new AppException("auth.resetInvalid", 400);
        var user = await users.FindByIdAsync(ott.UserId.ToString()) ?? throw new AppException("auth.resetInvalid", 400);
        await SetPasswordAsync(user, newPassword, false, null, ct);
        ott.UsedAt = Now;
        user.LockoutEnd = null;
        await users.UpdateAsync(user);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ MFA

    public async Task<(string Secret, string OtpAuthUri, string QrPngBase64)> BeginMfaSetupAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("user");
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        await cache.SetAsync("mfa-setup:" + userId, secret, TimeSpan.FromMinutes(10), ct);
        var issuer = Uri.EscapeDataString(config["Mfa:Issuer"] ?? "BlueMeal");
        var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.UserName!)}?secret={secret}&issuer={issuer}&digits=6&period=30";
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(6);
        return (secret, uri, Convert.ToBase64String(png));
    }

    public async Task<string[]> EnableMfaAsync(Guid userId, string code, CancellationToken ct)
    {
        var secret = await cache.GetAsync<string>("mfa-setup:" + userId, ct) ?? throw new AppException("auth.mfaInvalid", 400);
        if (!VerifyTotp(secret, code)) throw new AppException("auth.mfaInvalid", 400);
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("user");
        user.MfaSecret = secret;
        user.TwoFactorEnabled = true;
        await users.UpdateAsync(user);
        await cache.RemoveAsync("mfa-setup:" + userId, ct);
        var codes = await RegenerateRecoveryCodesAsync(userId, ct);
        await audit.WriteAsync(AuditAction.MfaChange, nameof(AppUser), userId.ToString(), newValues: new { mfa = "enabled" }, ct: ct);
        return codes;
    }

    public async Task DisableMfaAsync(Guid userId, bool byAdmin, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("user");
        if (!byAdmin && await IsMfaMandatoryAsync(user, ct)) throw new AppException("auth.mfaRequired", 400);
        user.MfaSecret = null;
        user.TwoFactorEnabled = false;
        await users.UpdateAsync(user);
        await db.Set<MfaRecoveryCode>().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        await audit.WriteAsync(AuditAction.MfaChange, nameof(AppUser), userId.ToString(), newValues: new { mfa = byAdmin ? "reset_by_admin" : "disabled" }, ct: ct);
    }

    public async Task<string[]> RegenerateRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        await db.Set<MfaRecoveryCode>().Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        var codes = Enumerable.Range(0, 10).Select(_ => $"{RandomCode(4)}-{RandomCode(4)}").ToArray();
        foreach (var c in codes) db.Set<MfaRecoveryCode>().Add(new MfaRecoveryCode { UserId = userId, CodeHash = TokenService.Sha256(userId + ":" + c) });
        await db.SaveChangesAsync(ct);
        return codes;
    }

    /// <summary>Re-authentication (password + MFA code) for sensitive actions such as downloading backups (SPEC §10.7).</summary>
    public async Task<bool> ReauthenticateAsync(Guid userId, string password, string? code, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || !await users.CheckPasswordAsync(user, password)) return false;
        if (user.TwoFactorEnabled)
            return (VerifyTotp(user.MfaSecret, code ?? "") && await ConsumeTotpAsync(userId, code!, ct)) || await UseRecoveryCodeAsync(userId, code ?? "", ct);
        return true;
    }

    /// <summary>Replay protection: each TOTP code can be used once per user within its validity window.</summary>
    private Task<bool> ConsumeTotpAsync(Guid userId, string code, CancellationToken ct)
        => cache.TrySetAsync($"totp-used:{userId:N}:{code.Trim()}", "1", TimeSpan.FromSeconds(120), ct);

    public static bool VerifyTotp(string? secret, string code)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(code)) return false;
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code.Trim().Replace(" ", ""), out _, new VerificationWindow(1, 1));
    }

    private async Task<bool> UseRecoveryCodeAsync(Guid userId, string code, CancellationToken ct)
    {
        var hash = TokenService.Sha256(userId + ":" + (code ?? "").Trim().ToUpperInvariant());
        var rc = await db.Set<MfaRecoveryCode>().FirstOrDefaultAsync(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null, ct);
        if (rc is null) return false;
        rc.UsedAt = Now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> VerifyOneTimeAsync(Guid userId, string purpose, string code, CancellationToken ct)
    {
        var hash = TokenService.Sha256(userId + ":" + (code ?? "").Trim());
        var t = await db.Set<OneTimeToken>().FirstOrDefaultAsync(x => x.UserId == userId && x.Purpose == purpose && x.TokenHash == hash && x.UsedAt == null && x.ExpiresAt > Now, ct);
        if (t is null) return false;
        t.UsedAt = Now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> IsMfaMandatoryAsync(AppUser user, CancellationToken ct)
    {
        if (user.MfaRequired) return true;
        var policy = await settings.GetAsync<MfaPolicySettings>(SettingKeys.MfaPolicy, ct);
        if (policy.RequireForAllUsers) return true;
        var required = policy.RequiredRoles.Concat(DefaultRoles.MfaMandatory).ToHashSet();
        var roles = await db.Set<UserRole>().Where(ur => ur.UserId == user.Id).Select(ur => new { ur.Role!.Name, ur.Role.RequiresMfa }).ToListAsync(ct);
        return roles.Any(r => r.RequiresMfa || required.Contains(r.Name!));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(bool Restricted, bool MustChange, bool MfaSetup)> RestrictionAsync(AppUser user, CancellationToken ct)
    {
        var p = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        var expired = p.ExpiryDays > 0 && user.PasswordChangedAt is { } pc && pc.AddDays(p.ExpiryDays) < Now;
        var mustChange = user.MustChangePassword || expired;
        var mfaSetup = !user.TwoFactorEnabled && await IsMfaMandatoryAsync(user, ct);
        return (mustChange || mfaSetup, mustChange, mfaSetup);
    }

    public async Task<AuthUserDto> BuildUserDtoAsync(AppUser user, CancellationToken ct)
    {
        var access = await permissionService.LoadAsync(user.Id, ct);
        var p = await settings.GetAsync<PasswordPolicySettings>(SettingKeys.PasswordPolicy, ct);
        var expired = p.ExpiryDays > 0 && user.PasswordChangedAt is { } pc && pc.AddDays(p.ExpiryDays) < Now;
        return new AuthUserDto(user.Id, user.UserName!, user.FullName, user.Email, user.Language, user.Theme, access.Roles, access.Permissions,
            user.TwoFactorEnabled, await IsMfaMandatoryAsync(user, ct), user.MustChangePassword, expired, access.Warehouses, access.Customers,
            await PrivacyConsentRequiredAsync(user.Id, ct));
    }

    // ------------------------------------------------------------------ personal-data notice (Luật 91/2025/QH15)

    /// <summary>True when the notice is enabled and the user has not accepted its current version.</summary>
    public async Task<bool> PrivacyConsentRequiredAsync(Guid userId, CancellationToken ct)
    {
        var notice = await settings.GetAsync<PrivacyNoticeSettings>(SettingKeys.PrivacyNotice, ct);
        return notice.Enabled && !await db.Set<PrivacyConsent>().AnyAsync(c => c.UserId == userId && c.NoticeVersion == notice.Version, ct);
    }

    /// <summary>
    /// Records acceptance of the notice version the user was shown. A stale version (the text changed meanwhile)
    /// is rejected so that consent always refers to the text actually read.
    /// </summary>
    public async Task AcceptPrivacyNoticeAsync(Guid userId, int version, string language, string? ip, string? userAgent, CancellationToken ct)
    {
        var notice = await settings.GetAsync<PrivacyNoticeSettings>(SettingKeys.PrivacyNotice, ct);
        if (version != notice.Version) throw new AppException("privacy.noticeChanged", 409);
        if (await db.Set<PrivacyConsent>().AnyAsync(c => c.UserId == userId && c.NoticeVersion == version, ct)) return;
        db.Set<PrivacyConsent>().Add(new PrivacyConsent
        {
            UserId = userId, NoticeVersion = version, AcceptedAt = Now, Language = language is "en" ? "en" : "vi", Ip = ip, UserAgent = Trunc(userAgent, 500),
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditAction.Consent, nameof(PrivacyConsent), userId.ToString(), newValues: new { noticeVersion = version }, ct: ct);
    }

    private async Task RegisterFailure(AppUser? user, string name, string? ip, string? ua, LoginResult result, string reason, CancellationToken ct)
    {
        await cache.IncrementAsync(FailKey("u", name), TimeSpan.FromMinutes(15), ct);
        await cache.IncrementAsync(FailKey("ip", ip ?? ""), TimeSpan.FromMinutes(15), ct);
        await RecordLogin(user?.Id, name, result, ip, ua, reason, ct);
        await audit.WriteAsync(AuditAction.LoginFailed, nameof(AppUser), user?.Id.ToString(), success: false, message: $"{name}: {reason}", ct: ct);
    }

    private async Task RecordLogin(Guid? userId, string userName, LoginResult result, string? ip, string? ua, string? reason, CancellationToken ct)
    {
        db.Set<LoginHistory>().Add(new LoginHistory { UserId = userId, UserName = Trunc(userName, 100)!, At = Now, Result = result, Ip = ip, UserAgent = Trunc(ua, 500), Reason = reason });
        await db.SaveChangesAsync(ct);
    }

    private static string FailKey(string kind, string id) => $"lf:{kind}:{id.ToLowerInvariant()}";
    public static string SessionKey(Guid sid) => "sess:" + sid.ToString("N");
    private static string? Trunc(string? s, int n) => s is null || s.Length <= n ? s : s[..n];

    private static string RandomCode(int len)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder(len);
        for (var i = 0; i < len; i++) sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        return sb.ToString();
    }

    public static string? DescribeDevice(string? ua)
    {
        if (string.IsNullOrEmpty(ua)) return null;
        var os = ua.Contains("Android") ? "Android" : ua.Contains("iPhone") || ua.Contains("iPad") ? "iOS" : ua.Contains("Windows") ? "Windows" : ua.Contains("Mac OS") ? "macOS" : ua.Contains("Linux") ? "Linux" : "Other";
        var browser = ua.Contains("coc_coc") || ua.Contains("CocCoc") ? "Cốc Cốc" : ua.Contains("Edg/") ? "Edge" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : ua.Contains("Firefox/") ? "Firefox" : "Other";
        return $"{browser} / {os}";
    }
}
