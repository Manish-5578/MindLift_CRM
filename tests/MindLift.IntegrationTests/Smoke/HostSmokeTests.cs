using System.Net;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Smoke;

/// <summary>
/// M0/M1 smoke tests: the API host serves the staff app, the OpenAPI document, health checks and the SPA fallback.
/// Runs against the real API and a PostgreSQL container with migrations applied.
/// </summary>
public sealed class HostSmokeTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Root_serves_the_blazor_host_page()
    {
        var response = await _client.GetAsync("/");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("<div id=\"app\">");
    }

    [Fact]
    public async Task OpenApi_document_is_available_in_development()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_check_reports_the_database_as_healthy()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_and_not_the_spa_page()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("<div id=\"app\">");
    }

    [Fact]
    public async Task Unknown_hub_route_returns_404_and_not_the_spa_page()
    {
        var response = await _client.GetAsync("/hubs/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("<div id=\"app\">");
    }
}
