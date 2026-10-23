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

    /// <summary>Active people for owner pickers. Staff without access to the user list get an empty list.</summary>
    public static async Task<List<UserListItem>> UsersAsync(HttpClient http)
    {
        try
        {
            var page = await http.GetFromJsonAsync<PagedResult<UserListItem>>("api/v1/users?pageSize=200");
            return page?.Items.Where(u => u.IsActive).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}
