using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

/// <summary>The Hangfire dashboard is off unless it is switched on, so job details are not published by default.</summary>
public sealed class HangfireDashboardTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task The_dashboard_is_not_served_by_default()
    {
        var client = factory.CreateClient();

        // The web app's fallback answers unknown paths with its own page, so check that the Hangfire page is not what comes back.
        var body = await client.GetStringAsync("/hangfire");

        body.ShouldNotContain("Hangfire Dashboard");
    }
}
