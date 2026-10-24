using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A customer's request for help: what they asked, who looks after it, how urgent it is and when an answer is due.
/// Status moves open, pending (waiting for the customer), resolved, closed.
/// </summary>
public sealed class Ticket : TenantEntity
{
    /// <summary>The number people quote, for example T-10001.</summary>
    public string Number { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>"open", "pending", "resolved" or "closed".</summary>
    public string Status { get; set; } = "open";

    /// <summary>"low", "normal", "high" or "urgent".</summary>
    public string Priority { get; set; } = "normal";

    public string? Category { get; set; }

    /// <summary>"manual", "email" or "portal".</summary>
    public string Channel { get; set; } = "manual";

    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? AssigneeId { get; set; }

    /// <summary>When the first answer is due, from the priority. A ticket answered or finished after this is late.</summary>
    public DateTime? DueAt { get; set; }

    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
}

/// <summary>One message on a ticket. An internal note is seen by staff only.</summary>
public sealed class TicketComment : TenantEntity
{
    public Guid TicketId { get; set; }
    public Guid? AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
}
