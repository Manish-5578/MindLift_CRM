using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MindLift.Api.Hosting;

/// <summary>
/// Lets the app see the real client address and scheme when it sits behind a reverse proxy or load balancer. It is off unless
/// <c>ForwardedHeaders:Enabled</c> is true, because trusting these headers from anyone would let a caller pick their own address and dodge the rate limits.
/// Only the proxies named in <c>ForwardedHeaders:KnownProxies</c> (addresses) and <c>ForwardedHeaders:KnownNetworks</c> (for example 10.0.0.0/8) are trusted;
/// with none named, only a proxy on the same machine is.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>("ForwardedHeaders:Enabled");

    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;

        if (configuration.GetValue<bool>("ForwardedHeaders:TrustAllProxies"))
        {
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            return;
        }

        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
        {
            return; // Keep the built-in default: a proxy on this machine.
        }

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies has \"{proxy}\", which is not an IP address.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in networks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks has \"{network}\", which is not an address range such as 10.0.0.0/8.");
            }

            options.KnownIPNetworks.Add(parsed);
        }
    }
}
