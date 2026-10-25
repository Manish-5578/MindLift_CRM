using System.Net;
using MailKit.Security;

namespace MindLift.Infrastructure.Messaging;

/// <summary>
/// One rule for how every mail connection is secured: port 465 is encrypted from the start, the configured STARTTLS setting is honoured, and a server
/// that is not on this machine is never used without encryption unless <c>Smtp:AllowInsecure</c> is set.
/// </summary>
public static class SmtpSecurity
{
    public static SecureSocketOptions Choose(string host, int port, SmtpOptions options)
    {
        if (port == 465) return SecureSocketOptions.SslOnConnect;
        if (options.UseStartTls) return SecureSocketOptions.StartTls;
        if (options.AllowInsecure || IsThisMachine(host)) return SecureSocketOptions.Auto;
        return SecureSocketOptions.StartTls;
    }

    private static bool IsThisMachine(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
}
