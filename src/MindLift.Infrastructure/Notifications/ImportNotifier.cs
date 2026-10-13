using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Notifications;

/// <summary>Tells the person who ran an import how it went, so the result is still there after they leave the page.</summary>
public sealed class ImportNotifier(INotificationService notifications, ITenantContext tenant)
{
    public async Task NotifyAsync(string label, string url, ImportResultDto result, CancellationToken cancellationToken = default)
    {
        if (tenant.UserId is not { } me)
        {
            return;
        }

        var summary = $"{result.Created} imported, {result.Skipped} skipped, {result.Failed} could not be imported.";
        await notifications.NotifyAsync(new NotificationRequest(
            [me], NotificationTypes.ImportCompleted, $"Your {label} import finished", summary, null, null, url, IncludeActor: true), cancellationToken);
    }
}
