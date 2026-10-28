namespace MindLift.Shared.Contracts;

public sealed class CreatePaymentLinkRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Amount { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? QuoteId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    public int? ExpiresInDays { get; set; } = 14;
    public bool AllowPartial { get; set; }
    public string GatewayProvider { get; set; } = "Hosted";
}

public sealed class PaymentLinkDto
{
    public Guid Id { get; set; }
    public string LinkNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "Active";
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? QuoteId { get; set; }
    public string? QuoteNumber { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    public string PaymentToken { get; set; } = string.Empty;
    public string CheckoutUrl { get; set; } = string.Empty;
    public string GatewayProvider { get; set; } = "Hosted";
    public string? GatewayReference { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool AllowPartial { get; set; }
    public DateTime? PaidAt { get; set; }
    public string? PaymentMethodUsed { get; set; }
    public string? ReceiptNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class PublicCheckoutDto
{
    public Guid Id { get; set; }
    public string LinkNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Status { get; set; } = "Active";
    public string? CustomerEmail { get; set; }
    public string? CustomerName { get; set; }
    public string PaymentToken { get; set; } = string.Empty;
    public string GatewayProvider { get; set; } = "Hosted";
    public DateTime? ExpiresAt { get; set; }
    public bool AllowPartial { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? QuoteNumber { get; set; }
}

public sealed class ProcessPaymentRequest
{
    public string PaymentToken { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "card";
    public string? CardHolderName { get; set; }
    public string? CardLast4 { get; set; }
    public string? Reference { get; set; }
}

public sealed class PaymentReceiptDto
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid PaymentLinkId { get; set; }
    public string LinkNumber { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public string Currency { get; set; } = "USD";
    public string Status { get; set; } = string.Empty;
    public DateTime PaidAt { get; set; }
    public string PaymentMethodUsed { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
}

public sealed class WebhookEventPayload
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PaymentToken { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? PaymentMethod { get; set; }
    public string? TransactionReference { get; set; }
}
