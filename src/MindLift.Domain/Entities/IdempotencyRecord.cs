using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Remembers that one piece of work (a webhook event, a scheduled send, a sync step) has already been started for a company,
/// so a retry or a replay does nothing the second time. The unique index on company, scope and key is what makes it safe
/// when two requests arrive at the same moment.
/// </summary>
public sealed class IdempotencyRecord : TenantEntity
{
    /// <summary>What kind of work this is, for example <c>webhook:twilio</c> or <c>cadence-step</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The caller's unique id for the work (an event id, a message id).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>After this time the record can be removed and the key may be used again.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the work finished, so a retry can tell "still running" from "done".</summary>
    public DateTime? CompletedAt { get; set; }
}
