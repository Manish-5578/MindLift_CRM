using System.Net.Http.Json;
using MindLift.Shared.Contracts;
using MindLift.UI.Components;

namespace MindLift.Web.Services;

/// <summary>Searches that feed the record and user pickers.</summary>
public static class RecordLookups
{
    /// <summary>Accounts whose name or website matches the text, newest first, at most eight.</summary>
    public static async Task<IReadOnlyList<LookupItem>> AccountsAsync(HttpClient http, string text, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/v1/accounts/query", new ListQuery(text, null, "name", "asc", 1, 8), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var page = await response.Content.ReadFromJsonAsync<PagedResult<AccountListItemDto>>(cancellationToken);
            return page?.Items.Select(a => new LookupItem(a.Id, a.Name)).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>Contacts whose name or email matches the text, at most eight.</summary>
    public static async Task<IReadOnlyList<LookupItem>> ContactsAsync(HttpClient http, string text, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/v1/contacts/query", new ListQuery(text, null, "first_name", "asc", 1, 8), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var page = await response.Content.ReadFromJsonAsync<PagedResult<ContactListItemDto>>(cancellationToken);
            return page?.Items.Select(c => new LookupItem(c.Id, c.FullName)).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>Deals whose name or account matches the text, at most eight.</summary>
    public static async Task<IReadOnlyList<LookupItem>> DealsAsync(HttpClient http, string text, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/v1/deals/query", new ListQuery(text, null, "name", "asc", 1, 8), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var page = await response.Content.ReadFromJsonAsync<PagedResult<DealListItemDto>>(cancellationToken);
            return page?.Items.Select(d => new LookupItem(d.Id, d.Name)).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private static List<UserListItem>? _cachedUsers;
    private static DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _userLock = new(1, 1);

    /// <summary>Invalidates the user cache so the next UsersAsync call pulls fresh data.</summary>
    public static void InvalidateUsersCache()
    {
        _cachedUsers = null;
    }

    /// <summary>Active people for owner pickers. Cached for 2 minutes across views to optimize load time.</summary>
    public static async Task<List<UserListItem>> UsersAsync(HttpClient http, bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedUsers is not null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedUsers;
        }

        await _userLock.WaitAsync();
        try
        {
            if (!forceRefresh && _cachedUsers is not null && DateTime.UtcNow < _cacheExpiry)
            {
                return _cachedUsers;
            }

            var page = await http.GetFromJsonAsync<PagedResult<UserListItem>>("api/v1/users?pageSize=200");
            var items = page?.Items.Where(u => u.IsActive).ToList() ?? [];
            _cachedUsers = items;
            _cacheExpiry = DateTime.UtcNow.AddMinutes(2);
            return items;
        }
        catch (HttpRequestException)
        {
            return _cachedUsers ?? [];
        }
        finally
        {
            _userLock.Release();
        }
    }
}
