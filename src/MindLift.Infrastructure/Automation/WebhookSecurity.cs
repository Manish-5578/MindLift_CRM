using System.Net;
using System.Net.Sockets;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// SSRF protection for outbound automation webhooks.
/// Blocks private ranges, loopback, link-local metadata addresses, and invalid schemes.
/// </summary>
public static class WebhookSecurity
{
    public static bool IsSafeUrl(string url, out string? reason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "Invalid URL format.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            reason = "Only HTTP and HTTPS protocols are allowed.";
            return false;
        }

        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = "Localhost and loopback URLs are blocked.";
            return false;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            if (addresses.Length == 0)
            {
                reason = "Could not resolve hostname.";
                return false;
            }

            foreach (var ip in addresses)
            {
                if (IPAddress.IsLoopback(ip) || IsPrivateOrRestricted(ip))
                {
                    reason = "Internal, private and loopback IP addresses are blocked.";
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            reason = $"DNS resolution failed: {ex.Message}";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool IsPrivateOrRestricted(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
        {
            return true;
        }

        // Map IPv4-mapped IPv6 addresses to IPv4
        var targetIp = ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
        if (targetIp.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = targetIp.GetAddressBytes();

            // 0.0.0.0/8 (current network)
            if (bytes[0] == 0) return true;

            // 10.0.0.0/8 (private)
            if (bytes[0] == 10) return true;

            // 127.0.0.0/8 (loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (link local / AWS/Azure metadata 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (private 172.16.x.x - 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.168.0.0/16 (private)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 224.0.0.0/4 (multicast)
            if (bytes[0] >= 224) return true;
        }

        return false;
    }
}
