using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// One thing that happened to a record, in the order it happened: a call, an email opened, a payment, a stage change.
/// Features write here instead of each keeping its own history, so a record's timeline can show them all.
/// </summary>
public sealed class TimelineEvent : TenantEntity
{
    /// <summary>The kind of record, for example <c>contacts</c> or <c>deals</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>What happened, for example <c>call.completed</c>. Lower case, dotted.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>One line a person can read.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Extra detail as JSON text. Never put secrets or full message bodies here.</summary>
    public string? DetailJson { get; set; }

    /// <summary>Which feature wrote it, for example <c>calls</c> or <c>email-tracking</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>The person who did it, when a person did.</summary>
    public Guid? ActorUserId { get; set; }

    public DateTime OccurredAt { get; set; }
}
