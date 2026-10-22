using System;
using System.Collections.Generic;

namespace MindLift.Shared.Contracts;

public enum ProductType
{
    Product = 0,
    Service = 1,
    Subscription = 2
}

public enum BillingFrequency
{
    OneTime = 0,
    Monthly = 1,
    Quarterly = 2,
    Annual = 3
}

public enum QuoteStatus
{
    Draft = 0,
    InReview = 1,
    Sent = 2,
    Accepted = 3,
    Rejected = 4,
    Expired = 5
}

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Sent = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Overdue = 5,
    Cancelled = 6
}

public enum PaymentMethod
{
    BankTransfer = 0,
    CreditCard = 1,
    Stripe = 2,
    Cash = 3,
    Cheque = 4,
    Other = 5
}

public enum DocumentOcrStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

// ================= PRODUCT & PRICEBOOK DTOs =================

public sealed class ProductDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public ProductType Type { get; set; } = ProductType.Product;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TaxRate { get; set; }
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.OneTime;
    public bool IsActive { get; set; } = true;
    public int? StockQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SaveProductRequest
{
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "General";
    public ProductType Type { get; set; } = ProductType.Product;
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal TaxRate { get; set; }
    public BillingFrequency BillingFrequency { get; set; } = BillingFrequency.OneTime;
    public bool IsActive { get; set; } = true;
    public int? StockQuantity { get; set; }
}

public sealed class PriceBookDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsStandard { get; set; }
    public bool IsActive { get; set; } = true;
    public string Currency { get; set; } = "USD";
    public int EntryCount { get; set; }
    public List<PriceBookEntryDto> Entries { get; set; } = [];
}

public sealed class PriceBookEntryDto
{
    public Guid Id { get; set; }
    public Guid PriceBookId { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public decimal ListPrice { get; set; }
    public bool UseStandardPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SavePriceBookRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsStandard { get; set; }
    public bool IsActive { get; set; } = true;
    public string Currency { get; set; } = "USD";
}

public sealed class SavePriceBookEntryRequest
{
    public Guid ProductId { get; set; }
    public decimal ListPrice { get; set; }
    public bool UseStandardPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

// ================= QUOTE DTOs =================

public sealed class QuoteLineItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
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

public sealed class QuoteDto
{
    public Guid Id { get; set; }
    public string QuoteNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? DealId { get; set; }
    public string? DealTitle { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public Guid? ContactId { get; set; }
    public string? ContactName { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime IssueDate { get; set; }
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
    public List<QuoteLineItemDto> LineItems { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class SaveQuoteRequest
{
    public string Title { get; set; } = string.Empty;
    public Guid? DealId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpirationDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal DiscountPercent { get; set; }
    public string? TermsAndConditions { get; set; }
    public string? Notes { get; set; }
    public List<SaveQuoteLineItemRequest> LineItems { get; set; } = [];
}

public sealed class SaveQuoteLineItemRequest
{
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxRate { get; set; }
    public int SortOrder { get; set; }
}

// ================= INVOICE & PAYMENT DTOs =================

public sealed class InvoiceLineItemDto
{
    public Guid Id { get; set; }
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TotalAmount { get; set; }
    public int SortOrder { get; set; }
}

public sealed class PaymentDto
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; }
    public PaymentMethod Method { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RecordPaymentRequest
{
    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public PaymentMethod Method { get; set; } = PaymentMethod.BankTransfer;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public sealed class InvoiceDto
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid? QuoteId { get; set; }
    public string? QuoteNumber { get; set; }
    public Guid? DealId { get; set; }
    public string? DealTitle { get; set; }
    public Guid? AccountId { get; set; }
    public string? AccountName { get; set; }
    public Guid? ContactId { get; set; }
    public string? ContactName { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime IssueDate { get; set; }
    public DateTime DueDate { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Notes { get; set; }
    public List<InvoiceLineItemDto> LineItems { get; set; } = [];
    public List<PaymentDto> Payments { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class SaveInvoiceRequest
{
    public Guid? QuoteId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? ContactId { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; } = DateTime.UtcNow.AddDays(30);
    public string Currency { get; set; } = "USD";
    public decimal DiscountAmount { get; set; }
    public string? PaymentTerms { get; set; } = "Net 30";
    public string? Notes { get; set; }
    public List<SaveInvoiceLineItemRequest> LineItems { get; set; } = [];
}

public sealed class SaveInvoiceLineItemRequest
{
    public Guid? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
    public int SortOrder { get; set; }
}

// ================= OCR & DOCUMENT INTELLIGENCE DTOs =================

public sealed class OcrExtractedFieldDto
{
    public string FieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
}

public sealed class OcrResultDto
{
    public Guid DocumentId { get; set; }
    public DocumentOcrStatus Status { get; set; }
    public string DocumentType { get; set; } = "Invoice"; // Invoice, Receipt, Contract, Identity
    public string? ExtractedText { get; set; }
    public List<OcrExtractedFieldDto> ExtractedFields { get; set; } = [];
    public decimal? DetectedTotal { get; set; }
    public string? DetectedVendor { get; set; }
    public string? DetectedInvoiceNumber { get; set; }
    public DateTime? DetectedDate { get; set; }
    public TimeSpan ProcessingDuration { get; set; }

    /// <summary>How well the pages were read, 0–1 (1 for a file that already had its text). Null when the document was not read.</summary>
    public double? ReadConfidence { get; set; }

    /// <summary>Pages that scored low even after every way of reading them was tried. A person should check these against the original.</summary>
    public List<int> LowConfidencePages { get; set; } = [];
}

public sealed class BusinessDocumentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
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
    public string? RejectReason { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateOnly? IssueDate { get; set; }
    public double? Confidence { get; set; }
    public string? DetectedVendor { get; set; }
    public decimal? DetectedTotal { get; set; }
    public string? DetectedInvoiceNumber { get; set; }
    public DocumentOcrStatus OcrStatus { get; set; } = DocumentOcrStatus.Pending;
    public OcrResultDto? OcrResult { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed record ApplyFieldToRecordItem(string FieldName, string TargetField, string Value);

public sealed record ReviewDocumentRequest(string Action, string? Reason = null, List<ApplyFieldToRecordItem>? ApplyFields = null);

public sealed record UpdateExtractedFieldsRequest(List<OcrExtractedFieldDto> Fields);
