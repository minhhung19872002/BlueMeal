using System.Security.Cryptography;
using System.Text;
using BlueMeal.Api.Auth;
using BlueMeal.Api.Authorization;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Authorization;
using BlueMeal.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using BlueMeal.Domain.Entities;

namespace BlueMeal.Api.Controllers;

/// <summary>Authentication: login, MFA, refresh (rotating, HttpOnly cookie), logout, forgot/reset password (SPEC §7.1.11, §10.1).</summary>
[Route("api/v1/auth")]
public sealed class AuthController(AuthService auth, CaptchaService captcha, JwtOptions jwt, IEmailSender email, IConfiguration config, UserManager<AppUser> users) : ApiControllerBase
{
    public const string RefreshCookie = "bm_rt";
    public const string CsrfCookie = "bm_csrf";
    public const string CsrfHeader = "X-BlueMeal-CSRF";

    public sealed record ForgotRequest(string UserNameOrEmail);
    public sealed record ResetRequest(string Token, string NewPassword);

    [HttpGet("options"), AllowAnonymous]
    public IActionResult Options() => Ok(new { smtpConfigured = email.IsConfigured, languages = new[] { "vi", "en" } });

    [HttpGet("captcha"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Captcha(CancellationToken ct)
    {
        var c = await captcha.CreateAsync(ct);
        return Ok(new { id = c.Id, svg = c.Svg });
    }

    [HttpGet("captcha-status"), AllowAnonymous]
    public async Task<IActionResult> CaptchaStatus([FromQuery] string userName, CancellationToken ct)
        => Ok(new { required = await auth.CaptchaRequiredAsync(userName ?? "", ClientIp, ct) });

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(req, ClientIp, UserAgentHeader, ct) ?? throw new AppException("auth.invalidCredentials", 401);
        return Issue(result);
    }

    [HttpPost("mfa/verify"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> VerifyMfa([FromBody] MfaVerifyRequest req, CancellationToken ct)
        => Issue(await auth.VerifyMfaAsync(req, ClientIp, UserAgentHeader, ct));

    [HttpPost("mfa/email-otp"), AllowAnonymous, EnableRateLimiting("forgot")]
    public async Task<IActionResult> SendEmailOtp([FromBody] MfaVerifyRequest req, CancellationToken ct)
    {
        await auth.SendEmailOtpAsync(req.MfaToken, ct);
        return NoContent();
    }

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken ct)
    {
        EnsureCsrf();
        var token = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(token)) throw new AppException("error.unauthorized", 401);
        try
        {
            return Issue(await auth.RefreshAsync(token, ClientIp, UserAgentHeader, ct));
        }
        catch (AppException) { ClearCookies(); throw; }
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        EnsureCsrf();
        await auth.LogoutAsync(SessionId, Request.Cookies[RefreshCookie], ct);
        ClearCookies();
        return NoContent();
    }

    [HttpGet("me"), HasPermission(Permissions.Self), AllowRestricted]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var user = await users.FindByIdAsync(UserId.ToString()) ?? throw new NotFoundException("user");
        return await auth.BuildUserDtoAsync(user, ct);
    }

    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("forgot")]
    public async Task<IActionResult> Forgot([FromBody] ForgotRequest req, CancellationToken ct)
    {
        var baseUrl = config["App:PublicUrl"] ?? $"{Request.Scheme}://{Request.Host}";
        await auth.ForgotPasswordAsync(req.UserNameOrEmail, baseUrl, ct);
        // Always neutral (anti account enumeration)
        return Ok(new { message = L.T("auth.forgotSent") });
    }

    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("forgot")]
    public async Task<IActionResult> Reset([FromBody] ResetRequest req, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(req.Token, req.NewPassword, ct);
        return NoContent();
    }

    // ------------------------------------------------------------------

    private ActionResult<LoginResponse> Issue(SessionTokens t)
    {
        if (!string.IsNullOrEmpty(t.RefreshToken))
        {
            Response.Cookies.Append(RefreshCookie, t.RefreshToken, new CookieOptions
            {
                HttpOnly = true, Secure = jwt.CookieSecure, SameSite = SameSiteMode.Strict, Path = "/api/v1/auth", Expires = t.RefreshExpiresAt, IsEssential = true,
            });
            Response.Cookies.Append(CsrfCookie, TokenService.NewOpaqueToken(16), new CookieOptions
            {
                HttpOnly = false, Secure = jwt.CookieSecure, SameSite = SameSiteMode.Strict, Path = "/", Expires = t.RefreshExpiresAt, IsEssential = true,
            });
        }
        return Ok(t.Response);
    }

    private void ClearCookies()
    {
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/v1/auth", Secure = jwt.CookieSecure, SameSite = SameSiteMode.Strict });
        Response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/", Secure = jwt.CookieSecure, SameSite = SameSiteMode.Strict });
    }

    /// <summary>Double-submit CSRF check for cookie-authenticated endpoints (A-08).</summary>
    private void EnsureCsrf()
    {
        var cookie = Request.Cookies[CsrfCookie];
        var header = Request.Headers[CsrfHeader].ToString();
        if (string.IsNullOrEmpty(cookie) && string.IsNullOrEmpty(Request.Cookies[RefreshCookie])) return; // nothing to protect
        if (string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(header) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(cookie), Encoding.UTF8.GetBytes(header)))
            throw new AppException("auth.csrf", 403);
    }
}
