namespace MindLift.Shared.Contracts;

public sealed class SendQuoteESignRequest
{
    public string SignerEmail { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string? SignerTitle { get; set; }
    public int ExpiresInDays { get; set; } = 14;
    public string? CustomMessage { get; set; }
}

public sealed class SignQuoteRequest
{
    public string SigningToken { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string? SignerTitle { get; set; }
    public string SignatureData { get; set; } = string.Empty;
    public bool ConsentAgreed { get; set; }
}

public sealed class DeclineQuoteESignRequest
{
    public string SigningToken { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? DeclinedBy { get; set; }
}

public sealed class RevokeQuoteESignRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class QuoteSignatureLogDto
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? ActorEmail { get; set; }
    public string? ActorName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
}

public sealed class QuoteESignDetailsDto
{
    public Guid QuoteId { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? DealId { get; set; }
    public string? DealTitle { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public Guid? ContactId { get; set; }
    public string? ContactName { get; set; }
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime IssueDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }

    // E-Sign Specific
    public string ESignStatus { get; set; } = "Draft";
    public string? SignerEmail { get; set; }
    public string? SignerName { get; set; }
    public string? SignerTitle { get; set; }
    public string? SigningToken { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SignatureData { get; set; }
    public string? SignerIpAddress { get; set; }
    public DateTime? SigningExpiresAt { get; set; }
    public string? DeclineReason { get; set; }
    public string? SecurityHash { get; set; }
    public string? SigningUrl { get; set; }

    public IReadOnlyList<QuoteLineItemDto> LineItems { get; set; } = [];
    public IReadOnlyList<QuoteSignatureLogDto> AuditTrail { get; set; } = [];
}

public sealed class CertificateOfCompletionDto
{
    public Guid CertificateId { get; set; }
    public Guid QuoteId { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "USD";
    public string ESignStatus { get; set; } = string.Empty;
    public string? SignerName { get; set; }
    public string? SignerEmail { get; set; }
    public string? SignerTitle { get; set; }
    public string? SignerIpAddress { get; set; }
    public DateTime? SignedAt { get; set; }
    public string? SecurityHash { get; set; }
    public IReadOnlyList<QuoteSignatureLogDto> AuditTrail { get; set; } = [];
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
