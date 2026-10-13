using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Portal;

/// <summary>Portal sessions: refresh rotation, reuse detection, logout, cookie delivery, the on/off switch and the profile.</summary>
public sealed class PortalSessionTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Invited(Session Admin, string Slug, Guid ContactId, string Email);

    private async Task<Invited> InviteAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var me = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        string slug;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == me!.TenantId).Select(t => t.Slug).SingleAsync();
        }

        var email = $"session-{Guid.NewGuid():N}@example.test";
        var created = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Mona", "Synthetic", email, "+971500000001", null, null, null, "client", null, null));
        var contact = (await created.Content.ReadFromJsonAsync<ContactDto>())!;
        (await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite", new InvitePortalUserRequest(contact.Id, email)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return new Invited(admin, slug, contact.Id, email);
    }

    private static HttpClient Mobile(MindLiftApiFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Client", "mobile");
        return c;
    }

    private async Task<PortalLoginResponse> AcceptAsync(Invited inv, HttpClient client, string password = "Customer!Pass1")
    {
        var resp = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(inv.Email), password));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<PortalLoginResponse>())!;
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_a_reused_token_ends_every_session()
    {
        var inv = await InviteAsync();
        var client = Mobile(factory);
        var first = await AcceptAsync(inv, client);
        first.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        first.ExpiresAtUtc.ShouldNotBeNull();

        var r1 = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/refresh", new PortalRefreshRequest(first.RefreshToken));
        r1.StatusCode.ShouldBe(HttpStatusCode.OK, await r1.Content.ReadAsStringAsync());
        var second = (await r1.Content.ReadFromJsonAsync<PortalLoginResponse>())!;
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // Re-using the first (rotated) token is treated as theft: it fails and the newer token stops working too.
        var reuse = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/refresh", new PortalRefreshRequest(first.RefreshToken));
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Workspaces.ErrorCodeAsync(reuse)).ShouldBe("refresh_token_reused");

        var afterReuse = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/refresh", new PortalRefreshRequest(second.RefreshToken));
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var inv = await InviteAsync();
        var client = Mobile(factory);
        var login = await AcceptAsync(inv, client);

        (await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/logout", new PortalRefreshRequest(login.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/refresh", new PortalRefreshRequest(login.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Browser_clients_get_an_http_only_cookie_and_no_token_in_the_body()
    {
        var inv = await InviteAsync();
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(inv.Email), "Customer!Pass1"));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = (await resp.Content.ReadFromJsonAsync<PortalLoginResponse>())!;
        body.RefreshToken.ShouldBeNull();

        resp.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        var cookie = cookies!.Single(c => c.StartsWith("ml_portal_refresh=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain($"path=/api/v1/portal/{inv.Slug}/auth", Case.Insensitive);
    }

    [Fact]
    public async Task Short_password_is_rejected_on_accept()
    {
        var inv = await InviteAsync();
        var resp = await factory.CreateClient().PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(inv.Email), "short"));
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Switching_the_portal_off_blocks_sign_in_and_refresh()
    {
        var inv = await InviteAsync();
        var client = Mobile(factory);
        var login = await AcceptAsync(inv, client);

        (await inv.Admin.Client.PutAsJsonAsync("/api/v1/settings/portal", new SavePortalSettingsRequest(false, null, null, true, true, true, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var signIn = await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/login", new PortalLoginRequest(inv.Email, "Customer!Pass1"));
        signIn.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Workspaces.ErrorCodeAsync(signIn)).ShouldBe("portal_disabled");

        (await client.PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/refresh", new PortalRefreshRequest(login.RefreshToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_can_read_and_update_their_profile_and_change_password()
    {
        var inv = await InviteAsync();
        var login = await AcceptAsync(inv, Mobile(factory));
        var portal = factory.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var profile = await portal.GetFromJsonAsync<PortalProfileDto>("/api/v1/portal/me");
        profile!.Email.ShouldBe(inv.Email);
        profile.FirstName.ShouldBe("Mona");

        var updated = await portal.PutAsJsonAsync("/api/v1/portal/me", new UpdatePortalProfileRequest("ar"));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<PortalProfileDto>())!.Language.ShouldBe("ar");
        (await portal.PutAsJsonAsync("/api/v1/portal/me", new UpdatePortalProfileRequest("fr"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await portal.PostAsJsonAsync("/api/v1/portal/me/password", new ChangePortalPasswordRequest("wrong-password", "NewCustomer!2")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await portal.PostAsJsonAsync("/api/v1/portal/me/password", new ChangePortalPasswordRequest("Customer!Pass1", "NewCustomer!2")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await factory.CreateClient().PostAsJsonAsync($"/api/v1/portal/{inv.Slug}/auth/login", new PortalLoginRequest(inv.Email, "NewCustomer!2")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A staff token cannot read a portal profile.
        (await inv.Admin.Client.GetAsync("/api/v1/portal/me")).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Portal_Serves_Host_Page_At_Portal_Path()
    {
        var client = factory.CreateClient();
        var res = await client.GetAsync("/portal");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await res.Content.ReadAsStringAsync();
        html.ShouldContain("Customer Portal");
        html.ShouldContain("css/portal.css");
    }
}
