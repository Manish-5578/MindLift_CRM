using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Manages product and custom line items for Deals.
/// Computes subtotals, discounts, taxes and grand totals strictly on the server,
/// and synchronizes the deal's Amount when requested.
/// </summary>
public sealed class DealLineItemService(
    AppDbContext db,
    IDataScopeService scope,
    ITimelineEventBus timeline,
    ITenantContext tenant)
{
    private const int MaxItemsPerDeal = 100;
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 1000;

    public async Task<DealLineItemsSummaryDto> GetSummaryAsync(Guid dealId, CancellationToken ct = default)
    {
        var deal = await LoadScopedDealAsync(dealId, ct);
        var items = await db.DealLineItems
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.PriceBook)
            .Where(x => x.DealId == dealId)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(ct);

        var dtos = items.Select(x => new DealLineItemDto(
            x.Id,
            x.DealId,
            x.ProductId,
            x.Product?.Sku,
            x.PriceBookId,
            x.PriceBook?.Name,
            x.ProductName,
            x.Description,
            x.Quantity,
            x.UnitPrice,
            x.DiscountPercent,
            x.DiscountAmount,
            x.TaxRate,
            x.SubTotal,
            x.TotalAmount,
            x.SortOrder
        )).ToList();

        var subTotal = dtos.Sum(i => i.SubTotal);
        var totalDiscount = dtos.Sum(i => i.DiscountAmount);
        var totalTax = dtos.Sum(i => Math.Max(0m, i.SubTotal - i.DiscountAmount) * (i.TaxRate / 100m));
        totalTax = Math.Round(totalTax, 4, MidpointRounding.AwayFromZero);
        var grandTotal = dtos.Sum(i => i.TotalAmount);

        return new DealLineItemsSummaryDto(dtos, subTotal, totalDiscount, totalTax, grandTotal, deal.Currency);
    }

    public async Task<DealLineItemsSummaryDto> SetLineItemsAsync(Guid dealId, SetDealLineItemsRequest request, CancellationToken ct = default)
    {
        var deal = await LoadScopedDealAsync(dealId, ct);

        if (request.Items == null)
        {
            throw new ValidationException("items", "Line items list cannot be null.");
        }

        if (request.Items.Count > MaxItemsPerDeal)
        {
            throw new ValidationException("items", $"A deal can have at most {MaxItemsPerDeal} line items.");
        }

        var newEntities = new List<DealLineItem>();
        for (var idx = 0; idx < request.Items.Count; idx++)
        {
            var item = request.Items[idx];
            var entity = await BuildLineItemEntityAsync(deal, item, idx, ct);
            newEntities.Add(entity);
        }

        // Replace existing line items
        var existing = await db.DealLineItems.Where(x => x.DealId == dealId).ToListAsync(ct);
        db.DealLineItems.RemoveRange(existing);

        db.DealLineItems.AddRange(newEntities);

        var grandTotal = newEntities.Sum(x => x.TotalAmount);
        if (request.UpdateDealAmount)
        {
            deal.Amount = grandTotal;
        }

        await db.SaveChangesAsync(ct);

        // Record timeline event (F6)
        await timeline.PublishAsync(new TimelineEventInput(
            "deal",
            dealId,
            "line_items_updated",
            $"Updated line items ({newEntities.Count} items, total: {grandTotal} {deal.Currency})",
            "crm",
            JsonSerializer.Serialize(new
            {
                ItemCount = newEntities.Count,
                GrandTotal = grandTotal,
                Currency = deal.Currency,
                UpdatedBy = tenant.UserId
            })
        ), ct);

        return await GetSummaryAsync(dealId, ct);
    }

    public async Task<DealLineItemsSummaryDto> AddLineItemAsync(Guid dealId, SaveDealLineItemRequest request, CancellationToken ct = default)
    {
        var deal = await LoadScopedDealAsync(dealId, ct);

        var currentCount = await db.DealLineItems.CountAsync(x => x.DealId == dealId, ct);
        if (currentCount >= MaxItemsPerDeal)
        {
            throw new ValidationException("items", $"A deal can have at most {MaxItemsPerDeal} line items.");
        }

        var entity = await BuildLineItemEntityAsync(deal, request, currentCount, ct);
        db.DealLineItems.Add(entity);

        deal.Amount += entity.TotalAmount;

        await db.SaveChangesAsync(ct);

        await timeline.PublishAsync(new TimelineEventInput(
            "deal",
            dealId,
            "line_item_added",
            $"Added line item '{entity.ProductName}' ({entity.TotalAmount} {deal.Currency})",
            "crm",
            JsonSerializer.Serialize(new
            {
                entity.ProductName,
                entity.Quantity,
                entity.UnitPrice,
                entity.TotalAmount,
                deal.Currency
            })
        ), ct);

        return await GetSummaryAsync(dealId, ct);
    }

    public async Task<DealLineItemsSummaryDto> DeleteLineItemAsync(Guid dealId, Guid lineItemId, CancellationToken ct = default)
    {
        var deal = await LoadScopedDealAsync(dealId, ct);

        var item = await db.DealLineItems.FirstOrDefaultAsync(x => x.Id == lineItemId && x.DealId == dealId, ct)
            ?? throw new NotFoundException("LineItem", lineItemId);

        db.DealLineItems.Remove(item);
        deal.Amount = Math.Max(0m, deal.Amount - item.TotalAmount);

        await db.SaveChangesAsync(ct);

        await timeline.PublishAsync(new TimelineEventInput(
            "deal",
            dealId,
            "line_item_removed",
            $"Removed line item '{item.ProductName}'",
            "crm",
            null
        ), ct);

        return await GetSummaryAsync(dealId, ct);
    }

    private async Task<DealLineItem> BuildLineItemEntityAsync(
        Deal deal,
        SaveDealLineItemRequest item,
        int defaultSortOrder,
        CancellationToken ct)
    {
        if (item.Quantity <= 0m || item.Quantity > 1_000_000m)
        {
            throw new ValidationException("quantity", "Quantity must be greater than zero and at most 1,000,000.");
        }

        Product? product = null;
        if (item.ProductId is { } pId)
        {
            product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pId, ct)
                ?? throw new NotFoundException("Product", pId);
        }

        PriceBook? priceBook = null;
        if (item.PriceBookId is { } pbId)
        {
            priceBook = await db.PriceBooks.AsNoTracking().FirstOrDefaultAsync(pb => pb.Id == pbId, ct)
                ?? throw new NotFoundException("PriceBook", pbId);

            if (!string.Equals(priceBook.Currency, deal.Currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("priceBookId", $"Price book currency '{priceBook.Currency}' does not match deal currency '{deal.Currency}'.");
            }
        }

        var productName = (item.ProductName ?? product?.Name)?.Trim();
        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new ValidationException("productName", "Product name is required.");
        }
        if (productName.Length > MaxNameLength)
        {
            throw new ValidationException("productName", $"Product name cannot exceed {MaxNameLength} characters.");
        }

        var description = item.Description?.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            throw new ValidationException("description", $"Description cannot exceed {MaxDescriptionLength} characters.");
        }

        // Determine unit price
        decimal unitPrice;
        if (item.UnitPrice.HasValue)
        {
            unitPrice = item.UnitPrice.Value;
        }
        else if (priceBook != null && product != null)
        {
            var entry = await db.PriceBookEntries.AsNoTracking()
                .FirstOrDefaultAsync(e => e.PriceBookId == priceBook.Id && e.ProductId == product.Id && e.IsActive, ct);
            unitPrice = entry?.ListPrice ?? product.UnitPrice;
        }
        else if (product != null)
        {
            unitPrice = product.UnitPrice;
        }
        else
        {
            throw new ValidationException("unitPrice", "Unit price is required for custom items.");
        }

        if (unitPrice < 0m || unitPrice > 9_999_999_999_999m)
        {
            throw new ValidationException("unitPrice", "Unit price must be zero or positive.");
        }

        var discountPercent = item.DiscountPercent ?? 0m;
        if (discountPercent < 0m || discountPercent > 100m)
        {
            throw new ValidationException("discountPercent", "Discount percent must be between 0 and 100.");
        }

        var taxRate = item.TaxRate ?? product?.TaxRate ?? 0m;
        if (taxRate < 0m || taxRate > 100m)
        {
            throw new ValidationException("taxRate", "Tax rate must be between 0 and 100.");
        }

        // Server-side strict calculations
        var quantity = Math.Round(item.Quantity, 4, MidpointRounding.AwayFromZero);
        unitPrice = Math.Round(unitPrice, 4, MidpointRounding.AwayFromZero);
        var subTotal = Math.Round(quantity * unitPrice, 4, MidpointRounding.AwayFromZero);

        decimal discountAmount;
        if (discountPercent > 0m)
        {
            discountAmount = Math.Round(subTotal * (discountPercent / 100m), 4, MidpointRounding.AwayFromZero);
        }
        else
        {
            var requestedDiscount = item.DiscountAmount ?? 0m;
            if (requestedDiscount < 0m)
            {
                throw new ValidationException("discountAmount", "Discount amount cannot be negative.");
            }
            discountAmount = Math.Min(requestedDiscount, subTotal);
        }

        var taxableAmount = Math.Max(0m, subTotal - discountAmount);
        var taxAmount = Math.Round(taxableAmount * (taxRate / 100m), 4, MidpointRounding.AwayFromZero);
        var totalAmount = Math.Round(taxableAmount + taxAmount, 4, MidpointRounding.AwayFromZero);

        return new DealLineItem
        {
            DealId = deal.Id,
            ProductId = product?.Id,
            PriceBookId = priceBook?.Id,
            ProductName = productName,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            DiscountPercent = discountPercent,
            DiscountAmount = discountAmount,
            TaxRate = taxRate,
            SubTotal = subTotal,
            TotalAmount = totalAmount,
            SortOrder = item.SortOrder != 0 ? item.SortOrder : defaultSortOrder
        };
    }

    private async Task<Deal> LoadScopedDealAsync(Guid id, CancellationToken ct)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Deals, ct);
        var query = db.Deals.AsQueryable();
        if (ids is not null)
        {
            query = query.Where(d => ids.Contains(d.OwnerId));
        }

        return await query.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Deal", id);
    }
}
