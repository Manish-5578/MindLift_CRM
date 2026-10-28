using System.Security.Cryptography;
using System.Text.Json;
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
/// Phase 3 / #20: Online Payment Links (payment-links).
/// Service for generating hosted checkout payment links for quotes and invoices,
/// processing online payments, partial payments, and idempotent webhook handling.
/// </summary>
public sealed class PaymentLinkService(
    AppDbContext db,
    ITenantContext tenant,
    IFeatureFlags featureFlags,
    ILogger<PaymentLinkService> logger) : IPaymentLinkService
{
    public async Task<PaymentLinkDto> CreatePaymentLinkAsync(CreatePaymentLinkRequest req, CancellationToken ct = default)
    {
        var title = req.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new ValidationException("Title", "Payment link title is required.");

        Invoice? invoice = null;
        Quote? quote = null;

        if (req.InvoiceId.HasValue)
        {
            invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == req.InvoiceId.Value, ct)
                ?? throw new NotFoundException("Invoice", req.InvoiceId.Value);
        }

        if (req.QuoteId.HasValue)
        {
            quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId.Value, ct)
                ?? throw new NotFoundException("Quote", req.QuoteId.Value);
        }

        var amount = req.Amount;
        if (amount <= 0)
        {
            if (invoice != null)
                amount = invoice.BalanceDue > 0 ? invoice.BalanceDue : invoice.GrandTotal;
            else if (quote != null)
                amount = quote.GrandTotal;
            else
                throw new ValidationException("Amount", "A valid positive payment amount is required.");
        }

        var currency = string.IsNullOrWhiteSpace(req.Currency)
            ? (invoice?.Currency ?? quote?.Currency ?? "USD")
            : req.Currency.Trim().ToUpperInvariant();

        var token = "pay_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        var linkNumber = "PL-" + DateTime.UtcNow.ToString("yyyyMM") + "-" + RandomNumberGenerator.GetHexString(3).ToUpperInvariant();
        var expiresDays = req.ExpiresInDays ?? 14;

        var link = new PaymentLink
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            LinkNumber = linkNumber,
            Title = title,
            Description = req.Description?.Trim(),
            Currency = currency,
            Amount = amount,
            PaidAmount = 0m,
            Status = "Active",
            InvoiceId = invoice?.Id,
            QuoteId = quote?.Id,
            CustomerEmail = req.CustomerEmail?.Trim() ?? invoice?.Contact?.NormalizedEmail,
            CustomerName = req.CustomerName?.Trim() ?? (invoice?.Contact != null ? $"{invoice.Contact.FirstName} {invoice.Contact.LastName}".Trim() : null),
            PaymentToken = token,
            GatewayProvider = string.IsNullOrWhiteSpace(req.GatewayProvider) ? "Hosted" : req.GatewayProvider.Trim(),
            ExpiresAt = DateTime.UtcNow.AddDays(expiresDays),
            AllowPartial = req.AllowPartial,
            CreatedAt = DateTime.UtcNow
        };

        db.PaymentLinks.Add(link);
        await db.SaveChangesAsync(ct);

        return MapDto(link, invoice, quote);
    }

    public async Task<PaymentLinkDto> GetPaymentLinkByIdAsync(Guid id, CancellationToken ct = default)
    {
        var link = await db.PaymentLinks
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .FirstOrDefaultAsync(pl => pl.Id == id, ct)
            ?? throw new NotFoundException("PaymentLink", id);

        return MapDto(link, link.Invoice, link.Quote);
    }

    public async Task<IReadOnlyList<PaymentLinkDto>> ListPaymentLinksAsync(Guid? invoiceId = null, Guid? quoteId = null, CancellationToken ct = default)
    {
        var query = db.PaymentLinks
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .AsNoTracking()
            .AsQueryable();

        if (invoiceId.HasValue) query = query.Where(pl => pl.InvoiceId == invoiceId.Value);
        if (quoteId.HasValue) query = query.Where(pl => pl.QuoteId == quoteId.Value);

        var list = await query.OrderByDescending(pl => pl.CreatedAt).ToListAsync(ct);
        return list.Select(pl => MapDto(pl, pl.Invoice, pl.Quote)).ToList();
    }

    public async Task<PaymentLinkDto> CancelPaymentLinkAsync(Guid id, CancellationToken ct = default)
    {
        var link = await db.PaymentLinks
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .FirstOrDefaultAsync(pl => pl.Id == id, ct)
            ?? throw new NotFoundException("PaymentLink", id);

        if (link.Status == "Paid")
            throw new ValidationException("Status", "Cannot cancel a payment link that has already been paid in full.");

        link.Status = "Cancelled";
        link.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapDto(link, link.Invoice, link.Quote);
    }

    public async Task<PublicCheckoutDto> GetPublicCheckoutAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new NotFoundException("PaymentToken", token ?? string.Empty);

        var link = await db.PaymentLinks
            .IgnoreQueryFilters()
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .FirstOrDefaultAsync(pl => pl.PaymentToken == token && !pl.IsDeleted, ct)
            ?? throw new NotFoundException("PaymentToken", token);

        using (tenant.Use(link.TenantId))
        {
            if (!await featureFlags.IsEnabledAsync("payment-links", ct))
                throw new NotFoundException("Feature", "payment-links");

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow && link.Status == "Active")
            {
                link.Status = "Expired";
                link.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            var remaining = Math.Max(0, link.Amount - link.PaidAmount);

            return new PublicCheckoutDto
            {
                Id = link.Id,
                LinkNumber = link.LinkNumber,
                Title = link.Title,
                Description = link.Description,
                Currency = link.Currency,
                Amount = link.Amount,
                PaidAmount = link.PaidAmount,
                RemainingAmount = remaining,
                Status = link.Status,
                CustomerEmail = link.CustomerEmail,
                CustomerName = link.CustomerName,
                PaymentToken = link.PaymentToken,
                GatewayProvider = link.GatewayProvider,
                ExpiresAt = link.ExpiresAt,
                AllowPartial = link.AllowPartial,
                InvoiceNumber = link.Invoice?.InvoiceNumber,
                QuoteNumber = link.Quote?.QuoteNumber
            };
        }
    }

    public async Task<PaymentReceiptDto> ProcessPublicPaymentAsync(ProcessPaymentRequest req, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.PaymentToken))
            throw new ValidationException("PaymentToken", "Payment token is required.");

        if (req.Amount <= 0)
            throw new ValidationException("Amount", "Payment amount must be greater than zero.");

        var link = await db.PaymentLinks
            .IgnoreQueryFilters()
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .FirstOrDefaultAsync(pl => pl.PaymentToken == req.PaymentToken && !pl.IsDeleted, ct)
            ?? throw new NotFoundException("PaymentToken", req.PaymentToken);

        using (tenant.Use(link.TenantId))
        {
            if (!await featureFlags.IsEnabledAsync("payment-links", ct))
                throw new NotFoundException("Feature", "payment-links");

            if (link.Status == "Paid")
                throw new ValidationException("Status", "This payment link has already been paid in full.");

            if (link.Status == "Cancelled")
                throw new ValidationException("Status", "This payment link has been cancelled.");

            if (link.ExpiresAt.HasValue && link.ExpiresAt.Value < DateTime.UtcNow)
                throw new ValidationException("Status", "This payment link has expired.");

            var remaining = link.Amount - link.PaidAmount;
            if (!link.AllowPartial && req.Amount < remaining)
                throw new ValidationException("Amount", $"Partial payments are not permitted for this link. Expected full amount: {remaining} {link.Currency}.");

            if (req.Amount > remaining)
                throw new ValidationException("Amount", $"Payment amount exceeds remaining balance of {remaining} {link.Currency}.");

            var paidAt = DateTime.UtcNow;
            link.PaidAmount += req.Amount;
            link.Status = link.PaidAmount >= link.Amount ? "Paid" : "PartiallyPaid";
            link.PaidAt = paidAt;
            link.PaymentMethodUsed = string.IsNullOrWhiteSpace(req.PaymentMethod) ? "card" : req.PaymentMethod.Trim();

            var receiptNumber = "RCT-" + paidAt.ToString("yyyyMM") + "-" + RandomNumberGenerator.GetHexString(4).ToUpperInvariant();
            link.ReceiptNumber = receiptNumber;
            link.UpdatedAt = paidAt;

            // Fulfill Invoice if attached
            if (link.Invoice != null)
            {
                link.Invoice.PaidAmount += req.Amount;
                link.Invoice.BalanceDue = Math.Max(0, link.Invoice.GrandTotal - link.Invoice.PaidAmount);
                link.Invoice.Status = link.Invoice.BalanceDue <= 0 ? (int)InvoiceStatus.Paid : (int)InvoiceStatus.PartiallyPaid;
                link.Invoice.UpdatedAt = paidAt;

                var paymentRec = new PaymentRecord
                {
                    TenantId = link.TenantId,
                    InvoiceId = link.Invoice.Id,
                    Amount = req.Amount,
                    PaymentDate = paidAt,
                    Method = string.Equals(req.PaymentMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                    ReferenceNumber = req.Reference ?? receiptNumber,
                    Notes = $"Paid online via payment link {link.LinkNumber}. Provider: {link.GatewayProvider}"
                };
                db.Payments.Add(paymentRec);
            }

            // Fulfill Quote if attached
            if (link.Quote != null)
            {
                if (link.Quote.Status != (int)QuoteStatus.Accepted)
                {
                    link.Quote.Status = (int)QuoteStatus.Accepted;
                    link.Quote.UpdatedAt = paidAt;
                }
            }

            await db.SaveChangesAsync(ct);

            return new PaymentReceiptDto
            {
                ReceiptNumber = receiptNumber,
                PaymentLinkId = link.Id,
                LinkNumber = link.LinkNumber,
                AmountPaid = req.Amount,
                TotalAmount = link.Amount,
                RemainingBalance = Math.Max(0, link.Amount - link.PaidAmount),
                Currency = link.Currency,
                Status = link.Status,
                PaidAt = paidAt,
                PaymentMethodUsed = link.PaymentMethodUsed,
                TransactionId = req.Reference ?? receiptNumber
            };
        }
    }

    public async Task<bool> HandleWebhookAsync(string provider, string payload, string? signature, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(payload)) return false;

        WebhookEventPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WebhookEventPayload>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return false;
        }

        if (parsed == null || string.IsNullOrWhiteSpace(parsed.EventId)) return false;

        // Idempotency check: check if event already handled
        var alreadyProcessed = await db.PaymentWebhookLogs
            .IgnoreQueryFilters()
            .AnyAsync(w => w.EventId == parsed.EventId && w.Provider == provider, ct);

        if (alreadyProcessed)
        {
            logger.LogInformation("Webhook event {EventId} from provider {Provider} was already processed.", parsed.EventId, provider);
            return true;
        }

        var link = await db.PaymentLinks
            .IgnoreQueryFilters()
            .Include(pl => pl.Invoice)
            .Include(pl => pl.Quote)
            .FirstOrDefaultAsync(pl => pl.PaymentToken == parsed.PaymentToken && !pl.IsDeleted, ct);

        if (link == null)
        {
            return false;
        }

        using (tenant.Use(link.TenantId))
        {
            var log = new PaymentWebhookLog
            {
                TenantId = link.TenantId,
                Provider = provider,
                EventId = parsed.EventId,
                EventType = parsed.EventType,
                PayloadJson = payload,
                Status = "Processed",
                ReceivedAt = DateTime.UtcNow
            };

            if (parsed.EventType.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ||
                parsed.EventType.Contains("paid", StringComparison.OrdinalIgnoreCase))
            {
                var payReq = new ProcessPaymentRequest
                {
                    PaymentToken = parsed.PaymentToken,
                    Amount = parsed.Amount > 0 ? parsed.Amount : link.Amount - link.PaidAmount,
                    PaymentMethod = parsed.PaymentMethod ?? "online",
                    Reference = parsed.TransactionReference ?? parsed.EventId
                };

                await ProcessPublicPaymentAsync(payReq, "webhook", ct);
            }

            db.PaymentWebhookLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return true;
        }
    }

    private static PaymentLinkDto MapDto(PaymentLink pl, Invoice? inv, Quote? q)
    {
        return new PaymentLinkDto
        {
            Id = pl.Id,
            LinkNumber = pl.LinkNumber,
            Title = pl.Title,
            Description = pl.Description,
            Currency = pl.Currency,
            Amount = pl.Amount,
            PaidAmount = pl.PaidAmount,
            Status = pl.Status,
            InvoiceId = pl.InvoiceId,
            InvoiceNumber = inv?.InvoiceNumber,
            QuoteId = pl.QuoteId,
            QuoteNumber = q?.QuoteNumber,
            CustomerEmail = pl.CustomerEmail,
            CustomerName = pl.CustomerName,
            PaymentToken = pl.PaymentToken,
            CheckoutUrl = $"/public/pay/{pl.PaymentToken}",
            GatewayProvider = pl.GatewayProvider,
            GatewayReference = pl.GatewayReference,
            ExpiresAt = pl.ExpiresAt,
            AllowPartial = pl.AllowPartial,
            PaidAt = pl.PaidAt,
            PaymentMethodUsed = pl.PaymentMethodUsed,
            ReceiptNumber = pl.ReceiptNumber,
            CreatedAt = pl.CreatedAt
        };
    }
}
