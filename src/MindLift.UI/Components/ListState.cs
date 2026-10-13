using System.Text;
using System.Text.Json;
using MindLift.Shared.Filtering;

namespace MindLift.UI.Components;

/// <summary>
/// Everything that decides which rows a list shows: the view tab, the search text, the page, the extra filters and the sort.
/// It is kept in the address (plan 18.8.1: "?view=…&amp;q=…&amp;page=2") so refresh, back and shared links restore the same list.
/// </summary>
public sealed record ListState(string View, string? Search = null, int Page = 1, FilterGroup? Filter = null, string? SortField = null, string? SortDir = null)
{
    public const string ViewKey = "view";
    public const string SearchKey = "q";
    public const string PageKey = "page";
    public const string FilterKey = "f";
    public const string SortKey = "sort";
    public const string DirKey = "dir";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>True when the user has narrowed the list beyond the view tab (search text or extra filters).</summary>
    public bool IsNarrowed => !string.IsNullOrWhiteSpace(Search) || HasFilter;

    public bool HasFilter => Filter is { Conditions.Count: > 0 } or { Groups.Count: > 0 };

    /// <summary>Reads the state from a query string such as "view=mine&amp;q=sam&amp;page=2". Unknown or broken values fall back to the defaults.</summary>
    public static ListState FromQuery(string? queryString, string defaultView, IReadOnlyCollection<string>? knownViews = null)
    {
        var values = ParseQuery(queryString);

        var view = values.GetValueOrDefault(ViewKey) ?? defaultView;
        if (knownViews is not null && !knownViews.Contains(view))
        {
            view = defaultView;
        }

        var page = int.TryParse(values.GetValueOrDefault(PageKey), out var p) && p >= 1 ? p : 1;
        var search = values.GetValueOrDefault(SearchKey);
        var dir = values.GetValueOrDefault(DirKey) is "asc" or "desc" ? values[DirKey] : null;
        var sort = string.IsNullOrWhiteSpace(values.GetValueOrDefault(SortKey)) ? null : values[SortKey];

        return new ListState(view, string.IsNullOrWhiteSpace(search) ? null : search, page, DecodeFilter(values.GetValueOrDefault(FilterKey)), sort, sort is null ? null : dir ?? "asc");
    }

    /// <summary>The values to put in the address. Defaults are left out so a plain list has a plain address.</summary>
    public IReadOnlyDictionary<string, string?> ToQuery(string defaultView)
    {
        var query = new Dictionary<string, string?>
        {
            [ViewKey] = View == defaultView ? null : View,
            [SearchKey] = string.IsNullOrWhiteSpace(Search) ? null : Search,
            [PageKey] = Page > 1 ? Page.ToString() : null,
            [FilterKey] = HasFilter ? EncodeFilter(Filter!) : null,
            [SortKey] = SortField,
            [DirKey] = SortField is null ? null : SortDir,
        };
        return query;
    }

    /// <summary>A new state for a changed view, search or filter. The page goes back to 1 because the old page may not exist any more.</summary>
    public ListState Reset(string? view = null, string? search = null, FilterGroup? filter = null, bool clearSearch = false, bool clearFilter = false)
        => this with
        {
            View = view ?? View,
            Search = clearSearch ? null : search ?? Search,
            Filter = clearFilter ? null : filter ?? Filter,
            Page = 1,
        };

    public static string EncodeFilter(FilterGroup filter)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(filter, Json));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static FilterGroup? DecodeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var group = JsonSerializer.Deserialize<FilterGroup>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)), Json);
            return group is { Conditions.Count: > 0 } or { Groups.Count: > 0 } ? group : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // A damaged link must not break the page. Show the list without the filter.
            return null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string? queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return result;
        }

        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}
