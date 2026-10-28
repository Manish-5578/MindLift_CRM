namespace MindLift.Shared.Contracts;

public sealed record DealLineItemDto(
    Guid Id,
    Guid DealId,
    Guid? ProductId,
    string? ProductSku,
    Guid? PriceBookId,
    string? PriceBookName,
    string ProductName,
    string? Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal TaxRate,
    decimal SubTotal,
    decimal TotalAmount,
    int SortOrder);

public sealed record SaveDealLineItemRequest(
    Guid? ProductId,
    Guid? PriceBookId,
    string? ProductName,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? DiscountPercent,
    decimal? DiscountAmount,
    decimal? TaxRate,
    int SortOrder = 0);

public sealed record DealLineItemsSummaryDto(
    IReadOnlyList<DealLineItemDto> Items,
    decimal SubTotal,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal GrandTotal,
    string Currency);

public sealed record SetDealLineItemsRequest(
    IReadOnlyList<SaveDealLineItemRequest> Items,
    bool UpdateDealAmount = true);
