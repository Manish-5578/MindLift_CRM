using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Cpq;

/// <summary>
/// Hangfire recurring job that scans for approved documents nearing their expiry dates (e.g. 60, 30, 7 days)
/// and triggers expiry notifications to owners/admins and portal customers.
/// </summary>
public sealed class DocumentExpiryJob(IDocumentOcrService ocrService, ILogger<DocumentExpiryJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting scheduled document expiry check...");
        var alerts = await ocrService.RunExpiryChecksAsync(ct);
        logger.LogInformation("Completed scheduled document expiry check. {Count} alert notifications dispatched.", alerts);
    }
}
