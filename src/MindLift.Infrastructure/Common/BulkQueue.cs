using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Common;

/// <summary>
/// Decides whether a bulk change runs while the person waits or in the background. Up to <see cref="InlineLimit"/> records it runs at once and
/// the answer comes back with the request. More than that goes to a background job, so a large change never times out at a proxy; the person
/// gets a notification with the result when it is done.
/// </summary>
public sealed class BulkQueue(IBackgroundJobClient jobs, ITenantContext tenant)
{
    public const int InlineLimit = 50;

    private static readonly AsyncLocal<bool> InBackground = new();

    /// <summary>True while a background job is applying the change, so it is not queued a second time.</summary>
    public static bool IsBackground => InBackground.Value;

    public bool ShouldQueue(IReadOnlyList<Guid> ids) => ids.Count > InlineLimit && !IsBackground && tenant.TenantId is not null && tenant.UserId is not null;

    /// <summary>Queues the change for the records already chosen. The answer says it was queued; nothing has changed yet.</summary>
    public BulkActionResult Queue(string entity, BulkActionRequest request, IReadOnlyList<Guid> ids)
        => Enqueue(entity, false, JsonSerializer.Serialize(request with { Ids = ids, AllMatching = null }), ids.Count);

    public BulkActionResult QueueDelete(string entity, IReadOnlyList<Guid> ids)
        => Enqueue(entity, true, JsonSerializer.Serialize(new BulkDeleteRequest(ids, null)), ids.Count);

    private BulkActionResult Enqueue(string entity, bool delete, string payload, int count)
    {
        var jobId = jobs.Enqueue<BulkJob>("imports", job => job.RunAsync(tenant.TenantId!.Value, tenant.UserId!.Value, entity, delete, payload, CancellationToken.None));
        return new BulkActionResult(count, 0, [], Queued: true, JobId: jobId);
    }

    internal static IDisposable Background()
    {
        InBackground.Value = true;
        return new Reset();
    }

    private sealed class Reset : IDisposable
    {
        public void Dispose() => InBackground.Value = false;
    }
}

/// <summary>Runs a queued bulk change as the person who asked for it, then tells them how it went.</summary>
public sealed class BulkJob(IServiceProvider services, ITenantContext tenant, INotificationService notifications)
{
    [AutomaticRetry(Attempts = 0)]   // a half-applied change must not run again from the start
    public async Task RunAsync(Guid tenantId, Guid userId, string entity, bool delete, string payload, CancellationToken cancellationToken)
    {
        using var scope = tenant.Use(tenantId, userId);
        using var background = BulkQueue.Background();

        BulkActionResult result;
        if (delete)
        {
            var request = JsonSerializer.Deserialize<BulkDeleteRequest>(payload)!;
            result = entity switch
            {
                "leads" => await services.GetRequiredService<Leads.LeadService>().BulkDeleteAsync(request, cancellationToken),
                "contacts" => await services.GetRequiredService<Contacts.ContactService>().BulkDeleteAsync(request, cancellationToken),
                "accounts" => await services.GetRequiredService<Contacts.AccountService>().BulkDeleteAsync(request, cancellationToken),
                "deals" => await services.GetRequiredService<Deals.DealService>().BulkDeleteAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown bulk target {entity}."),
            };
        }
        else
        {
            var request = JsonSerializer.Deserialize<BulkActionRequest>(payload)!;
            result = entity switch
            {
                "leads" => await services.GetRequiredService<Leads.LeadService>().BulkAsync(request, cancellationToken),
                "contacts" => await services.GetRequiredService<Contacts.ContactService>().BulkAsync(request, cancellationToken),
                "accounts" => await services.GetRequiredService<Contacts.AccountService>().BulkAsync(request, cancellationToken),
                "deals" => await services.GetRequiredService<Deals.DealService>().BulkAsync(request, cancellationToken),
                _ => throw new InvalidOperationException($"Unknown bulk target {entity}."),
            };
        }

        var failed = result.Failures.Count;
        var summary = failed == 0
            ? $"All {result.Succeeded} {entity} were {(delete ? "deleted" : "updated")}."
            : $"{result.Succeeded} of {result.Requested} {entity} were {(delete ? "deleted" : "updated")}. {failed} could not be: {string.Join("; ", result.Failures.Take(3).Select(f => f.Message))}{(failed > 3 ? " …" : "")}";
        await notifications.NotifyAsync(new NotificationRequest(
            [userId], NotificationTypes.BulkCompleted, $"Your bulk {(delete ? "delete" : "change")} of {entity} finished", summary, null, null, $"/{entity}", IncludeActor: true), cancellationToken);
    }
}
