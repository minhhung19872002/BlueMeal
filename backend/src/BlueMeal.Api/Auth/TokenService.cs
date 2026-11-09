using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BlueMeal.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace BlueMeal.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "bluemeal";
    public string Audience { get; set; } = "bluemeal-web";
    /// <summary>HS256 signing key, ≥ 32 bytes (env BLUEMEAL_JWT_KEY).</summary>
    public string Key { get; set; } = default!;
    public int AccessTokenMinutes { get; set; } = 15;
    public bool CookieSecure { get; set; } = true;
}

public sealed class TokenService(JwtOptions options, TimeProvider clock)
{
    public SymmetricSecurityKey SigningKey { get; } = new(Encoding.UTF8.GetBytes(options.Key));

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AppUser user, Guid sessionId, bool restricted, int? minutes = null)
    {
        var now = clock.GetUtcNow();
        var exp = now.AddMinutes(minutes ?? options.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Name, user.UserName!),
            new("full_name", user.FullName),
            new("sid", sessionId.ToString()),
            new("scope", restricted ? "restricted" : "full"),
            new("lang", user.Language),
        };
        var token = new JwtSecurityToken(options.Issuer, options.Audience, claims, now.UtcDateTime, exp.UtcDateTime,
            new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), exp);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = options.Issuer,
        ValidateAudience = true, ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = SigningKey,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = JwtRegisteredClaimNames.Name,
    };

    public static string NewOpaqueToken(int bytes = 32) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
