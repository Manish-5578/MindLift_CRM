using System.Security.Cryptography;
using Hangfire;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Communication;

namespace MindLift.Infrastructure.Portal;

/// <summary>
/// Sends a portal sign-in or invitation email in the background, so a request for a link answers as quickly for an address with portal access as for one
/// without. The body holds a live link, so it is stored in the job protected and only opened here.
/// </summary>
public sealed class PortalEmailJob(IEmailSender email, ISecretProtector secretProtector, ILogger<PortalEmailJob> logger)
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [1, 2, 5])]
    public async Task SendAsync(string to, string subject, string protectedBody, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = secretProtector.Unprotect(protectedBody);
        }
        catch (CryptographicException exception)
        {
            // The protection key changed or the job was altered. Retrying will not help.
            logger.LogError(exception, "A portal email for {Recipient} could not be opened and was not sent.", to);
            return;
        }

        await email.SendAsync(to, subject, body, cancellationToken);
    }
}
