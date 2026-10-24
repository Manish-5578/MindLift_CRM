using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Cpq;

public sealed class CpqService(IAppDbContext db, ITenantContext tenant, MindLift.Infrastructure.Tenancy.NumberSequences numbers) : ICpqService
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    // ================= PRODUCT CATALOG =================

    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        var q = db.Set<Product>().AsNoTracking().Where(p => !p.IsDeleted);
        if (activeOnly) q = q.Where(p => p.IsActive);

        var items = await q.OrderBy(p => p.Name).ToListAsync(ct);
        return items.Select(MapProduct).ToList();
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return p == null ? null : MapProduct(p);
    }

    public async Task<ProductDto> CreateProductAsync(SaveProductRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name", "Product name is required.");
        if (string.IsNullOrWhiteSpace(req.Sku)) throw new ValidationException("Sku", "SKU is required.");

        var existing = await db.Set<Product>().AnyAsync(p => p.Sku == req.Sku.Trim() && !p.IsDeleted, ct);
        if (existing) throw new ValidationException("Sku", $"A product with SKU '{req.Sku}' already exists.");

        var p = new Product
        {
            TenantId = CurrentTenantId,
            Name = req.Name.Trim(),
            Sku = req.Sku.Trim(),
            Description = req.Description?.Trim(),
            Category = string.IsNullOrWhiteSpace(req.Category) ? "General" : req.Category.Trim(),
            Type = (int)req.Type,
            UnitPrice = req.UnitPrice,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant(),
            TaxRate = req.TaxRate,
            BillingFrequency = (int)req.BillingFrequency,
            IsActive = req.IsActive,
            StockQuantity = req.StockQuantity,
            CreatedAt = DateTime.UtcNow
        };

        db.Set<Product>().Add(p);
        await db.SaveChangesAsync(ct);
        return MapProduct(p);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid id, SaveProductRequest req, CancellationToken ct = default)
    {
        var p = await db.Set<Product>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Product", id);

        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name", "Product name is required.");
        if (string.IsNullOrWhiteSpace(req.Sku)) throw new ValidationException("Sku", "SKU is required.");

        var existing = await db.Set<Product>().AnyAsync(x => x.Id != id && x.Sku == req.Sku.Trim() && !x.IsDeleted, ct);
        if (existing) throw new ValidationException("Sku", $"A product with SKU '{req.Sku}' already exists.");

        p.Name = req.Name.Trim();
        p.Sku = req.Sku.Trim();
        p.Description = req.Description?.Trim();
        p.Category = string.IsNullOrWhiteSpace(req.Category) ? "General" : req.Category.Trim();
        p.Type = (int)req.Type;
        p.UnitPrice = req.UnitPrice;
        p.Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant();
        p.TaxRate = req.TaxRate;
        p.BillingFrequency = (int)req.BillingFrequency;
        p.IsActive = req.IsActive;
        p.StockQuantity = req.StockQuantity;
        p.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapProduct(p);
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Set<Product>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (p == null) return false;

        p.IsDeleted = true;
        p.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= PRICE BOOKS =================

    public async Task<IReadOnlyList<PriceBookDto>> GetPriceBooksAsync(CancellationToken ct = default)
    {
        var list = await db.Set<PriceBook>()
            .AsNoTracking()
            .Include(pb => pb.Entries.Where(e => !e.IsDeleted))
                .ThenInclude(e => e.Product)
            .Where(pb => !pb.IsDeleted)
            .OrderByDescending(pb => pb.IsStandard)
            .ThenBy(pb => pb.Name)
            .ToListAsync(ct);

        return list.Select(MapPriceBook).ToList();
    }

    public async Task<PriceBookDto?> GetPriceBookByIdAsync(Guid id, CancellationToken ct = default)
    {
        var pb = await db.Set<PriceBook>()
            .AsNoTracking()
            .Include(pb => pb.Entries.Where(e => !e.IsDeleted))
                .ThenInclude(e => e.Product)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

        return pb == null ? null : MapPriceBook(pb);
    }

    public async Task<PriceBookDto> CreatePriceBookAsync(SavePriceBookRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) throw new ValidationException("Name", "Price book name is required.");

        var pb = new PriceBook
        {
            TenantId = CurrentTenantId,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            IsStandard = req.IsStandard,
            IsActive = req.IsActive,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow
        };

        db.Set<PriceBook>().Add(pb);
        await db.SaveChangesAsync(ct);
        return MapPriceBook(pb);
    }

    public async Task<PriceBookEntryDto> SetPriceBookEntryAsync(Guid priceBookId, SavePriceBookEntryRequest req, CancellationToken ct = default)
    {
        var pb = await db.Set<PriceBook>().FirstOrDefaultAsync(x => x.Id == priceBookId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("PriceBook", priceBookId);

        var prod = await db.Set<Product>().FirstOrDefaultAsync(x => x.Id == req.ProductId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Product", req.ProductId);

        var entry = await db.Set<PriceBookEntry>()
            .FirstOrDefaultAsync(e => e.PriceBookId == priceBookId && e.ProductId == req.ProductId && !e.IsDeleted, ct);

        if (entry == null)
        {
            entry = new PriceBookEntry
            {
                TenantId = CurrentTenantId,
                PriceBookId = priceBookId,
                ProductId = req.ProductId,
                ListPrice = req.UseStandardPrice ? prod.UnitPrice : req.ListPrice,
                UseStandardPrice = req.UseStandardPrice,
                IsActive = req.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            db.Set<PriceBookEntry>().Add(entry);
        }
        else
        {
            entry.ListPrice = req.UseStandardPrice ? prod.UnitPrice : req.ListPrice;
            entry.UseStandardPrice = req.UseStandardPrice;
            entry.IsActive = req.IsActive;
            entry.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new PriceBookEntryDto
        {
            Id = entry.Id,
            PriceBookId = entry.PriceBookId,
            ProductId = entry.ProductId,
            ProductName = prod.Name,
            ProductSku = prod.Sku,
            ListPrice = entry.ListPrice,
            UseStandardPrice = entry.UseStandardPrice,
            IsActive = entry.IsActive
        };
    }

    // ================= QUOTES =================

    public async Task<IReadOnlyList<QuoteDto>> GetQuotesAsync(Guid? dealId = null, Guid? accountId = null, CancellationToken ct = default)
    {
        var q = db.Set<Quote>()
            .AsNoTracking()
            .Include(x => x.Deal)
            .Include(x => x.Account)
            .Include(x => x.Contact)
            .Include(x => x.LineItems.Where(li => !li.IsDeleted))
            .Where(x => !x.IsDeleted);

        if (dealId.HasValue) q = q.Where(x => x.DealId == dealId.Value);
        if (accountId.HasValue) q = q.Where(x => x.AccountId == accountId.Value);

        var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return list.Select(MapQuote).ToList();
    }

    public async Task<QuoteDto?> GetQuoteByIdAsync(Guid id, CancellationToken ct = default)
    {
        var q = await db.Set<Quote>()
            .AsNoTracking()
            .Include(x => x.Deal)
            .Include(x => x.Account)
            .Include(x => x.Contact)
            .Include(x => x.LineItems.Where(li => !li.IsDeleted))
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

        return q == null ? null : MapQuote(q);
    }

    public async Task<QuoteDto> CreateQuoteAsync(SaveQuoteRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) throw new ValidationException("Title", "Quote title is required.");

        var quoteNumber = $"Q-{await numbers.NextAsync(MindLift.Infrastructure.Tenancy.NumberSequences.Quote, async () => 10001 + await db.Set<Quote>().IgnoreQueryFilters().CountAsync(x => x.TenantId == CurrentTenantId, ct), ct)}";

        var quote = new Quote
        {
            TenantId = CurrentTenantId,
            QuoteNumber = quoteNumber,
            Title = req.Title.Trim(),
            DealId = req.DealId,
            AccountId = req.AccountId,
            ContactId = req.ContactId,
            Status = (int)req.Status,
            IssueDate = DateTime.SpecifyKind(req.IssueDate == default ? DateTime.UtcNow : req.IssueDate, DateTimeKind.Utc),
            ExpirationDate = req.ExpirationDate.HasValue ? DateTime.SpecifyKind(req.ExpirationDate.Value, DateTimeKind.Utc) : null,
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant(),
            DiscountPercent = req.DiscountPercent,
            TermsAndConditions = req.TermsAndConditions?.Trim(),
            Notes = req.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        RecalculateQuoteTotals(quote, req.LineItems);

        db.Set<Quote>().Add(quote);
        await db.SaveChangesAsync(ct);
        return MapQuote(quote);
    }

    public async Task<QuoteDto> UpdateQuoteAsync(Guid id, SaveQuoteRequest req, CancellationToken ct = default)
    {
        var quote = await db.Set<Quote>()
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Quote", id);

        if (string.IsNullOrWhiteSpace(req.Title)) throw new ValidationException("Title", "Quote title is required.");

        quote.Title = req.Title.Trim();
        quote.DealId = req.DealId;
        quote.AccountId = req.AccountId;
        quote.ContactId = req.ContactId;
        quote.Status = (int)req.Status;
        quote.IssueDate = DateTime.SpecifyKind(req.IssueDate == default ? DateTime.UtcNow : req.IssueDate, DateTimeKind.Utc);
        quote.ExpirationDate = req.ExpirationDate.HasValue ? DateTime.SpecifyKind(req.ExpirationDate.Value, DateTimeKind.Utc) : null;
        quote.Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant();
        quote.DiscountPercent = req.DiscountPercent;
        quote.TermsAndConditions = req.TermsAndConditions?.Trim();
        quote.Notes = req.Notes?.Trim();
        quote.UpdatedAt = DateTime.UtcNow;

        // Clear existing line items and recreate
        quote.LineItems.Clear();
        RecalculateQuoteTotals(quote, req.LineItems);

        await db.SaveChangesAsync(ct);
        return MapQuote(quote);
    }

    public async Task<bool> DeleteQuoteAsync(Guid id, CancellationToken ct = default)
    {
        var quote = await db.Set<Quote>().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (quote == null) return false;

        quote.IsDeleted = true;
        quote.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<QuoteDto> SyncQuoteToDealAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await db.Set<Quote>()
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(x => x.Id == quoteId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        if (!quote.DealId.HasValue)
        {
            throw new ValidationException("DealId", "This quote is not associated with any deal.");
        }

        var deal = await db.Set<Deal>().FirstOrDefaultAsync(d => d.Id == quote.DealId.Value && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Deal", quote.DealId.Value);

        // Mark all quotes for this deal as not synced, and this one as synced
        var otherQuotes = await db.Set<Quote>().Where(q => q.DealId == deal.Id && !q.IsDeleted).ToListAsync(ct);
        foreach (var o in otherQuotes) o.IsSyncedToDeal = false;

        quote.IsSyncedToDeal = true;
        deal.Amount = quote.GrandTotal;
        deal.Currency = quote.Currency;
        deal.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapQuote(quote);
    }

    public async Task<QuoteDto> UpdateQuoteStatusAsync(Guid quoteId, QuoteStatus status, CancellationToken ct = default)
    {
        var quote = await db.Set<Quote>()
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(x => x.Id == quoteId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        quote.Status = (int)status;
        quote.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapQuote(quote);
    }

    public async Task<InvoiceDto> ConvertQuoteToInvoiceAsync(Guid quoteId, CancellationToken ct = default)
    {
        var quote = await db.Set<Quote>()
            .Include(q => q.LineItems)
            .FirstOrDefaultAsync(x => x.Id == quoteId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Quote", quoteId);

        var invoiceNumber = await NextInvoiceNumberAsync(ct);

        var invoice = new Invoice
        {
            TenantId = CurrentTenantId,
            InvoiceNumber = invoiceNumber,
            QuoteId = quote.Id,
            DealId = quote.DealId,
            AccountId = quote.AccountId,
            ContactId = quote.ContactId,
            Status = (int)InvoiceStatus.Draft,
            IssueDate = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(30),
            Currency = quote.Currency,
            SubTotal = quote.SubTotal,
            DiscountAmount = quote.DiscountAmount,
            TaxAmount = quote.TaxAmount,
            GrandTotal = quote.GrandTotal,
            BalanceDue = quote.GrandTotal,
            PaidAmount = 0,
            PaymentTerms = "Net 30",
            Notes = $"Generated from Quote {quote.QuoteNumber}",
            CreatedAt = DateTime.UtcNow
        };

        foreach (var qli in quote.LineItems)
        {
            invoice.LineItems.Add(new InvoiceLineItem
            {
                TenantId = CurrentTenantId,
                ProductId = qli.ProductId,
                ProductName = qli.ProductName,
                Description = qli.Description,
                Quantity = qli.Quantity,
                UnitPrice = qli.UnitPrice,
                TaxRate = qli.TaxRate,
                TotalAmount = qli.TotalAmount,
                SortOrder = qli.SortOrder,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.Set<Invoice>().Add(invoice);
        await db.SaveChangesAsync(ct);
        return MapInvoice(invoice);
    }

    // ================= INVOICES & PAYMENTS =================

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(Guid? dealId = null, Guid? accountId = null, CancellationToken ct = default)
    {
        var q = db.Set<Invoice>()
            .AsNoTracking()
            .Include(x => x.Quote)
            .Include(x => x.Deal)
            .Include(x => x.Account)
            .Include(x => x.Contact)
            .Include(x => x.LineItems.Where(li => !li.IsDeleted))
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .Where(x => !x.IsDeleted);

        if (dealId.HasValue) q = q.Where(x => x.DealId == dealId.Value);
        if (accountId.HasValue) q = q.Where(x => x.AccountId == accountId.Value);

        var list = await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return list.Select(MapInvoice).ToList();
    }

    public async Task<InvoiceDto?> GetInvoiceByIdAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await db.Set<Invoice>()
            .AsNoTracking()
            .Include(x => x.Quote)
            .Include(x => x.Deal)
            .Include(x => x.Account)
            .Include(x => x.Contact)
            .Include(x => x.LineItems.Where(li => !li.IsDeleted))
            .Include(x => x.Payments.Where(p => !p.IsDeleted))
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

        return inv == null ? null : MapInvoice(inv);
    }

    private async Task<string> NextInvoiceNumberAsync(CancellationToken ct)
        => $"INV-{await numbers.NextAsync(MindLift.Infrastructure.Tenancy.NumberSequences.Invoice, async () => 10001 + await db.Set<Invoice>().IgnoreQueryFilters().CountAsync(x => x.TenantId == CurrentTenantId, ct), ct)}";

    public async Task<InvoiceDto> CreateInvoiceAsync(SaveInvoiceRequest req, CancellationToken ct = default)
    {
        var invoiceNumber = await NextInvoiceNumberAsync(ct);

        var inv = new Invoice
        {
            TenantId = CurrentTenantId,
            InvoiceNumber = invoiceNumber,
            QuoteId = req.QuoteId,
            DealId = req.DealId,
            AccountId = req.AccountId,
            ContactId = req.ContactId,
            Status = (int)req.Status,
            IssueDate = DateTime.SpecifyKind(req.IssueDate == default ? DateTime.UtcNow : req.IssueDate, DateTimeKind.Utc),
            DueDate = DateTime.SpecifyKind(req.DueDate == default ? DateTime.UtcNow.AddDays(30) : req.DueDate, DateTimeKind.Utc),
            Currency = string.IsNullOrWhiteSpace(req.Currency) ? "USD" : req.Currency.ToUpperInvariant(),
            DiscountAmount = req.DiscountAmount,
            PaymentTerms = req.PaymentTerms?.Trim(),
            Notes = req.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        decimal subTotal = 0;
        decimal taxAmount = 0;
        int idx = 0;

        foreach (var li in req.LineItems)
        {
            var lineSub = li.Quantity * li.UnitPrice;
            var lineTax = lineSub * (li.TaxRate / 100m);
            var lineTotal = lineSub + lineTax;

            subTotal += lineSub;
            taxAmount += lineTax;

            inv.LineItems.Add(new InvoiceLineItem
            {
                TenantId = CurrentTenantId,
                ProductId = li.ProductId,
                ProductName = string.IsNullOrWhiteSpace(li.ProductName) ? "Item" : li.ProductName.Trim(),
                Description = li.Description?.Trim(),
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                TaxRate = li.TaxRate,
                TotalAmount = lineTotal,
                SortOrder = idx++,
                CreatedAt = DateTime.UtcNow
            });
        }

        inv.SubTotal = subTotal;
        inv.TaxAmount = taxAmount;
        inv.GrandTotal = Math.Max(0m, (subTotal - req.DiscountAmount) + taxAmount);
        inv.BalanceDue = inv.GrandTotal;
        inv.PaidAmount = 0;

        db.Set<Invoice>().Add(inv);
        await db.SaveChangesAsync(ct);
        return MapInvoice(inv);
    }

    public async Task<PaymentDto> RecordPaymentAsync(Guid invoiceId, RecordPaymentRequest req, CancellationToken ct = default)
    {
        var inv = await db.Set<Invoice>()
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(x => x.Id == invoiceId && !x.IsDeleted, ct)
            ?? throw new NotFoundException("Invoice", invoiceId);

        if (req.Amount <= 0) throw new ValidationException("Amount", "Payment amount must be greater than zero.");

        var payment = new PaymentRecord
        {
            TenantId = CurrentTenantId,
            InvoiceId = invoiceId,
            Amount = req.Amount,
            PaymentDate = DateTime.SpecifyKind(req.PaymentDate == default ? DateTime.UtcNow : req.PaymentDate, DateTimeKind.Utc),
            Method = (int)req.Method,
            ReferenceNumber = req.ReferenceNumber?.Trim(),
            Notes = req.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        db.Set<PaymentRecord>().Add(payment);
        inv.Payments.Add(payment);
        inv.PaidAmount += req.Amount;
        inv.BalanceDue = Math.Max(0m, inv.GrandTotal - inv.PaidAmount);

        if (inv.BalanceDue == 0)
        {
            inv.Status = (int)InvoiceStatus.Paid;
        }
        else if (inv.PaidAmount > 0)
        {
            inv.Status = (int)InvoiceStatus.PartiallyPaid;
        }

        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new PaymentDto
        {
            Id = payment.Id,
            InvoiceId = payment.InvoiceId,
            Amount = payment.Amount,
            PaymentDate = payment.PaymentDate,
            Method = (PaymentMethod)payment.Method,
            ReferenceNumber = payment.ReferenceNumber,
            Notes = payment.Notes,
            CreatedAt = payment.CreatedAt
        };
    }

    public async Task<bool> CancelInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await db.Set<Invoice>().FirstOrDefaultAsync(x => x.Id == invoiceId && !x.IsDeleted, ct);
        if (inv == null) return false;

        inv.Status = (int)InvoiceStatus.Cancelled;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= PRIVATE HELPERS =================

    private static void RecalculateQuoteTotals(Quote quote, List<SaveQuoteLineItemRequest> lineItems)
    {
        decimal subTotal = 0;
        decimal lineDiscountTotal = 0;
        decimal taxAmount = 0;
        int idx = 0;

        foreach (var li in lineItems)
        {
            var lineSub = li.Quantity * li.UnitPrice;
            var lineDisc = lineSub * (li.DiscountPercent / 100m);
            var netLine = lineSub - lineDisc;
            var lineTax = netLine * (li.TaxRate / 100m);
            var lineTotal = netLine + lineTax;

            subTotal += lineSub;
            lineDiscountTotal += lineDisc;
            taxAmount += lineTax;

            quote.LineItems.Add(new QuoteLineItem
            {
                TenantId = quote.TenantId,
                ProductId = li.ProductId,
                ProductName = string.IsNullOrWhiteSpace(li.ProductName) ? "Item" : li.ProductName.Trim(),
                Description = li.Description?.Trim(),
                Quantity = li.Quantity,
                UnitPrice = li.UnitPrice,
                DiscountPercent = li.DiscountPercent,
                DiscountAmount = lineDisc,
                TaxRate = li.TaxRate,
                SubTotal = lineSub,
                TotalAmount = lineTotal,
                SortOrder = idx++,
                CreatedAt = DateTime.UtcNow
            });
        }

        quote.SubTotal = subTotal;
        var headerDisc = (subTotal - lineDiscountTotal) * (quote.DiscountPercent / 100m);
        quote.DiscountAmount = lineDiscountTotal + headerDisc;
        quote.TaxAmount = taxAmount;
        quote.GrandTotal = Math.Max(0m, (subTotal - quote.DiscountAmount) + taxAmount);
    }

    private static ProductDto MapProduct(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Sku = p.Sku,
        Description = p.Description,
        Category = p.Category,
        Type = (ProductType)p.Type,
        UnitPrice = p.UnitPrice,
        Currency = p.Currency,
        TaxRate = p.TaxRate,
        BillingFrequency = (BillingFrequency)p.BillingFrequency,
        IsActive = p.IsActive,
        StockQuantity = p.StockQuantity,
        CreatedAt = p.CreatedAt
    };

    private static PriceBookDto MapPriceBook(PriceBook pb) => new()
    {
        Id = pb.Id,
        Name = pb.Name,
        Description = pb.Description,
        IsStandard = pb.IsStandard,
        IsActive = pb.IsActive,
        Currency = pb.Currency,
        EntryCount = pb.Entries.Count(e => !e.IsDeleted),
        Entries = pb.Entries.Where(e => !e.IsDeleted).Select(e => new PriceBookEntryDto
        {
            Id = e.Id,
            PriceBookId = e.PriceBookId,
            ProductId = e.ProductId,
            ProductName = e.Product?.Name ?? "",
            ProductSku = e.Product?.Sku ?? "",
            ListPrice = e.ListPrice,
            UseStandardPrice = e.UseStandardPrice,
            IsActive = e.IsActive
        }).ToList()
    };

    private static QuoteDto MapQuote(Quote q) => new()
    {
        Id = q.Id,
        QuoteNumber = q.QuoteNumber,
        Title = q.Title,
        DealId = q.DealId,
        DealTitle = q.Deal?.Name,
        AccountId = q.AccountId,
        AccountName = q.Account?.Name,
        ContactId = q.ContactId,
        ContactName = q.Contact != null ? $"{q.Contact.FirstName} {q.Contact.LastName}".Trim() : null,
        Status = (QuoteStatus)q.Status,
        IssueDate = q.IssueDate,
        ExpirationDate = q.ExpirationDate,
        Currency = q.Currency,
        SubTotal = q.SubTotal,
        DiscountPercent = q.DiscountPercent,
        DiscountAmount = q.DiscountAmount,
        TaxAmount = q.TaxAmount,
        GrandTotal = q.GrandTotal,
        TermsAndConditions = q.TermsAndConditions,
        Notes = q.Notes,
        IsSyncedToDeal = q.IsSyncedToDeal,
        CreatedAt = q.CreatedAt,
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
        }).ToList()
    };

    private static InvoiceDto MapInvoice(Invoice i) => new()
    {
        Id = i.Id,
        InvoiceNumber = i.InvoiceNumber,
        QuoteId = i.QuoteId,
        QuoteNumber = i.Quote?.QuoteNumber,
        DealId = i.DealId,
        DealTitle = i.Deal?.Name,
        AccountId = i.AccountId,
        AccountName = i.Account?.Name,
        ContactId = i.ContactId,
        ContactName = i.Contact != null ? $"{i.Contact.FirstName} {i.Contact.LastName}".Trim() : null,
        Status = (InvoiceStatus)i.Status,
        IssueDate = i.IssueDate,
        DueDate = i.DueDate,
        Currency = i.Currency,
        SubTotal = i.SubTotal,
        DiscountAmount = i.DiscountAmount,
        TaxAmount = i.TaxAmount,
        GrandTotal = i.GrandTotal,
        PaidAmount = i.PaidAmount,
        BalanceDue = i.BalanceDue,
        PaymentTerms = i.PaymentTerms,
        Notes = i.Notes,
        CreatedAt = i.CreatedAt,
        LineItems = i.LineItems.Where(li => !li.IsDeleted).OrderBy(li => li.SortOrder).Select(li => new InvoiceLineItemDto
        {
            Id = li.Id,
            ProductId = li.ProductId,
            ProductName = li.ProductName,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            TaxRate = li.TaxRate,
            TotalAmount = li.TotalAmount,
            SortOrder = li.SortOrder
        }).ToList(),
        Payments = i.Payments.Where(p => !p.IsDeleted).OrderBy(p => p.PaymentDate).Select(p => new PaymentDto
        {
            Id = p.Id,
            InvoiceId = p.InvoiceId,
            Amount = p.Amount,
            PaymentDate = p.PaymentDate,
            Method = (PaymentMethod)p.Method,
            ReferenceNumber = p.ReferenceNumber,
            Notes = p.Notes,
            CreatedAt = p.CreatedAt
        }).ToList()
    };
}
