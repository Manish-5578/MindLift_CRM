using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Trial;

/// <summary>
/// Runs daily (plan 6.9, 11). Sends the trial reminder three days before the trial ends, and moves an expired trial to PastDue.
/// PastDue workspaces can still sign in and read their data; writes are refused by the subscription guard.
/// </summary>
public sealed class TrialExpiryJob(AppDbContext db, IEmailSender email, IClock clock, ITenantContext tenant, INotificationService notifications)
{
    public const int ReminderDaysBeforeEnd = 3;

    public async Task<TrialExpiryResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var trials = await db.Tenants.Where(t => t.Status == "Trial" && t.TrialEndsAt != null).ToListAsync(cancellationToken);

        var reminders = 0;
        var expired = 0;
        foreach (var workspace in trials)
        {
            var endsAt = workspace.TrialEndsAt!.Value;

            if (endsAt <= now)
            {
                workspace.Status = "PastDue";
                expired++;
                continue;
            }

            if (!workspace.TrialReminderSent && endsAt - now <= TimeSpan.FromDays(ReminderDaysBeforeEnd))
            {
                var owner = await db.Users.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(u => u.TenantId == workspace.Id && u.IsTenantOwner, cancellationToken);
                if (owner?.Email is not null)
                {
                    await email.SendAsync(
                        owner.Email,
                        $"Your MindLift trial ends in {ReminderDaysBeforeEnd} days",
                        $"Hello {owner.FirstName},\n\nYour {workspace.Name} trial ends on {endsAt:dd MMM yyyy} (UTC). Choose a plan to keep working without interruption.",
                        cancellationToken);
                }

                if (owner is not null)
                {
                    using (tenant.Use(workspace.Id))
                    {
                        await notifications.NotifyAsync(new NotificationRequest(
                            [owner.Id], NotificationTypes.TrialEnding, $"Your trial ends in {ReminderDaysBeforeEnd} days",
                            $"It ends on {endsAt:dd MMM yyyy} (UTC). Choose a plan to keep working without interruption.", null, null, "/settings/company", IncludeActor: true, InAppOnly: true), cancellationToken);
                    }
                }

                workspace.TrialReminderSent = true;
                reminders++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TrialExpiryResult(reminders, expired);
    }
}

public sealed record TrialExpiryResult(int RemindersSent, int Expired);
