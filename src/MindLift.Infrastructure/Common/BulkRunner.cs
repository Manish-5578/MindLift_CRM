using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Common;

/// <summary>
/// Applies one action to many records, one at a time, each through the record's own service so its permissions, validation, automation and audit all apply.
/// A record that cannot be changed is reported with the reason, and the others still go ahead.
/// </summary>
public static class BulkRunner
{
    /// <summary>The records a bulk request names: its ticked ids, or every record its list query matches. Refuses more than <see cref="BulkActions.MaxRecords"/>.</summary>
    public static async Task<IReadOnlyList<Guid>> TargetsAsync(
        IReadOnlyList<Guid>? ids, ListQuery? allMatching, Func<ListQuery, int, CancellationToken, Task<IReadOnlyList<Guid>>> matching, CancellationToken cancellationToken)
    {
        if (ids is { Count: > 0 } && allMatching is not null)
        {
            throw new ValidationException("ids", "Send either the chosen records or the list query, not both.");
        }

        IReadOnlyList<Guid> targets;
        if (allMatching is not null)
        {
            // One more than allowed is asked for, so "too many" can be told apart from "exactly the limit".
            targets = await matching(allMatching, BulkActions.MaxRecords + 1, cancellationToken);
        }
        else
        {
            targets = (ids ?? []).Distinct().ToList();
        }

        if (targets.Count == 0)
        {
            throw new ValidationException("ids", "Choose at least one record.");
        }

        if (targets.Count > BulkActions.MaxRecords)
        {
            throw new ValidationException("ids", $"At most {BulkActions.MaxRecords} records can be changed at once. Narrow the list with a search or filter and try again.");
        }

        return targets;
    }

    public static async Task<BulkActionResult> RunAsync(
        AppDbContext db, IReadOnlyList<Guid> ids, Func<Guid, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var failures = new List<BulkFailure>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action(id, cancellationToken);
            }
            catch (AppException exception)
            {
                failures.Add(new BulkFailure(id, null, Describe(exception)));

                // A change that failed part-way must not be saved along with the next record.
                db.ChangeTracker.Clear();
            }
        }

        return new BulkActionResult(ids.Count, ids.Count - failures.Count, failures);
    }

    private static string Describe(AppException exception) => exception switch
    {
        ValidationException { Errors.Count: > 0 } validation => string.Join(" ", validation.Errors.SelectMany(e => e.Value)),
        NotFoundException => "It was not found, or you cannot see it.",
        _ => exception.Message,
    };
}
