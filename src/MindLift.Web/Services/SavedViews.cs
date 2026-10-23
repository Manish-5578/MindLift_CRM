using System.Net.Http.Json;
using MindLift.Shared.Contracts;
using MindLift.UI.Components;

namespace MindLift.Web.Services;

/// <summary>A list's saved views (plan 4.6.6): the user's own and those colleagues shared, read and stored through the views API.</summary>
public static class SavedViews
{
    /// <summary>The saved views of a record type as list tabs. A list still works without them, so a failure gives none.</summary>
    public static async Task<IReadOnlyList<ListView>> LoadAsync(HttpClient http, string entity, Guid? currentUserId)
    {
        try
        {
            var views = await http.GetFromJsonAsync<List<SavedViewDto>>($"api/v1/views/{entity}");
            return views?.Select(v => ListView.FromSaved(v, currentUserId)).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>Stores a view. Returns null on success, or the reason it could not be saved.</summary>
    public static async Task<string?> SaveAsync(HttpClient http, string entity, SaveViewRequest view, string failure)
    {
        var response = await http.PostAsJsonAsync("api/v1/views",
            new SaveSavedViewRequest(entity, view.Name, view.IsShared, false, view.Filter, view.Columns, view.SortField, view.SortDir));
        return response.IsSuccessStatusCode ? null : await ProblemText.FromAsync(response, failure);
    }

    /// <summary>Deletes a view. Returns null on success, or the reason it could not be deleted.</summary>
    public static async Task<string?> DeleteAsync(HttpClient http, Guid id, string failure)
    {
        var response = await http.DeleteAsync($"api/v1/views/{id}");
        return response.IsSuccessStatusCode ? null : await ProblemText.FromAsync(response, failure);
    }
}
