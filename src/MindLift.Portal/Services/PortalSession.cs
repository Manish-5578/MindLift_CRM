using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using MindLift.Shared.Contracts;

namespace MindLift.Portal.Services;

/// <summary>
/// The signed-in customer for one company (slug). The access token lives in memory and sessionStorage;
/// the refresh token is an HttpOnly cookie the browser sends to /api/v1/portal/{slug}/auth.
/// </summary>
public sealed class PortalSession(NavigationManager nav, IJSRuntime? js = null)
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
        var slugChanged = !string.Equals(Slug, slug, StringComparison.OrdinalIgnoreCase);
        if (slugChanged || Branding is null)
        {
            Slug = slug;
            try
            {
                Branding = await Raw.GetFromJsonAsync<PortalBrandingDto>($"api/v1/portal/{Uri.EscapeDataString(slug)}/branding");
            }
            catch (HttpRequestException)
            {
                Branding = null;
            }
        }

        // 1. Instant tab reload restoration from sessionStorage if User is not yet in memory
        if (User is null && !string.IsNullOrWhiteSpace(slug) && js is not null)
        {
            try
            {
                var storedJson = await js.InvokeAsync<string?>("sessionStorage.getItem", $"portal_{slug}_user");
                if (!string.IsNullOrWhiteSpace(storedJson))
                {
                    var cached = System.Text.Json.JsonSerializer.Deserialize<PortalLoginResponse>(storedJson);
                    if (cached is not null && cached.ExpiresAtUtc > DateTime.UtcNow)
                    {
                        User = cached;
                        Changed?.Invoke();
                    }
                }
            }
            catch
            {
            }
        }

        // 2. Cookie-based refresh if branding exists and we either need to restore or rotate
        if (Branding is not null && (!IsSignedIn || ExpiresSoon))
        {
            await TryRefreshAsync();
        }

        Changed?.Invoke();
    }

    public void SignIn(PortalLoginResponse login)
    {
        User = login;
        if (!string.IsNullOrWhiteSpace(Slug) && js is not null)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(login);
                _ = js.InvokeVoidAsync("sessionStorage.setItem", $"portal_{Slug}_user", json);
            }
            catch
            {
            }
        }
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
            var req = new HttpRequestMessage(HttpMethod.Post, $"api/v1/portal/{Uri.EscapeDataString(Slug)}/auth/refresh")
            {
                Content = JsonContent.Create(new PortalRefreshRequest(null))
            };
            req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
            var resp = await Raw.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                User = await resp.Content.ReadFromJsonAsync<PortalLoginResponse>();
                if (User is not null)
                {
                    if (js is not null)
                    {
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(User);
                            await js.InvokeVoidAsync("sessionStorage.setItem", $"portal_{Slug}_user", json);
                        }
                        catch
                        {
                        }
                    }
                    return true;
                }
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                if (js is not null)
                {
                    try
                    {
                        await js.InvokeVoidAsync("sessionStorage.removeItem", $"portal_{Slug}_user");
                    }
                    catch
                    {
                    }
                }
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
            if (js is not null)
            {
                try
                {
                    await js.InvokeVoidAsync("sessionStorage.removeItem", $"portal_{Slug}_user");
                }
                catch
                {
                }
            }
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"api/v1/portal/{Uri.EscapeDataString(Slug)}/auth/logout")
                {
                    Content = JsonContent.Create(new PortalRefreshRequest(null))
                };
                req.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                await Raw.SendAsync(req);
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
