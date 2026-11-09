using System.Security.Cryptography;
using System.Text;
using BlueMeal.Application.Abstractions;

namespace BlueMeal.Api.Auth;

/// <summary>Self-hosted arithmetic CAPTCHA rendered as SVG with noise (ASSUMPTIONS A-09). No external service.</summary>
public sealed class CaptchaService(ICacheService cache)
{
    public sealed record Challenge(string Id, string Svg);

    public async Task<Challenge> CreateAsync(CancellationToken ct = default)
    {
        var a = RandomNumberGenerator.GetInt32(2, 20);
        var b = RandomNumberGenerator.GetInt32(1, 10);
        var plus = RandomNumberGenerator.GetInt32(0, 2) == 0 || a < b;
        var answer = plus ? a + b : a - b;
        var id = TokenService.NewOpaqueToken(16);
        await cache.SetAsync("captcha:" + id, TokenService.Sha256(answer.ToString()), TimeSpan.FromMinutes(5), ct);
        return new Challenge(id, Render($"{a} {(plus ? "+" : "−")} {b} = ?"));
    }

    /// <summary>Single-use verification.</summary>
    public async Task<bool> VerifyAsync(string? id, string? answer, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(answer)) return false;
        var stored = await cache.GetAsync<string>("captcha:" + id, ct);
        await cache.RemoveAsync("captcha:" + id, ct);
        return stored is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(stored), Encoding.ASCII.GetBytes(TokenService.Sha256(answer.Trim())));
    }

    private static string Render(string text)
    {
        var sb = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"180\" height=\"56\" viewBox=\"0 0 180 56\">");
        sb.Append("<rect width=\"180\" height=\"56\" fill=\"#f3f8f4\"/>");
        for (var i = 0; i < 6; i++)
            sb.Append($"<line x1=\"{R(0, 180)}\" y1=\"{R(0, 56)}\" x2=\"{R(0, 180)}\" y2=\"{R(0, 56)}\" stroke=\"#{R(100, 200):x2}{R(120, 200):x2}{R(100, 200):x2}\" stroke-width=\"1.5\"/>");
        var x = 14;
        foreach (var ch in text)
        {
            if (ch == ' ') { x += 8; continue; }
            sb.Append($"<text x=\"{x}\" y=\"{R(34, 42)}\" font-family=\"monospace\" font-size=\"{R(22, 28)}\" fill=\"#2f5d3a\" transform=\"rotate({R(-15, 15)} {x} 36)\">{System.Net.WebUtility.HtmlEncode(ch.ToString())}</text>");
            x += 18;
        }
        for (var i = 0; i < 30; i++) sb.Append($"<circle cx=\"{R(0, 180)}\" cy=\"{R(0, 56)}\" r=\"1\" fill=\"#7a9a80\"/>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static int R(int min, int max) => RandomNumberGenerator.GetInt32(min, max);
}
