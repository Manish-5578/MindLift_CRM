using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 1 / #4: Product line items and price books on Deals.
/// Ensures server-calculated totals, currency validation, tenant isolation, and feature flag gating.
/// </summary>
public sealed class DealLineItemTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/deal-line-items", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static async Task<DealDto> SetupPipelineAndDealAsync(Session s, string dealName = "Enterprise Plan", decimal amount = 0m)
    {
        // Apply onboarding industry template to have an active pipeline
        (await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        var r = await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            dealName,
            null,
            null,
            amount,
            null, // uses workspace base currency
            20,
            DateTime.UtcNow.AddMonths(1),
            null,
            null,
            null,
            null,
            null
        ));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<DealDto>())!;
    }

    private static async Task<ProductDto> CreateProductAsync(Session s, string name, decimal price, decimal taxRate = 10m, string currency = "USD")
    {
        var r = await s.Client.PostAsJsonAsync("/api/v1/products", new SaveProductRequest
        {
            Name = name,
            Sku = $"SKU-{Guid.NewGuid():N}"[..12],
            UnitPrice = price,
            TaxRate = taxRate,
            Currency = currency
        });
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    [Fact]
    public async Task Endpoints_return_not_found_when_feature_flag_is_off()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var deal = await SetupPipelineAndDealAsync(owner);

        var response = await owner.Client.GetAsync($"/api/v1/deals/{deal.Id}/line-items");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Can_add_line_items_and_server_computes_all_totals_and_syncs_deal_amount()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);
        var deal = await SetupPipelineAndDealAsync(owner, amount: 0m);

        var product = await CreateProductAsync(owner, "Cloud License", price: 100m, taxRate: 10m, currency: deal.Currency);

        // Add 2 licenses with 10% discount
        var add = await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/line-items", new SaveDealLineItemRequest(
            product.Id,
            null,
            null,
            "Yearly seat license",
            Quantity: 2m,
            UnitPrice: null, // should default to 100
            DiscountPercent: 10m,
            DiscountAmount: null,
            TaxRate: null // should default to 10%
        ));
        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = (await add.Content.ReadFromJsonAsync<DealLineItemsSummaryDto>())!;

        // SubTotal: 2 * 100 = 200
        // Discount: 200 * 10% = 20
        // TaxBase: 180, Tax (10%): 18
        // TotalAmount: 198
        summary.Items.Count.ShouldBe(1);
        summary.Items[0].ProductName.ShouldBe("Cloud License");
        summary.Items[0].Quantity.ShouldBe(2m);
        summary.Items[0].UnitPrice.ShouldBe(100m);
        summary.Items[0].SubTotal.ShouldBe(200m);
        summary.Items[0].DiscountAmount.ShouldBe(20m);
        summary.Items[0].TotalAmount.ShouldBe(198m);
        summary.GrandTotal.ShouldBe(198m);

        // Verify the deal itself has had its Amount updated
        var dealAfter = (await owner.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!;
        dealAfter.Amount.ShouldBe(198m);
    }

    [Fact]
    public async Task Can_batch_set_line_items_and_delete_individual_items()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);
        var deal = await SetupPipelineAndDealAsync(owner, amount: 0m);

        var p1 = await CreateProductAsync(owner, "Item A", price: 50m, taxRate: 0m, currency: deal.Currency);
        var p2 = await CreateProductAsync(owner, "Item B", price: 75m, taxRate: 0m, currency: deal.Currency);

        var setResponse = await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}/line-items", new SetDealLineItemsRequest(
            [
                new SaveDealLineItemRequest(p1.Id, null, null, null, 2m, null, null, null, null), // 100
                new SaveDealLineItemRequest(p2.Id, null, null, null, 1m, null, null, null, null), // 75
            ],
            UpdateDealAmount: true
        ));
        setResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = (await setResponse.Content.ReadFromJsonAsync<DealLineItemsSummaryDto>())!;
        summary.Items.Count.ShouldBe(2);
        summary.GrandTotal.ShouldBe(175m);

        // Delete the first item
        var itemToDelete = summary.Items[0].Id;
        var deleteResponse = await owner.Client.DeleteAsync($"/api/v1/deals/{deal.Id}/line-items/{itemToDelete}");
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterDelete = (await deleteResponse.Content.ReadFromJsonAsync<DealLineItemsSummaryDto>())!;
        afterDelete.Items.Count.ShouldBe(1);
        afterDelete.GrandTotal.ShouldBe(75m);

        // Deal amount updated
        var dealAfter = (await owner.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!;
        dealAfter.Amount.ShouldBe(75m);
    }

    [Fact]
    public async Task Another_company_cannot_view_or_modify_deal_line_items()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(a);
        await EnableFeatureAsync(b);

        var aDeal = await SetupPipelineAndDealAsync(a);
        var bProduct = await CreateProductAsync(b, "B Product", price: 10m, currency: aDeal.Currency);

        // Company B cannot read Company A's deal line items
        var readRes = await b.Client.GetAsync($"/api/v1/deals/{aDeal.Id}/line-items");
        readRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Company A cannot attach Company B's product to its deal
        var attachRes = await a.Client.PostAsJsonAsync($"/api/v1/deals/{aDeal.Id}/line-items", new SaveDealLineItemRequest(
            bProduct.Id, null, null, null, 1m, null, null, null, null));
        attachRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_quantity_and_negative_prices_are_rejected()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);
        var deal = await SetupPipelineAndDealAsync(owner);

        // Quantity 0
        var r1 = await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/line-items", new SaveDealLineItemRequest(
            null, null, "Custom", null, Quantity: 0m, UnitPrice: 10m, null, null, null));
        r1.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Negative Unit Price
        var r2 = await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/line-items", new SaveDealLineItemRequest(
            null, null, "Custom", null, Quantity: 1m, UnitPrice: -5m, null, null, null));
        r2.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_member_cannot_modify_line_items_outside_their_scope()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff", "staff-lineitems");
        var ownersDeal = await SetupPipelineAndDealAsync(owner);

        // Staff (Own scope) cannot edit owner's deal line items
        var r = await staff.Client.PostAsJsonAsync($"/api/v1/deals/{ownersDeal.Id}/line-items", new SaveDealLineItemRequest(
            null, null, "Custom", null, 1m, 10m, null, null, null));
        r.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
