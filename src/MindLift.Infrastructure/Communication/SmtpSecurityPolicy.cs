using System.Net;
using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Automation;

namespace MindLift.Infrastructure.Communication;

public static class SmtpSecurityPolicy
{
    private static readonly HashSet<int> AllowedPorts = [25, 465, 587, 2525];

    public static async Task ValidateDestinationAsync(string? host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        await ResolveAndValidateDestinationAsync(host, 587, ct);
    }

    public static async Task<IPAddress> ResolveAndValidateDestinationAsync(string host, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["host"] = ["SMTP host is required."] });
        }

        if (!AllowedPorts.Contains(port))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["port"] = [$"Port {port} is not an allowed SMTP port. Use 25, 465, 587, or 2525."] });
        }

        host = host.Trim();
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["host"] = [$"Failed to resolve host '{host}': {ex.Message}"] });
        }

        if (addresses.Length == 0)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["host"] = ["Could not resolve SMTP host."] });
        }

        foreach (var ip in addresses)
        {
            if (WebhookSecurity.IsBlockedAddress(ip))
            {
                throw new ValidationException(new Dictionary<string, string[]> { ["host"] = ["Connections to internal, private, loopback, or cloud-metadata network destinations are prohibited."] });
            }
        }

        return addresses[0];
    }

    public static async Task<SmtpClient> ConnectWithPinnedDestinationAsync(string host, int port, SecureSocketOptions security, CancellationToken ct = default)
    {
        var pinnedIp = await ResolveAndValidateDestinationAsync(host, port, ct);
        var socket = new Socket(pinnedIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(pinnedIp, port), ct);
            var client = new SmtpClient();
            await client.ConnectAsync(socket, host, port, security, ct);
            return client;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static SecureSocketOptions DetermineSecurity(int port, bool useStartTls, bool hasCredentials)
    {
        if (port == 465)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        if (useStartTls || hasCredentials)
        {
            return SecureSocketOptions.StartTls;
        }

        return SecureSocketOptions.None;
    }

    public static void AssertTransportSecurity(SecureSocketOptions security, bool hasCredentials)
    {
        if (hasCredentials && security == SecureSocketOptions.None)
        {
            throw new InvalidOperationException("Transport encryption (STARTTLS or SSL/TLS) is mandatory when authenticating with SMTP credentials.");
        }
    }
}
