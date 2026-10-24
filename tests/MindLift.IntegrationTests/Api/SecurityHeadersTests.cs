using System.Net;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

/// <summary>Plain HTTP is sent to HTTPS (health checks excepted), HTTPS answers carry HSTS, and every page carries a Content-Security-Policy that keeps scripts to the app's own files.</summary>
public sealed class SecurityHeadersTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private HttpClient Client(bool requireHttps, string csp = "Enforce")
        => factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Security:RequireHttps", requireHttps.ToString());
            b.UseSetting("Security:CspMode", csp);
        }).CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task Plain_http_is_redirected_to_https_but_health_checks_are_not()
    {
        using var client = Client(requireHttps: true);

        var page = await client.GetAsync("/leads");
        page.StatusCode.ShouldBeOneOf(HttpStatusCode.TemporaryRedirect, HttpStatusCode.PermanentRedirect);
        page.Headers.Location!.ToString().ShouldStartWith("https://");

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_page_carries_a_policy_that_allows_only_the_apps_own_scripts()
    {
        using var client = Client(requireHttps: false);

        var response = await client.GetAsync("/");
        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        policy.ShouldContain("script-src 'self' 'wasm-unsafe-eval'");
        policy.ShouldContain("object-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.Split("style-src")[0].ShouldNotContain("'unsafe-inline'");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
    }

    [Fact]
    public async Task The_policy_can_be_switched_to_report_only_or_off()
    {
        using (var reportOnly = Client(requireHttps: false, "ReportOnly"))
        {
            var response = await reportOnly.GetAsync("/");
            response.Headers.Contains("Content-Security-Policy-Report-Only").ShouldBeTrue();
        }

        using var off = Client(requireHttps: false, "Off");
        var plain = await off.GetAsync("/");
        string.Join(";", plain.Headers.GetValues("Content-Security-Policy")).ShouldBe("frame-ancestors 'none'");
    }
}
