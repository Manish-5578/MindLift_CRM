using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IQuoteESignService
{
    Task<QuoteESignDetailsDto> SendForSignatureAsync(Guid quoteId, SendQuoteESignRequest req, CancellationToken ct = default);
    Task<QuoteESignDetailsDto> GetSigningDetailsByTokenAsync(string token, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<QuoteESignDetailsDto> GetESignDetailsByQuoteIdAsync(Guid quoteId, CancellationToken ct = default);
    Task<QuoteESignDetailsDto> SignAsync(SignQuoteRequest req, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<QuoteESignDetailsDto> DeclineAsync(DeclineQuoteESignRequest req, string? ipAddress, string? userAgent, CancellationToken ct = default);
    Task<QuoteESignDetailsDto> RevokeAsync(Guid quoteId, string reason, CancellationToken ct = default);
    Task<IReadOnlyList<QuoteSignatureLogDto>> GetAuditTrailAsync(Guid quoteId, CancellationToken ct = default);
    Task<CertificateOfCompletionDto> GetCertificateAsync(Guid quoteId, CancellationToken ct = default);
}
