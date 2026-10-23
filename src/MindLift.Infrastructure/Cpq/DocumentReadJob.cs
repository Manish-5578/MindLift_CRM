using Hangfire;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;

namespace MindLift.Infrastructure.Cpq;

/// <summary>
/// Reads one document in the background. Reading with AI can take a minute or more on an ordinary computer, so an upload only queues the document
/// and this job does the reading. The document is never left Queued or Processing: it ends as NeedsReview or Failed, with a note saying why.
/// </summary>
public sealed class DocumentReadJob(IDocumentOcrService documents, ITenantContext tenant, IBackgroundJobClient jobs, ILogger<DocumentReadJob> logger)
{
    /// <summary>How often a document waits for a free place to be read before it is given up on, 30 seconds each time (20 minutes in all).</summary>
    private const int MaxWaits = 40;

    // A retry would read the document a second time while the first reading may still be writing; the job reschedules itself when it needs to wait.
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(Guid tenantId, Guid documentId, int waited, CancellationToken cancellationToken)
    {
        using (tenant.Use(tenantId))
        {
            try
            {
                await documents.ProcessOcrAsync(documentId, cancellationToken);
            }
            catch (BusinessRuleException ex) when (ex.Code == "ocr_busy")
            {
                // Other documents are being read. This one goes back to waiting and is tried again shortly.
                if (waited < MaxWaits)
                {
                    jobs.Schedule<DocumentReadJob>(job => job.RunAsync(tenantId, documentId, waited + 1, CancellationToken.None), TimeSpan.FromSeconds(30));
                }
                else
                {
                    await documents.MarkReadFailedAsync(documentId, "The document reader stayed busy for 20 minutes. Use Reprocess to try again.", cancellationToken);
                }
            }
            catch (NotFoundException)
            {
                // Deleted while it waited.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reading document {DocumentId} stopped unexpectedly.", documentId);
                await documents.MarkReadFailedAsync(documentId, "Reading stopped unexpectedly. Use Reprocess to try again.", cancellationToken);
            }
        }
    }
}
