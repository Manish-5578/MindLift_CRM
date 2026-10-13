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
        bool sharedWithPortal = true,
        CancellationToken ct = default);

    Task<OcrResultDto> ProcessOcrAsync(Guid documentId, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid? accountId = null, Guid? dealId = null, CancellationToken ct = default);
    Task<BusinessDocumentDto?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default);
    Task<(Stream Stream, string ContentType, string FileName)?> GetDocumentStreamAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct = default);
}
