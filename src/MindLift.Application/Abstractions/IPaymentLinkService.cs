using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IPaymentLinkService
{
    Task<PaymentLinkDto> CreatePaymentLinkAsync(CreatePaymentLinkRequest req, CancellationToken ct = default);
    Task<PaymentLinkDto> GetPaymentLinkByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentLinkDto>> ListPaymentLinksAsync(Guid? invoiceId = null, Guid? quoteId = null, CancellationToken ct = default);
    Task<PaymentLinkDto> CancelPaymentLinkAsync(Guid id, CancellationToken ct = default);
    Task<PublicCheckoutDto> GetPublicCheckoutAsync(string token, CancellationToken ct = default);
    Task<PaymentReceiptDto> ProcessPublicPaymentAsync(ProcessPaymentRequest req, string? ipAddress, CancellationToken ct = default);
    Task<bool> HandleWebhookAsync(string provider, string payload, string? signature, CancellationToken ct = default);
}
