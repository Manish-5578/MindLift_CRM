namespace MindLift.Infrastructure.Billing;

/// <summary>What a gateway needs to start a payment for a company.</summary>
public sealed record CheckoutContext(Guid TenantId, string CompanyName, string OwnerEmail, string PlanKey, string PlanName, string Interval, decimal Amount, string Currency, string SuccessUrl, string CancelUrl);

/// <summary>
/// Something a gateway told us, in one shape whatever the gateway. <c>Type</c> is "payment_succeeded", "payment_failed" or "subscription_cancelled".
/// </summary>
public sealed record ProviderEvent(
    string EventId,
    string Type,
    Guid? TenantId,
    string? ProviderSubscriptionId,
    string? ProviderCustomerId,
    string? PlanKey,
    string? Interval,
    decimal? Amount,
    string? Currency,
    DateTime? PeriodEnd,
    string? ProviderInvoiceId,
    string? HostedUrl,
    string? RawPayload = null);

/// <summary>
/// A payment gateway (Stripe, Razorpay, ...). Each one turns its own checkout and webhooks into the shapes above; everything else
/// (subscription status, invoices, the company's access) is handled once in <see cref="BillingService"/>.
/// </summary>
public interface IPaymentProvider
{
    string Key { get; }
    string DisplayName { get; }

    /// <summary>True when the company can pay online. False for manual billing.</summary>
    bool Online { get; }

    Task<string> CreateCheckoutUrlAsync(CheckoutContext context, CancellationToken cancellationToken);

    /// <summary>Checks the webhook's signature and returns what it says. Throws when the signature is wrong.</summary>
    Task<IReadOnlyList<ProviderEvent>> ReadWebhookAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

/// <summary>
/// Billing without a gateway: a company that chooses a plan gets an invoice to pay by bank transfer (Billing:PaymentInstructions),
/// and the platform owner marks it paid in the console. Used until a gateway is connected.
/// </summary>
public sealed class ManualPaymentProvider : IPaymentProvider
{
    public string Key => "manual";
    public string DisplayName => "Bank transfer";
    public bool Online => false;

    public Task<string> CreateCheckoutUrlAsync(CheckoutContext context, CancellationToken cancellationToken)
        => throw new NotSupportedException("Manual billing has no online checkout.");

    public Task<IReadOnlyList<ProviderEvent>> ReadWebhookAsync(string body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        => throw new NotSupportedException("Manual billing receives no webhooks.");
}
