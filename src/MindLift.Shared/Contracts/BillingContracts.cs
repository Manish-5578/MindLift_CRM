namespace MindLift.Shared.Contracts;

// Billing: what a company pays the platform. Online payment comes from a payment gateway once one is connected;
// until then the platform owner records payments by hand (bank transfer, cash, cheque).

public sealed record BillingPlanOption(string Key, string Name, string? Description, int MaxUsers, int MaxRecords, decimal PriceMonthly, decimal PriceYearly, string Currency, int ModuleCount, bool IsCurrent);

public sealed record BillingInvoiceDto(Guid Id, string Number, string PlanName, string Interval, decimal Amount, string Currency, string Status, DateTime PeriodStart, DateTime PeriodEnd, DateTime IssuedAt, DateTime? DueAt, DateTime? PaidAt, string? PaidVia, string? Reference, string? HostedUrl);

/// <summary>
/// The company's billing page. <c>Status</c> is "trialing", "active", "past_due" or "cancelled". <c>OnlinePayment</c> is false while no gateway
/// is connected: choosing a plan then sends an invoice to pay by transfer, and the platform owner marks it paid.
/// </summary>
public sealed record BillingOverviewDto(
    string Status,
    string PlanKey,
    string PlanName,
    string Interval,
    decimal Price,
    string Currency,
    DateTime? TrialEndsAt,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string? PendingPlanName,
    string? PendingInterval,
    int Users,
    int MaxUsers,
    int Records,
    int MaxRecords,
    bool OnlinePayment,
    string ProviderName,
    string? PaymentInstructions,
    IReadOnlyList<BillingPlanOption> Plans,
    IReadOnlyList<BillingInvoiceDto> Invoices);

public sealed record StartCheckoutRequest(string PlanKey, string Interval);

/// <summary>Either a page to pay on (<c>RedirectUrl</c>, with a gateway) or an invoice to pay by transfer (<c>Invoice</c>, without one).</summary>
public sealed record CheckoutResultDto(string Mode, string? RedirectUrl, BillingInvoiceDto? Invoice, string Message);

/// <summary>The platform owner records a payment: against an open invoice, or for a plan and period directly.</summary>
public sealed record RecordBillingPaymentRequest(Guid? InvoiceId, string? PlanKey, string? Interval, decimal? Amount, string? Reference, DateTime? PaidAt);

/// <summary>What the platform owner sees of one company's billing.</summary>
public sealed record PlatformBillingDto(
    string Status,
    string PlanName,
    string Interval,
    string Provider,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string? PendingPlanName,
    string? PendingInterval,
    decimal LifetimePaid,
    string Currency,
    IReadOnlyList<BillingInvoiceDto> Invoices);
