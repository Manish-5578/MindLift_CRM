using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ICpqService
{
    // Products
    Task<IReadOnlyList<ProductDto>> GetProductsAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken ct = default);
    Task<ProductDto> CreateProductAsync(SaveProductRequest request, CancellationToken ct = default);
    Task<ProductDto> UpdateProductAsync(Guid id, SaveProductRequest request, CancellationToken ct = default);
    Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default);

    // Price Books
    Task<IReadOnlyList<PriceBookDto>> GetPriceBooksAsync(CancellationToken ct = default);
    Task<PriceBookDto?> GetPriceBookByIdAsync(Guid id, CancellationToken ct = default);
    Task<PriceBookDto> CreatePriceBookAsync(SavePriceBookRequest request, CancellationToken ct = default);
    Task<PriceBookEntryDto> SetPriceBookEntryAsync(Guid priceBookId, SavePriceBookEntryRequest request, CancellationToken ct = default);

    // Quotes
    Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(Guid? dealId = null, Guid? accountId = null, CancellationToken ct = default);
    Task<QuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct = default);
    Task<QuoteDto> CreateQuoteAsync(SaveQuoteRequest request, CancellationToken ct = default);
    Task<QuoteDto> UpdateQuoteAsync(Guid id, SaveQuoteRequest request, CancellationToken ct = default);
    Task<bool> DeleteQuoteAsync(Guid id, CancellationToken ct = default);
    Task<QuoteDto> SyncQuoteToDealAsync(Guid quoteId, CancellationToken ct = default);
    Task<QuoteDto> UpdateQuoteStatusAsync(Guid quoteId, QuoteStatus status, CancellationToken ct = default);
    Task<InvoiceDto> ConvertQuoteToInvoiceAsync(Guid quoteId, CancellationToken ct = default);

    // Invoices & Payments
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(Guid? dealId = null, Guid? accountId = null, CancellationToken ct = default);
    Task<InvoiceDto?> GetInvoiceByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> CreateInvoiceAsync(SaveInvoiceRequest request, CancellationToken ct = default);
    Task<PaymentDto> RecordPaymentAsync(Guid invoiceId, RecordPaymentRequest request, CancellationToken ct = default);
    Task<bool> CancelInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
}

public interface IDocumentOcrService
{
    Task<BusinessDocumentDto> UploadDocumentAsync(
        string fileName,
        string contentType,
        long fileSize,
        byte[] fileBytes,
        string category,
        Guid? accountId = null,
        Guid? dealId = null,
        Guid? quoteId = null,
        Guid? invoiceId = null,
        Guid? contactId = null,
        bool sharedWithPortal = false,
        CancellationToken ct = default,
        bool linksAlreadyAuthorized = false);

    Task<OcrResultDto> ProcessOcrAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>A document shared with the portal that belongs to this customer's contact or account. Null otherwise.</summary>
    Task<BusinessDocumentDto?> GetPortalDocumentAsync(Guid id, Guid contactId, Guid? accountId, CancellationToken ct = default);

    Task<(Stream Stream, string ContentType, string FileName)?> GetPortalDocumentStreamAsync(Guid id, Guid contactId, Guid? accountId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid? accountId = null, Guid? dealId = null, Guid? contactId = null, CancellationToken ct = default);

    /// <summary>Staff only: 404s unless the document's linked CRM record (deal, else account, else contact) is in the caller's row scope.</summary>
    Task EnsureDocumentInScopeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Staff only: 404s unless every CRM record named here is in the caller's row scope.</summary>
    Task EnsureLinksInScopeAsync(Guid? accountId, Guid? dealId, Guid? contactId, Guid? quoteId = null, Guid? invoiceId = null, CancellationToken ct = default);
    Task<BusinessDocumentDto?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> GetDocumentStreamAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// A copy of a scanned PDF or picture as a PDF whose text can be searched and copied. A PDF that already has text on every page is returned as it is.
    /// Null when the document does not exist.
    /// </summary>
    Task<(Stream Stream, string ContentType, string FileName)?> GetSearchablePdfAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<BusinessDocumentDto> ReviewDocumentAsync(Guid id, ReviewDocumentRequest request, Guid reviewerUserId, CancellationToken ct = default);
    Task<bool> UpdateExtractedFieldsAsync(Guid id, UpdateExtractedFieldsRequest request, CancellationToken ct = default);
    Task<BusinessDocumentDto> ReprocessAsync(Guid id, CancellationToken ct = default);

    /// <summary>Queues the document to be read in the background (optionally as the given document type) and returns at once.</summary>
    Task<BusinessDocumentDto> QueueReadAsync(Guid id, string? documentType, CancellationToken ct = default);

    Task MarkReadFailedAsync(Guid id, string note, CancellationToken ct = default);

    Task<BusinessDocumentDto> LinkAsync(Guid id, LinkDocumentRequest request, CancellationToken ct = default);
    Task<int> RunExpiryChecksAsync(CancellationToken ct = default);
}
