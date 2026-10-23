using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;
using MindLift.Shared.Contracts;

namespace MindLift.Web.Services;

/// <summary>
/// The platform owner's notifications: new companies and companies that need a look. What has been read is remembered in this browser,
/// and a problem that is still there on a new day is raised again (its id carries the day).
/// </summary>
public sealed class PlatformNotifier(HttpClient http, IJSRuntime js)
{
    private const string StorageKey = "pc.read";
    private HashSet<string> _read = [];
    private bool _loadedRead;

    public IReadOnlyList<PlatformNotificationDto> Items { get; private set; } = [];

    public int Unread => Items.Count(i => !_read.Contains(i.Id));

    public event Action? Changed;

    public bool IsRead(string id) => _read.Contains(id);

    public async Task RefreshAsync()
    {
        if (!_loadedRead)
        {
            _loadedRead = true;
            try
            {
                var saved = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
                if (!string.IsNullOrEmpty(saved))
                {
                    _read = JsonSerializer.Deserialize<HashSet<string>>(saved) ?? [];
                }
            }
            catch (Exception ex) when (ex is JSException or JsonException or InvalidOperationException)
            {
                _read = [];
            }
        }

        try
        {
            Items = await http.GetFromJsonAsync<List<PlatformNotificationDto>>("api/v1/platform/notifications") ?? [];
        }
        catch (HttpRequestException)
        {
            // Keep what was shown; the next refresh tries again.
        }

        Changed?.Invoke();
    }

    public Task MarkReadAsync(string id) => Remember(_read.Add(id));

    public Task MarkAllReadAsync()
    {
        var added = false;
        foreach (var item in Items)
        {
            added |= _read.Add(item.Id);
        }

        return Remember(added);
    }

    private async Task Remember(bool changed)
    {
        if (!changed)
        {
            return;
        }

        // Only ids still in the list are kept, so what is stored never grows without end.
        _read.IntersectWith(Items.Select(i => i.Id));
        try { await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(_read)); }
        catch (Exception ex) when (ex is JSException or InvalidOperationException) { }
        Changed?.Invoke();
    }
}
