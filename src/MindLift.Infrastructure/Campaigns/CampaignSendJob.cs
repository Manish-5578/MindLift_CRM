using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Messaging;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Campaigns;

/// <summary>
/// Sends a campaign's pending recipients, a batch at a time. When the company's daily limit is reached it stops and the rest wait for tomorrow
/// (the scheduler below starts it again). When nobody is left pending the campaign is finished and whoever made it is told.
/// </summary>
public sealed class CampaignSendJob(AppDbContext db, ITenantContext tenant, IClock clock, IEmailSender email, EmailGuard guard, INotificationService notifications, IConfiguration configuration, ILogger<CampaignSendJob> logger)
{
    private const int Batch = 50;

    public async Task RunAsync(Guid tenantId, Guid campaignId, CancellationToken cancellationToken)
    {
        using var scope = tenant.Use(tenantId);
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
        if (campaign is null || campaign.Status != CampaignStatuses.Sending)
        {
            return;
        }

        var baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        var limitReached = false;
        campaign.PausedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested && !limitReached)
        {
            // A campaign cancelled while it is being sent stops at the next batch.
            if (await db.Campaigns.AsNoTracking().Where(c => c.Id == campaignId).Select(c => c.Status).FirstOrDefaultAsync(cancellationToken) != CampaignStatuses.Sending)
            {
                return;
            }

            var batch = await db.CampaignRecipients.Where(r => r.CampaignId == campaignId && r.Status == "pending").OrderBy(r => r.Email).Take(Batch).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var recipient in batch)
            {
                var (allowed, reason) = await guard.TryReserveAsync(recipient.Email, cancellationToken);
                if (!allowed)
                {
                    // An unsubscribed address is skipped for good; the daily limit only pauses.
                    if (reason?.Contains("daily limit", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        limitReached = true;
                        break;
                    }

                    recipient.Status = "skipped";
                    recipient.Reason = reason;
                    continue;
                }

                try
                {
                    var (text, html) = CampaignService.Compose(campaign, recipient, tracking: true, baseUrl, guard.Footer(recipient.Email));
                    await email.SendHtmlAsync(recipient.Email, CampaignService.Merge(campaign.Subject, recipient), text, html, cancellationToken);
                    recipient.Status = "sent";
                    recipient.SentAt = clock.UtcNow;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    recipient.Status = "failed";
                    recipient.Reason = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                    logger.LogWarning(ex, "Campaign {CampaignId} email to a recipient failed", campaignId);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        if (limitReached)
        {
            campaign.PausedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Campaign {CampaignId} paused: the daily email limit was reached", campaignId);
            return;
        }

        if (!await db.CampaignRecipients.AnyAsync(r => r.CampaignId == campaignId && r.Status == "pending", cancellationToken))
        {
            campaign.Status = CampaignStatuses.Sent;
            campaign.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            if (campaign.CreatedById is { } who)
            {
                var sent = await db.CampaignRecipients.CountAsync(r => r.CampaignId == campaignId && r.Status == "sent", cancellationToken);
                await notifications.NotifyAsync(new NotificationRequest([who], NotificationTypes.BulkCompleted, $"Campaign \"{campaign.Name}\" was sent", $"{sent:N0} emails went out.",
                    "Campaign", campaign.Id, $"/campaigns/{campaign.Id}", IncludeActor: true), cancellationToken);
            }
        }
    }
}

/// <summary>
/// Every few minutes: starts campaigns whose time has come, and picks up campaigns that paused at the daily limit once a new day gives room.
/// Looks across all companies, then does each one's work inside that company.
/// </summary>
public sealed class CampaignScheduleJob(AppDbContext db, ITenantContext tenant, IClock clock, CampaignService campaigns, IBackgroundJobClient jobs)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var due = await db.Campaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == CampaignStatuses.Scheduled && c.ScheduledAt != null && c.ScheduledAt <= now)
            .Select(c => new { c.TenantId, c.Id }).ToListAsync(cancellationToken);
        foreach (var c in due)
        {
            using var scope = tenant.Use(c.TenantId);
            try
            {
                await campaigns.StartAsync(c.Id, cancellationToken);
            }
            catch (Application.Common.Exceptions.AppException)
            {
                // An audience that has become empty, or content that was removed: the campaign goes back to a draft for its owner to fix.
                var campaign = await db.Campaigns.FirstAsync(x => x.Id == c.Id, cancellationToken);
                campaign.Status = CampaignStatuses.Draft;
                campaign.ScheduledAt = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        // A campaign that paused at the daily limit goes on once it is a new day.
        var today = now.Date;
        var paused = await db.Campaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(c => !c.IsDeleted && c.Status == CampaignStatuses.Sending && c.PausedAt != null && c.PausedAt < today)
            .Select(c => new { c.TenantId, c.Id }).ToListAsync(cancellationToken);
        foreach (var c in paused)
        {
            jobs.Enqueue<CampaignSendJob>("messaging", job => job.RunAsync(c.TenantId, c.Id, CancellationToken.None));
        }
    }
}
