using System.Net;
using MailKit.Security;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Automation;

namespace MindLift.Infrastructure.Communication;

public static class SmtpSecurityPolicy
{
    public static async Task ValidateDestinationAsync(string? host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        host = host.Trim();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
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
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["host"] = [$"Failed to resolve host '{host}': {ex.Message}"] });
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
