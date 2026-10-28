using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A product or custom service item attached to a Deal.
/// The server computes line totals and deal amount based on quantity, unit price, discounts and taxes.
/// </summary>
[Audited]
public sealed class DealLineItem : TenantEntity
{
    public Guid DealId { get; set; }
    public Deal Deal { get; set; } = null!;

    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    public Guid? PriceBookId { get; set; }
    public PriceBook? PriceBook { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TotalAmount { get; set; }
    public int SortOrder { get; set; }
}
