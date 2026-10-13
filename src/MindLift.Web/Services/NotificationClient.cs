using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using MindLift.Shared.Contracts;

namespace MindLift.Web.Services;

/// <summary>
/// Keeps the unread count and listens for new notifications over SignalR (plan 10.2). If the connection cannot be made the app still works:
/// the count is read from the API and the list refreshes whenever it is opened.
/// </summary>
public sealed class NotificationClient(HttpClient http, AuthSession session) : IAsyncDisposable
{
    private HubConnection? _hub;

    public int Unread { get; private set; }

    /// <summary>A notification just arrived. Raised on a background thread, so handlers must marshal to the UI themselves.</summary>
    public event Action<NotificationDto>? Received;

    /// <summary>The unread count changed.</summary>
    public event Action? Changed;

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async Task StartAsync()
    {
        if (_hub is not null || !session.IsSignedIn)
        {
            return;
        }

        await RefreshCountAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(http.BaseAddress!, "hubs/notifications"), options => options.AccessTokenProvider = TokenAsync)
            .WithAutomaticReconnect()
            .Build();

        _hub.On<NotificationDto, int>("ReceiveNotification", (notification, unread) =>
        {
            Unread = unread;
            Received?.Invoke(notification);
            Changed?.Invoke();
        });

        // Anything that arrived while the connection was down is picked up by reading the count again.
        _hub.Reconnected += async _ =>
        {
            await RefreshCountAsync();
            Changed?.Invoke();
        };
        _hub.Reconnecting += _ =>
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        };
        _hub.Closed += _ =>
        {
            Changed?.Invoke();
            return Task.CompletedTask;
        };

        try
        {
            await _hub.StartAsync();
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // The count stays correct from the API; the next page load tries again.
        }
    }

    public async Task StopAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }

        Unread = 0;
        Changed?.Invoke();
    }

    public async Task RefreshCountAsync()
    {
        try
        {
            Unread = (await http.GetFromJsonAsync<UnreadCountDto>("api/v1/notifications/unread-count"))?.Unread ?? Unread;
            Changed?.Invoke();
        }
        catch (HttpRequestException)
        {
            // Keep the count we have.
        }
    }

    public void SetUnread(int unread)
    {
        Unread = unread;
        Changed?.Invoke();
    }

    /// <summary>The token for the connection. When it has expired, an ordinary API call makes the app refresh it first.</summary>
    private async Task<string?> TokenAsync()
    {
        if (session.ExpiresAtUtc is { } expires && expires <= DateTime.UtcNow.AddSeconds(30))
        {
            await RefreshCountAsync();
        }

        return session.AccessToken;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
