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

    /// <summary>True for an address a webhook may never reach (loopback, private ranges, link-local, cloud metadata).</summary>
    public static bool IsBlockedAddress(IPAddress ip) => IPAddress.IsLoopback(ip) || IsPrivateOrRestricted(ip);

    /// <summary>
    /// Connects the way a webhook must: the name is resolved once, every address is checked, and the connection goes to the address that was checked.
    /// The check in <see cref="IsSafeUrl"/> alone is not enough, because the name could resolve to another address a moment later.
    /// </summary>
    public static async ValueTask<Stream> ConnectSafelyAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var safe = addresses.FirstOrDefault(a => !IsBlockedAddress(a))
            ?? throw new HttpRequestException("Webhook blocked: the address is internal, private or loopback.");
        if (addresses.Any(IsBlockedAddress))
        {
            throw new HttpRequestException("Webhook blocked: the name also points to an internal address.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(safe, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPrivateOrRestricted(IPAddress ip)
    {
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
        {
            return true;
        }

        if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.None))
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

            // 100.64.0.0/10 (carrier-grade NAT RFC 6598)
            if (bytes[0] == 100 && (bytes[1] & 0xC0) == 64) return true;

            // 127.0.0.0/8 (loopback)
            if (bytes[0] == 127) return true;

            // 169.254.0.0/16 (link local / AWS/Azure metadata 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254) return true;

            // 172.16.0.0/12 (private 172.16.x.x - 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

            // 192.0.0.0/24 (IETF protocol assignments)
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) return true;

            // 192.168.0.0/16 (private)
            if (bytes[0] == 192 && bytes[1] == 168) return true;

            // 198.18.0.0/15 (benchmark testing)
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) return true;

            // 224.0.0.0/4 (multicast) and 240.0.0.0/4 (reserved for future use)
            if (bytes[0] >= 224) return true;
        }
        else if (targetIp.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = targetIp.GetAddressBytes();

            // Loopback ::1
            if (IPAddress.IsLoopback(targetIp)) return true;

            // Unique Local Address (ULA) fc00::/7 (RFC 4193 - private IPv6)
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // Link-local fe80::/10
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;

            // Discard prefix 100::/64
            if (bytes[0] == 0x01 && bytes[1] == 0x00) return true;

            // Documentation 2001:db8::/32
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return true;

            // 6to4 encapsulation: 2002::/16 contains IPv4 in bytes 2..5
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                var embeddedIpv4 = new IPAddress(new ReadOnlySpan<byte>(bytes, 2, 4));
                if (IsPrivateOrRestricted(embeddedIpv4)) return true;
            }
        }

        return false;
    }
}
