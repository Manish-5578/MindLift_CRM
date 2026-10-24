using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MindLift.Shared.Contracts;

namespace MindLift.Web.Services;

/// <summary>
/// Adds the bearer token to every API call. On a 401 it refreshes the session once (cookie based) and retries.
/// If the refresh fails, the session is cleared so the app sends the user to sign-in.
/// </summary>
public sealed class AuthHandler(AuthSession session, Uri baseAddress) : DelegatingHandler(new HttpClientHandler())
{
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (session.AccessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.RequestUri?.AbsolutePath.Contains("/auth/", StringComparison.Ordinal) == true)
        {
            return response;
        }

        if (session.IsSupport)
        {
            session.Clear();   // the support session ended; it is never turned into another session
            return response;
        }

        if (!await RefreshAsync(cancellationToken))
        {
            return response;
        }

        var retry = await CloneAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken!);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        await RefreshGate.WaitAsync(cancellationToken);
        try
        {
            using var client = new HttpClient(new HttpClientHandler()) { BaseAddress = baseAddress };
            var response = await client.PostAsJsonAsync("api/v1/auth/refresh", new RefreshRequest(null), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                session.Clear();
                return false;
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            session.SetTokens(tokens!);
            return true;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
