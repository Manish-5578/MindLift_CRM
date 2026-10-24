using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Persistence;

/// <summary>
/// Removes record-change history older than the retention period (Audit:RetentionDays, two years unless set), a few thousand rows at a time,
/// so the history does not grow without end. The platform owner's own audit trail is not touched.
/// </summary>
public sealed class AuditRetentionJob(AppDbContext db, IConfiguration configuration, IClock clock, ILogger<AuditRetentionJob> logger)
{
    public const int DefaultRetentionDays = 730;
    private const int Batch = 5000;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var days = configuration.GetValue<int?>("Audit:RetentionDays") ?? DefaultRetentionDays;
        if (days <= 0)
        {
            return 0;   // 0 or less keeps everything
        }

        var cutoff = clock.UtcNow.AddDays(-days);
        var total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var ids = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.OccurredAt < cutoff).OrderBy(a => a.OccurredAt).Select(a => a.Id).Take(Batch).ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                break;
            }

            total += await db.AuditLogs.IgnoreQueryFilters().Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        if (total > 0)
        {
            logger.LogInformation("Removed {Count} audit rows older than {Days} days", total, days);
        }

        return total;
    }
}
