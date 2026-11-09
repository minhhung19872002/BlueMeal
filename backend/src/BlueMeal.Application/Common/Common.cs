using System.Globalization;

namespace BlueMeal.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalItems);

/// <summary>Standard list query: ?page=&amp;pageSize=&amp;sort=&amp;q= (SPEC §8). Sort: "field" or "-field" (desc), comma separated.</summary>
public class PageQuery
{
    public const int MaxPageSize = 200;
    private int _page = 1;
    private int _pageSize = 20;
    public int Page { get => _page; set => _page = value < 1 ? 1 : value; }
    public int PageSize { get => _pageSize; set => _pageSize = value < 1 ? 20 : Math.Min(value, MaxPageSize); }
    public string? Sort { get; set; }
    public string? Q { get; set; }
    public bool? IsActive { get; set; }
    public bool IncludeDeleted { get; set; }
}

/// <summary>Business error carrying a localizable message key (rendered as RFC 7807 by the API).</summary>
public class AppException : Exception
{
    public string Key { get; }
    public object[] Args { get; }
    public int StatusCode { get; }
    public IDictionary<string, string[]>? Errors { get; init; }

    public AppException(string key, int statusCode = 400, params object[] args) : base(key)
    {
        Key = key; Args = args; StatusCode = statusCode;
    }
}

public sealed class NotFoundException(string entity = "record") : AppException("error.notFound", 404, entity);
public sealed class ForbiddenException(string key = "error.forbidden") : AppException(key, 403);
public sealed class ConcurrencyException() : AppException("error.concurrency", 409);
public sealed class ConflictException(string key, params object[] args) : AppException(key, 409, args);
public sealed class BusinessRuleException(string key, params object[] args) : AppException(key, 422, args);

/// <summary>Bilingual message catalog for server-side messages (errors, notifications, Excel headers).</summary>
public static partial class L
{
    public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en";

    public static string T(string key, params object[] args)
    {
        var lang = IsEnglish ? 1 : 0;
        if (!Messages.TryGetValue(key, out var pair)) return key;
        var text = lang == 1 ? pair.En : pair.Vi;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    public static string Pick(string vi, string? en) => IsEnglish && !string.IsNullOrEmpty(en) ? en : vi;

    public static bool Has(string key) => Messages.ContainsKey(key);
}
