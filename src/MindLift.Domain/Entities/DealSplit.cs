using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Represents a revenue or overlay credit split among team members collaborating on a Deal.
/// </summary>
[Audited]
public sealed class DealSplit : TenantEntity
{
    public Guid DealId { get; set; }
    public Deal Deal { get; set; } = null!;

    public Guid UserId { get; set; }

    /// <summary>"Revenue" (direct revenue share totaling 100%) or "Overlay" (sales support/specialist credit).</summary>
    public string SplitType { get; set; } = "Revenue";

    /// <summary>Percentage share of the deal (0.01% - 100.00%).</summary>
    public decimal Percentage { get; set; }

    /// <summary>Calculated currency amount (Deal.Amount * Percentage / 100).</summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }
}
