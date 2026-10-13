using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Support;

/// <summary>A signed-in user and an HTTP client that sends their access token.</summary>
public sealed record Session(string Email, string Password, string AccessToken, string RefreshToken, HttpClient Client);

/// <summary>Helpers that drive the real API: sign-up, sign-in, invitations and role changes.</summary>
public static class Workspaces
{
    public const string Password = "Str0ngPass";

    /// <summary>Creates a new company with an Admin owner. Uses the mobile client mode so the refresh token is in the body.</summary>
    public static async Task<Session> SignupAsync(MindLiftApiFactory factory, string? company = null)
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var client = MobileClient(factory);
        var response = await client.PostAsJsonAsync("/api/v1/auth/signup",
            new SignupRequest(company ?? $"Company {Guid.NewGuid():N}", "Sara", "Khan", email, Password, "+971500000000", "AE", "en", true));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return WithToken(factory, email, tokens!);
    }

    public static async Task<Session> LoginAsync(MindLiftApiFactory factory, string email, string password)
    {
        var response = await MobileClient(factory).PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return WithToken(factory, email, (await response.Content.ReadFromJsonAsync<TokenResponse>())!);
    }

    /// <summary>Invites a person with a role, accepts the invitation from the emailed link, and returns their session.</summary>
    public static async Task<Session> InviteAndAcceptAsync(MindLiftApiFactory factory, Session admin, string roleName, string emailPrefix = "member")
    {
        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var roleId = roles!.Single(r => r.Name == roleName).Id;

        var email = $"{emailPrefix}-{Guid.NewGuid():N}@example.test";
        var invite = await admin.Client.PostAsJsonAsync("/api/v1/invitations",
            new CreateInvitationRequest(email, "Ali", "Hassan", roleId, null));
        invite.StatusCode.ShouldBe(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());

        var token = factory.Emails.LatestTokenFor(email);
        var accept = await MobileClient(factory).PostAsJsonAsync("/api/v1/auth/accept-invite",
            new AcceptInvitationRequest(token, Password));
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return WithToken(factory, email, (await accept.Content.ReadFromJsonAsync<TokenResponse>())!);
    }

    public static HttpClient MobileClient(MindLiftApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client", "mobile");
        return client;
    }

    private static Session WithToken(MindLiftApiFactory factory, string email, TokenResponse tokens)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return new Session(email, Password, tokens.AccessToken, tokens.RefreshToken ?? string.Empty, client);
    }

    /// <summary>The error code of an RFC 7807 response (the "code" extension).</summary>
    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
