namespace MindLift.Domain.Entities;

/// <summary>
/// What a company pays for: its plan, monthly or yearly, and the period it has paid up to. One per company.
/// The company's own <see cref="Tenant.Status"/> follows this (Trial, Active, PastDue, Cancelled), and that is what lets it work or not.
/// </summary>
public sealed class Subscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid PlanId { get; set; }

    /// <summary>"monthly" or "yearly".</summary>
    public string Interval { get; set; } = "monthly";

    /// <summary>"trialing", "active", "past_due" or "cancelled".</summary>
    public string Status { get; set; } = "trialing";

    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    /// <summary>The company asked to stop: it keeps working until the end of the paid period, then stops.</summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>A change the company asked for that is waiting for payment (with the manual provider).</summary>
    public Guid? PendingPlanId { get; set; }
    public string? PendingInterval { get; set; }

    /// <summary>"manual" until a payment gateway is connected, then that gateway's key.</summary>
    public string Provider { get; set; } = "manual";
    public string? ProviderCustomerId { get; set; }
    public string? ProviderSubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>An invoice from the platform to a company (not one of the company's own invoices to its customers).</summary>
public sealed class BillingInvoice
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public string Interval { get; set; } = "monthly";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>"open", "paid" or "void".</summary>
    public string Status { get; set; } = "open";

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>How it was paid: "manual" (recorded by the platform owner) or the gateway's key.</summary>
    public string? PaidVia { get; set; }

    /// <summary>The bank or gateway reference, or the platform owner's note.</summary>
    public string? Reference { get; set; }

    public string? ProviderInvoiceId { get; set; }

    /// <summary>A page where the company can see or pay the invoice, when the gateway provides one.</summary>
    public string? HostedUrl { get; set; }
}

/// <summary>A message received from a payment gateway, kept so the same message is never applied twice.</summary>
public sealed class BillingEvent
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Provider { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? Payload { get; set; }
}

/// <summary>
/// A time the platform owner looked at a company's workspace to help with a support question: read-only, time-limited, with a reason,
/// and a record of every page of data viewed. Kept even after it ends, for the company's audit.
/// </summary>
public sealed class SupportSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }

    /// <summary>The company's owner, whose view of the workspace is shown.</summary>
    public Guid ViewAsUserId { get; set; }

    public Guid PlatformUserId { get; set; }
    public string PlatformUserName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>The one-time code that opens the session, hashed; cleared once used.</summary>
    public string? CodeHash { get; set; }
    public DateTime CodeExpiresAt { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>The data pages (API addresses) read during the session, in order.</summary>
    public string[] VisitedPaths { get; set; } = [];
}
