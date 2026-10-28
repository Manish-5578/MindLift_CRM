using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 6 / #38: Customer health, NPS and CSAT (customer-health).
/// Records individual customer satisfaction surveys (NPS, CSAT, CES).
/// </summary>
[Audited]
public sealed class CustomerSurvey : TenantEntity
{
    /// <summary>NPS (0-10), CSAT (1-5), or CES (1-7).</summary>
    public string Type { get; set; } = "NPS";

    /// <summary>The numerical score provided by the customer.</summary>
    public int Score { get; set; }

    /// <summary>Automated sentiment: Promoter/Passive/Detractor (for NPS), Satisfied/Neutral/Dissatisfied (for CSAT).</summary>
    public string Sentiment { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public string Channel { get; set; } = "web";

    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }

    public Guid? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? DealId { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
}
