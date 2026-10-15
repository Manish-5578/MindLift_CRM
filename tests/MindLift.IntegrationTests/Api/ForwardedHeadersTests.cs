using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Api.Hosting;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

/// <summary>Behind a proxy the app can read the real client address, but only from a proxy it has been told to trust.</summary>
public sealed class ForwardedHeadersTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    /// <summary>Calls a one-line app whose connection comes from <paramref name="remote"/>, and returns the address and scheme it ends up seeing.</summary>
    private static async Task<string> SeenAsync(IConfiguration configuration, string remote, string? forwardedFor, string? forwardedProto)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ForwardedHeadersOptions>(options => ForwardedHeadersSetup.Configure(options, configuration));
        var app = builder.Build();
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
            return next();
        });
        app.UseForwardedHeaders();
        app.MapGet("/seen", context => context.Response.WriteAsync($"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}"));
        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/seen");
            if (forwardedFor is not null)
            {
                request.Headers.Add("X-Forwarded-For", forwardedFor);
            }

            if (forwardedProto is not null)
            {
                request.Headers.Add("X-Forwarded-Proto", forwardedProto);
            }

            return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void It_is_off_unless_it_is_switched_on()
    {
        ForwardedHeadersSetup.IsEnabled(Config()).ShouldBeFalse();
        ForwardedHeadersSetup.IsEnabled(Config(("ForwardedHeaders:Enabled", "false"))).ShouldBeFalse();
        ForwardedHeadersSetup.IsEnabled(Config(("ForwardedHeaders:Enabled", "true"))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_named_proxy_is_trusted_and_the_real_client_address_and_scheme_are_used()
    {
        var config = Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"));

        (await SeenAsync(config, "10.0.0.5", "203.0.113.9", "https")).ShouldBe("203.0.113.9|https");
    }

    [Fact]
    public async Task Headers_from_a_caller_that_is_not_a_named_proxy_are_ignored()
    {
        var config = Config(("ForwardedHeaders:KnownProxies:0", "10.0.0.5"));

        // The caller is not the proxy, so the address it claims is not believed and it cannot pick its own address.
        (await SeenAsync(config, "198.51.100.7", "203.0.113.9", "https")).ShouldBe("198.51.100.7|http");
    }

    [Fact]
    public async Task A_named_network_is_trusted()
    {
        var config = Config(("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        (await SeenAsync(config, "10.1.2.3", "203.0.113.9", null)).ShouldBe("203.0.113.9|http");
        (await SeenAsync(config, "192.168.1.1", "203.0.113.9", null)).ShouldBe("192.168.1.1|http");
    }

    [Fact]
    public async Task With_nothing_named_only_a_proxy_on_the_same_machine_is_trusted()
    {
        var config = Config();

        (await SeenAsync(config, "127.0.0.1", "203.0.113.9", "https")).ShouldBe("203.0.113.9|https");
        (await SeenAsync(config, "10.0.0.5", "203.0.113.9", "https")).ShouldBe("10.0.0.5|http");
    }

    [Fact]
    public void A_proxy_or_range_that_is_not_an_address_stops_the_app_with_a_clear_message()
    {
        Should.Throw<InvalidOperationException>(() => ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), Config(("ForwardedHeaders:KnownProxies:0", "not-an-address"))))
            .Message.ShouldContain("not-an-address");
        Should.Throw<InvalidOperationException>(() => ForwardedHeadersSetup.Configure(new ForwardedHeadersOptions(), Config(("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99"))))
            .Message.ShouldContain("10.0.0.0/99");
    }
}
