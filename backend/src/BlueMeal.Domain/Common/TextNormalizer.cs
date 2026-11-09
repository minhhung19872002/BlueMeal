using System.Globalization;
using System.Text;

namespace BlueMeal.Domain.Common;

/// <summary>Vietnamese-aware text normalization for accent-insensitive search (ASSUMPTIONS A-04).</summary>
public static class TextNormalizer
{
    /// <summary>Lower-case and strip diacritics, also maps đ/Đ → d. "Thịt Lợn Đùi" → "thit lon dui".</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var decomposed = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'đ' => 'd', 'Đ' => 'd', _ => c });
        }
        // collapse whitespace
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Build(IEnumerable<string?> parts)
        => Normalize(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));
}
