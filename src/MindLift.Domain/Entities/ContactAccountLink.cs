using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Ties a contact to an additional account (a consultant who works with several companies). The contact's own
/// <see cref="Contact.AccountId"/> stays the primary account, so nothing that reads it changes.
/// </summary>
[Audited]
public sealed class ContactAccountLink : TenantEntity
{
    public Guid ContactId { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>The person's role at that account, e.g. "Advisor".</summary>
    public string? Role { get; set; }
}
