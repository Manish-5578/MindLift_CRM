using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using MindLift.Shared.Contracts;

namespace MindLift.Portal.Services;

/// <summary>
/// The signed-in customer for one company (slug). The access token lives in memory only;
/// the refresh token is an HttpOnly cookie the browser sends to /api/v1/portal/{slug}/auth.
/// </summary>
public sealed class PortalSession(NavigationManager nav)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private HttpClient? _raw;

    public string? Slug { get; private set; }
    public PortalLoginResponse? User { get; private set; }
    public PortalBrandingDto? Branding { get; private set; }
    public bool IsSignedIn => User is not null && !string.IsNullOrEmpty(User.AccessToken);
    public string? AccessToken => User?.AccessToken;

    public event Action? Changed;

    /// <summary>An HttpClient without the auth handler, for sign-in calls (no recursion through the handler).</summary>
    private HttpClient Raw => _raw ??= new HttpClient { BaseAddress = new Uri(new Uri(nav.BaseUri), "/") };

    /// <summary>Loads branding for a company the first time its slug is seen, and tries to resume a session from the cookie.</summary>
    public async Task EnsureCompanyAsync(string slug)
    {
        if (string.Equals(Slug, slug, StringComparison.OrdinalIgnoreCase) && Branding is not null)
        {
            return;
        }

        Slug = slug;
        User = null;
        try
        {
            Branding = await Raw.GetFromJsonAsync<PortalBrandingDto>($"api/v1/portal/{Uri.EscapeDataString(slug)}/branding");
        }
        catch (HttpRequestException)
        {
            Branding = null;
        }

        if (Branding is not null)
        {
            await TryRefreshAsync();
        }

        Changed?.Invoke();
    }

    public void SignIn(PortalLoginResponse login)
    {
        User = login;
        Changed?.Invoke();
    }

    public bool ExpiresSoon => User?.ExpiresAtUtc is { } exp && exp <= DateTime.UtcNow.AddSeconds(60);

    /// <summary>Uses the refresh cookie to get a new access token. Returns false when the session has ended.</summary>
    public async Task<bool> TryRefreshAsync()
    {
        if (Slug is null) return false;
        await _refreshLock.WaitAsync();
        try
        {
            var resp = await Raw.PostAsJsonAsync($"api/v1/portal/{Uri.EscapeDataString(Slug)}/auth/refresh", new PortalRefreshRequest(null));
            if (resp.IsSuccessStatusCode)
            {
                User = await resp.Content.ReadFromJsonAsync<PortalLoginResponse>();
                return User is not null;
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                User = null;
            }

            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task SignOutAsync()
    {
        if (Slug is not null)
        {
            try
            {
                await Raw.PostAsJsonAsync($"api/v1/portal/{Uri.EscapeDataString(Slug)}/auth/logout", new PortalRefreshRequest(null));
            }
            catch (HttpRequestException)
            {
                // Signing out locally still matters when the network is down.
            }
        }

        User = null;
        Changed?.Invoke();
    }

    /// <summary>Raw client for anonymous auth calls (login, magic link, accept invite).</summary>
    public HttpClient Anonymous => Raw;
}
