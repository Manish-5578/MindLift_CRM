using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Cpq;

/// <summary>
/// Phase 3 / #19: Quote E-Signature (quote-esign).
/// Implementation of legally defensible e-signature workflow, tamper-evident document hashing,
/// public signing access, and immutable audit logs.
/// </summary>
public sealed class QuoteESignService(
    AppDbContext db,
    ITenantContext tenant,
    IFeatureFlags featureFlags,
    ILogger<QuoteESignService> logger) : IQuoteESignService
{
    public async Task<QuoteESignDetailsDto> SendForSignatureAsync(Guid quoteId, SendQuoteESignRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SignerEmail) || !req.SignerEmail.Contains('@'))
            throw new ValidationException("SignerEmail", "A valid signer email address is required.");

        if (string.IsNullOrWhiteSpace(req.SignerName))
            throw new ValidationException("SignerName", "Signer name is required.");

        var quote = await db.Quotes
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        if (quote.ESignStatus == "Signed")
            throw new ValidationException("ESignStatus", "Quote has already been signed and cannot be re-sent.");

        var token = "es_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiryDays = req.ExpiresInDays > 0 ? req.ExpiresInDays : 14;

        quote.ESignStatus = "Sent";
        quote.SignerEmail = req.SignerEmail.Trim();
        quote.SignerName = req.SignerName.Trim();
        quote.SignerTitle = req.SignerTitle?.Trim();
        quote.SigningToken = token;
        quote.SigningExpiresAt = DateTime.UtcNow.AddDays(expiryDays);
        quote.DeclineReason = null;
        quote.UpdatedAt = DateTime.UtcNow;

        if (quote.Status == (int)QuoteStatus.Draft)
        {
            quote.Status = (int)QuoteStatus.Sent;
        }

        var actorEmail = "sales@mindlift.local";
        if (tenant.UserId.HasValue)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == tenant.UserId.Value, ct);
            if (user != null) actorEmail = user.Email ?? actorEmail;
        }

        var log = new QuoteSignatureLog
        {
            TenantId = quote.TenantId,
            QuoteId = quote.Id,
            Action = "Sent",
            ActorEmail = actorEmail,
            ActorName = quote.SignerName,
            Timestamp = DateTime.UtcNow,
            Details = $"Sent for signature to {quote.SignerName} <{quote.SignerEmail}>. Expires in {expiryDays} days."
        };

        db.QuoteSignatureLogs.Add(log);
        await db.SaveChangesAsync(ct);

        var logs = await GetLogsForQuoteAsync(quote.Id, ct);
        return MapDetails(quote, logs);
    }

    public async Task<QuoteESignDetailsDto> GetSigningDetailsByTokenAsync(string token, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new NotFoundException("SigningToken", token ?? string.Empty);

        var quote = await db.Quotes
            .IgnoreQueryFilters()
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.SigningToken == token && !q.IsDeleted, ct)
            ?? throw new NotFoundException("SigningToken", token);

        using (tenant.Use(quote.TenantId))
        {
            if (!await featureFlags.IsEnabledAsync("quote-esign", ct))
                throw new NotFoundException("Feature", "quote-esign");

            // Check if expired
            if (quote.SigningExpiresAt.HasValue && quote.SigningExpiresAt.Value < DateTime.UtcNow && quote.ESignStatus != "Signed")
            {
                if (quote.ESignStatus != "Expired")
                {
                    quote.ESignStatus = "Expired";
                    var expLog = new QuoteSignatureLog
                    {
                        TenantId = quote.TenantId,
                        QuoteId = quote.Id,
                        Action = "Expired",
                        Timestamp = DateTime.UtcNow,
                        Details = "Signing invitation expired."
                    };
                    db.QuoteSignatureLogs.Add(expLog);
                    await db.SaveChangesAsync(ct);
                }
            }
            else if (quote.ESignStatus == "Sent")
            {
                quote.ESignStatus = "Viewed";
                var viewLog = new QuoteSignatureLog
                {
                    TenantId = quote.TenantId,
                    QuoteId = quote.Id,
                    Action = "Viewed",
                    ActorEmail = quote.SignerEmail,
                    ActorName = quote.SignerName,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Timestamp = DateTime.UtcNow,
                    Details = "Document viewed by recipient."
                };
                db.QuoteSignatureLogs.Add(viewLog);
                await db.SaveChangesAsync(ct);
            }

            var logs = await db.QuoteSignatureLogs
                .Where(l => l.QuoteId == quote.Id)
                .OrderBy(l => l.Timestamp)
                .ToListAsync(ct);

            return MapDetails(quote, logs);
        }
    }

    public async Task<QuoteESignDetailsDto> GetESignDetailsByQuoteIdAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await db.Quotes
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        var logs = await GetLogsForQuoteAsync(quote.Id, ct);
        return MapDetails(quote, logs);
    }

    public async Task<QuoteESignDetailsDto> SignAsync(SignQuoteRequest req, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SigningToken))
            throw new ValidationException("SigningToken", "Signing token is required.");

        if (!req.ConsentAgreed)
            throw new ValidationException("ConsentAgreed", "You must consent to sign electronically.");

        if (string.IsNullOrWhiteSpace(req.SignatureData))
            throw new ValidationException("SignatureData", "Signature data is required.");

        var quote = await db.Quotes
            .IgnoreQueryFilters()
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.SigningToken == req.SigningToken && !q.IsDeleted, ct)
            ?? throw new NotFoundException("SigningToken", req.SigningToken);

        using (tenant.Use(quote.TenantId))
        {
            if (!await featureFlags.IsEnabledAsync("quote-esign", ct))
                throw new NotFoundException("Feature", "quote-esign");

            if (quote.ESignStatus == "Signed")
                throw new ValidationException("ESignStatus", "This quote has already been signed.");

            if (quote.ESignStatus == "Declined")
                throw new ValidationException("ESignStatus", "This quote has been declined.");

            if (quote.ESignStatus == "Revoked")
                throw new ValidationException("ESignStatus", "This signature request has been revoked.");

            if (quote.SigningExpiresAt.HasValue && quote.SigningExpiresAt.Value < DateTime.UtcNow)
                throw new ValidationException("ESignStatus", "This signing invitation has expired.");

            var signedAt = DateTime.UtcNow;
            var signerName = string.IsNullOrWhiteSpace(req.SignerName) ? quote.SignerName ?? "Signer" : req.SignerName.Trim();

            // Calculate tamper-evident SHA-256 seal
            var rawData = $"{quote.Id}:{quote.QuoteNumber}:{quote.GrandTotal}:{quote.Currency}:{quote.SignerEmail}:{signerName}:{req.SignatureData}:{signedAt:O}";
            var securityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawData)));

            quote.ESignStatus = "Signed";
            quote.SignerName = signerName;
            if (!string.IsNullOrWhiteSpace(req.SignerTitle)) quote.SignerTitle = req.SignerTitle.Trim();
            quote.SignedAt = signedAt;
            quote.SignatureData = req.SignatureData;
            quote.SignerIpAddress = ipAddress;
            quote.SecurityHash = securityHash;
            quote.Status = (int)QuoteStatus.Accepted;
            quote.UpdatedAt = signedAt;

            var log = new QuoteSignatureLog
            {
                TenantId = quote.TenantId,
                QuoteId = quote.Id,
                Action = "Signed",
                ActorEmail = quote.SignerEmail,
                ActorName = signerName,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Timestamp = signedAt,
                Details = $"Digitally signed. Tamper seal: {securityHash[..16]}..."
            };

            db.QuoteSignatureLogs.Add(log);
            await db.SaveChangesAsync(ct);

            var logs = await db.QuoteSignatureLogs
                .Where(l => l.QuoteId == quote.Id)
                .OrderBy(l => l.Timestamp)
                .ToListAsync(ct);

            return MapDetails(quote, logs);
        }
    }

    public async Task<QuoteESignDetailsDto> DeclineAsync(DeclineQuoteESignRequest req, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.SigningToken))
            throw new ValidationException("SigningToken", "Signing token is required.");

        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new ValidationException("Reason", "Reason for declining is required.");

        var quote = await db.Quotes
            .IgnoreQueryFilters()
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.SigningToken == req.SigningToken && !q.IsDeleted, ct)
            ?? throw new NotFoundException("SigningToken", req.SigningToken);

        using (tenant.Use(quote.TenantId))
        {
            if (!await featureFlags.IsEnabledAsync("quote-esign", ct))
                throw new NotFoundException("Feature", "quote-esign");

            if (quote.ESignStatus == "Signed")
                throw new ValidationException("ESignStatus", "A signed quote cannot be declined.");

            quote.ESignStatus = "Declined";
            quote.DeclineReason = req.Reason.Trim();
            quote.Status = (int)QuoteStatus.Rejected;
            quote.UpdatedAt = DateTime.UtcNow;

            var declinerName = string.IsNullOrWhiteSpace(req.DeclinedBy) ? quote.SignerName ?? "Recipient" : req.DeclinedBy.Trim();

            var log = new QuoteSignatureLog
            {
                TenantId = quote.TenantId,
                QuoteId = quote.Id,
                Action = "Declined",
                ActorEmail = quote.SignerEmail,
                ActorName = declinerName,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Timestamp = DateTime.UtcNow,
                Details = $"Quote declined. Reason: {quote.DeclineReason}"
            };

            db.QuoteSignatureLogs.Add(log);
            await db.SaveChangesAsync(ct);

            var logs = await db.QuoteSignatureLogs
                .Where(l => l.QuoteId == quote.Id)
                .OrderBy(l => l.Timestamp)
                .ToListAsync(ct);

            return MapDetails(quote, logs);
        }
    }

    public async Task<QuoteESignDetailsDto> RevokeAsync(Guid quoteId, string reason, CancellationToken ct = default)
    {
        var quote = await db.Quotes
            .Include(q => q.LineItems)
            .Include(q => q.Deal)
            .Include(q => q.Account)
            .Include(q => q.Contact)
            .FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        if (quote.ESignStatus == "Signed")
            throw new ValidationException("ESignStatus", "Cannot revoke a quote that has already been signed.");

        quote.ESignStatus = "Revoked";
        quote.SigningToken = null;
        quote.UpdatedAt = DateTime.UtcNow;

        var log = new QuoteSignatureLog
        {
            TenantId = quote.TenantId,
            QuoteId = quote.Id,
            Action = "Revoked",
            Timestamp = DateTime.UtcNow,
            Details = string.IsNullOrWhiteSpace(reason) ? "Signature request revoked by sender." : $"Revoked: {reason.Trim()}"
        };

        db.QuoteSignatureLogs.Add(log);
        await db.SaveChangesAsync(ct);

        var logs = await GetLogsForQuoteAsync(quote.Id, ct);
        return MapDetails(quote, logs);
    }

    public async Task<IReadOnlyList<QuoteSignatureLogDto>> GetAuditTrailAsync(Guid quoteId, CancellationToken ct = default)
    {
        var exists = await db.Quotes.AnyAsync(q => q.Id == quoteId, ct);
        if (!exists) throw new NotFoundException("Quote", quoteId);

        var logs = await GetLogsForQuoteAsync(quoteId, ct);
        return logs.Select(MapLogDto).ToList();
    }

    public async Task<CertificateOfCompletionDto> GetCertificateAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        var logs = await GetLogsForQuoteAsync(quoteId, ct);

        return new CertificateOfCompletionDto
        {
            CertificateId = Guid.NewGuid(),
            QuoteId = quote.Id,
            QuoteNumber = quote.QuoteNumber,
            Title = quote.Title,
            GrandTotal = quote.GrandTotal,
            Currency = quote.Currency,
            ESignStatus = quote.ESignStatus,
            SignerName = quote.SignerName,
            SignerEmail = quote.SignerEmail,
            SignerTitle = quote.SignerTitle,
            SignerIpAddress = quote.SignerIpAddress,
            SignedAt = quote.SignedAt,
            SecurityHash = quote.SecurityHash,
            AuditTrail = logs.Select(MapLogDto).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<List<QuoteSignatureLog>> GetLogsForQuoteAsync(Guid quoteId, CancellationToken ct)
    {
        return await db.QuoteSignatureLogs
            .Where(l => l.QuoteId == quoteId)
            .OrderBy(l => l.Timestamp)
            .ToListAsync(ct);
    }

    private static QuoteESignDetailsDto MapDetails(Quote q, List<QuoteSignatureLog> logs)
    {
        return new QuoteESignDetailsDto
        {
            QuoteId = q.Id,
            QuoteNumber = q.QuoteNumber,
            Title = q.Title,
            DealId = q.DealId,
            DealTitle = q.Deal?.Name,
            AccountId = q.AccountId,
            AccountName = q.Account?.Name,
            ContactId = q.ContactId,
            ContactName = q.Contact != null ? $"{q.Contact.FirstName} {q.Contact.LastName}".Trim() : null,
            SubTotal = q.SubTotal,
            DiscountAmount = q.DiscountAmount,
            TaxAmount = q.TaxAmount,
            GrandTotal = q.GrandTotal,
            Currency = q.Currency,
            IssueDate = q.IssueDate,
            ExpirationDate = q.ExpirationDate,
            TermsAndConditions = q.TermsAndConditions,
            Notes = q.Notes,
            ESignStatus = q.ESignStatus,
            SignerEmail = q.SignerEmail,
            SignerName = q.SignerName,
            SignerTitle = q.SignerTitle,
            SigningToken = q.SigningToken,
            SignedAt = q.SignedAt,
            SignatureData = q.SignatureData,
            SignerIpAddress = q.SignerIpAddress,
            SigningExpiresAt = q.SigningExpiresAt,
            DeclineReason = q.DeclineReason,
            SecurityHash = q.SecurityHash,
            SigningUrl = q.SigningToken != null ? $"/public/quotes/sign/{q.SigningToken}" : null,
            LineItems = q.LineItems.Where(li => !li.IsDeleted).OrderBy(li => li.SortOrder).Select(li => new QuoteLineItemDto
            {
                Id = li.Id,
                ProductId = li.ProductId,
                ProductName = li.ProductName,
                Description = li.Description,
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = li.DiscountPercent,
                DiscountAmount = li.DiscountAmount,
                TaxRate = li.TaxRate,
                SubTotal = li.SubTotal,
                TotalAmount = li.TotalAmount,
                SortOrder = li.SortOrder
            }).ToList(),
            AuditTrail = logs.Select(MapLogDto).ToList()
        };
    }

    private static QuoteSignatureLogDto MapLogDto(QuoteSignatureLog l) => new()
    {
        Id = l.Id,
        QuoteId = l.QuoteId,
        Action = l.Action,
        ActorEmail = l.ActorEmail,
        ActorName = l.ActorName,
        IpAddress = l.IpAddress,
        UserAgent = l.UserAgent,
        Timestamp = l.Timestamp,
        Details = l.Details
    };
}
