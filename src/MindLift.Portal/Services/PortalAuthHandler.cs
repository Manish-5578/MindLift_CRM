using System.Net;
using System.Net.Http.Headers;

namespace MindLift.Portal.Services;

/// <summary>Adds the customer's access token, refreshes it shortly before it expires, and retries a body-less request once after a 401.</summary>
public sealed class PortalAuthHandler(PortalSession session) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (session.IsSignedIn && session.ExpiresSoon)
        {
            await session.TryRefreshAsync();
        }

        Attach(request);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && request.Content is null && await session.TryRefreshAsync())
        {
            response.Dispose();
            var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            Attach(retry);
            response = await base.SendAsync(retry, cancellationToken);
        }

        return response;
    }

    private void Attach(HttpRequestMessage request)
    {
        if (session.AccessToken is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
