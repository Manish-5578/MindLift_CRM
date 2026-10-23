namespace MindLift.Shared.Contracts;

/// <summary>The actions a list can apply to many records at once (plan 18.8.1, bulk mode).</summary>
public static class BulkActions
{
    /// <summary>Give the records to <see cref="BulkActionRequest.OwnerId"/>.</summary>
    public const string Assign = "assign";

    /// <summary>Set the lead status to <see cref="BulkActionRequest.Status"/>. Leads only.</summary>
    public const string ChangeStatus = "status";

    /// <summary>Add <see cref="BulkActionRequest.Tags"/> to the tags each record already has. Leads only.</summary>
    public const string AddTags = "tags";

    /// <summary>The most records one request may change. "Select all" beyond this asks the user to narrow the list.</summary>
    public const int MaxRecords = 500;
}

/// <summary>
/// Apply one action to many records. Either <c>Ids</c> (the rows the user ticked) or <c>AllMatching</c> (every record the list's search and filters match) is given.
/// Each record goes through the same checks as editing it on its own page; one that fails is reported and the rest still go ahead.
/// </summary>
public sealed record BulkActionRequest(
    string Action,
    IReadOnlyList<Guid>? Ids,
    ListQuery? AllMatching,
    Guid? OwnerId = null,
    string? Status = null,
    IReadOnlyList<string>? Tags = null);

/// <summary>Delete many records: the ticked rows, or every record the list matches.</summary>
public sealed record BulkDeleteRequest(IReadOnlyList<Guid>? Ids, ListQuery? AllMatching);

public sealed record BulkFailure(Guid Id, string? Name, string Message);

public sealed record BulkActionResult(int Requested, int Succeeded, IReadOnlyList<BulkFailure> Failures);
