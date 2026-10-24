using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>The platform owner opens a read-only, time-limited, recorded support view of a company, without its password.</summary>
public sealed class SupportAccessTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private async Task<Session> PlatformAsync()
    {
        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync(email, Workspaces.Password, "Priya", "Owner");
        }

        return await Workspaces.LoginAsync(factory, email, Workspaces.Password);
    }

    private HttpClient As(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_support_view_sees_the_company_as_its_owner_cannot_change_anything_and_is_recorded()
    {
        var platform = await PlatformAsync();
        var company = await Workspaces.SignupAsync(factory);
        (await company.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Seen", "BySupport", null, null, null, null, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;

        // A reason is required.
        (await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/support-sessions", new StartSupportSessionRequest("hi"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var started = (await (await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/support-sessions", new StartSupportSessionRequest("Customer says the lead list is empty")))
            .Content.ReadFromJsonAsync<SupportSessionStartedDto>())!;

        var anonymous = factory.CreateClient();
        var tokens = (await (await anonymous.PostAsJsonAsync("/api/v1/support/exchange", new SupportExchangeRequest(started.Code))).Content.ReadFromJsonAsync<TokenResponse>())!;
        tokens.RefreshToken.ShouldBeNull();
        (await anonymous.PostAsJsonAsync("/api/v1/support/exchange", new SupportExchangeRequest(started.Code))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // once only

        var support = As(tokens.AccessToken);
        var leads = await support.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(null, null, null, null));
        leads.StatusCode.ShouldBe(HttpStatusCode.OK);   // lists are read with a POST to /query, which changes nothing
        (await leads.Content.ReadAsStringAsync()).ShouldContain("BySupport");
        (await support.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Sneaky", "Write", null, null, null, null, null, null, null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await support.PostAsync("/api/v1/support/end", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await support.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var sessions = (await platform.Client.GetFromJsonAsync<List<SupportSessionDto>>($"/api/v1/platform/companies/{id}/support-sessions"))!;
        var session = sessions.Single();
        session.Reason.ShouldBe("Customer says the lead list is empty");
        session.EndedAt.ShouldNotBeNull();
        session.PagesViewed.ShouldContain("/api/v1/leads/query");

        var trail = (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}"))!;
        trail.Items.ShouldContain(a => a.Action == "support.started" && a.Summary.Contains("lead list is empty"));
        trail.Items.ShouldContain(a => a.Action == "support.ended");
    }

    [Fact]
    public async Task Only_the_platform_owner_can_open_a_support_view_and_a_made_up_code_does_nothing()
    {
        var company = await Workspaces.SignupAsync(factory);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        (await company.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/support-sessions", new StartSupportSessionRequest("Let me look around"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await factory.CreateClient().PostAsJsonAsync("/api/v1/support/exchange", new SupportExchangeRequest("0123456789abcdef"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
