using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

[Audited]
public sealed class Product : TenantEntity, ISampleData
{
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public int Type { get; set; } = 0; // 0=Product, 1=Service, 2=Subscription
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TaxRate { get; set; }
    public int BillingFrequency { get; set; } = 0; // 0=OneTime, 1=Monthly, 2=Quarterly, 3=Annual
    public bool IsActive { get; set; } = true;
    public int? StockQuantity { get; set; }
    public bool IsSample { get; set; }
}

[Audited]
public sealed class PriceBook : TenantEntity, ISampleData
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsStandard { get; set; }
    public bool IsActive { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public bool IsSample { get; set; }

    public ICollection<PriceBookEntry> Entries { get; set; } = new List<PriceBookEntry>();
}

[Audited]
public sealed class PriceBookEntry : TenantEntity
{
    public Guid PriceBookId { get; set; }
    public PriceBook PriceBook { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal ListPrice { get; set; }
    public bool UseStandardPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

[Audited]
public sealed class Quote : TenantEntity, ISampleData
{
    public string QuoteNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? DealId { get; set; }
    public Deal? Deal { get; set; }
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int Status { get; set; } = 0; // 0=Draft, 1=InReview, 2=Sent, 3=Accepted, 4=Rejected, 5=Expired
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpirationDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal SubTotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }
    public bool IsSyncedToDeal { get; set; }
    public bool IsSample { get; set; }

    // E-Signature
    public string ESignStatus { get; set; } = "Draft"; // Draft, Sent, Viewed, Signed, Declined, Revoked, Expired
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

    public ICollection<QuoteLineItem> LineItems { get; set; } = new List<QuoteLineItem>();
    public ICollection<QuoteSignatureLog> SignatureLogs { get; set; } = new List<QuoteSignatureLog>();
}

[Audited]
public sealed class QuoteLineItem : TenantEntity
{
    public Guid QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public int SortOrder { get; set; }
}

[Audited]
public sealed class QuoteSignatureLog : TenantEntity
{
    public Guid QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;
    public string Action { get; set; } = string.Empty; // Sent, Viewed, Signed, Declined, Revoked
    public string? ActorEmail { get; set; }
    public string? ActorName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Details { get; set; }
}

[Audited]
public sealed class Invoice : TenantEntity, ISampleData
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid? QuoteId { get; set; }
    public Quote? Quote { get; set; }
    public Guid? DealId { get; set; }
    public Deal? Deal { get; set; }
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int Status { get; set; } = 0; // 0=Draft, 1=Issued, 2=Sent, 3=PartiallyPaid, 4=Paid, 5=Overdue, 6=Cancelled
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(30);
    public string Currency { get; set; } = "USD";
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public bool IsSample { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
    public ICollection<PaymentRecord> Payments { get; set; } = new List<PaymentRecord>();
}

[Audited]
public sealed class InvoiceLineItem : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TotalAmount { get; set; }
    public int SortOrder { get; set; }
}

[Audited]
public sealed class PaymentRecord : TenantEntity
{
    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public int Method { get; set; } = 0; // 0=BankTransfer, 1=CreditCard, etc.
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

[Audited]
public sealed class BusinessDocument : TenantEntity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long FileSizeBytes { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string Category { get; set; } = "General"; // Contract, Quote, Invoice, Receipt, Identity
    public Guid? AssociatedAccountId { get; set; }
    public Guid? AssociatedDealId { get; set; }
    public Guid? AssociatedQuoteId { get; set; }
    public Guid? AssociatedInvoiceId { get; set; }
    public Guid? AssociatedContactId { get; set; }
    public bool SharedWithPortal { get; set; }
    public string Status { get; set; } = "Ready"; // Queued, Processing, NeedsReview, Ready, Rejected
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? PeriodDate { get; set; }
    public double? Confidence { get; set; }
    public int OcrStatus { get; set; } = 0; // 0=Pending, 1=Processing, 2=Completed, 3=Failed
    public string? OcrRawText { get; set; }
    public string? OcrStructuredDataJson { get; set; }
    public decimal? DetectedTotal { get; set; }
    public string? DetectedVendor { get; set; }
    public string? DetectedInvoiceNumber { get; set; }
    public DateTime? DetectedDate { get; set; }
    public string? RejectReason { get; set; }
    public string? ExpiryNotifiedOffsetsJson { get; set; }

    /// <summary>How the fields were read: "ai", "rules" or "mixed".</summary>
    public string? ExtractionMethod { get; set; }

    /// <summary>Why AI was not used or did not finish.</summary>
    public string? ExtractionNote { get; set; }

    /// <summary>How well the pages were read, 0 to 1.</summary>
    public double? ReadConfidence { get; set; }

    /// <summary>JSON list of page numbers that read poorly.</summary>
    public string? LowConfidencePagesJson { get; set; }
}
