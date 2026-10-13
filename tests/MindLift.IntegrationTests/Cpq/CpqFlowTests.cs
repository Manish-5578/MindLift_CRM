using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Cpq;

public sealed class CpqFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Can_create_and_manage_products_in_catalog()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create Product
        var createReq = new SaveProductRequest
        {
            Name = "Enterprise Cloud License",
            Sku = $"SKU-CLOUD-{Guid.NewGuid():N}"[..16],
            Description = "Annual enterprise subscription per seat",
            Category = "Software",
            Type = ProductType.Subscription,
            UnitPrice = 1200m,
            Currency = "USD",
            TaxRate = 10m,
            BillingFrequency = BillingFrequency.Annual,
            IsActive = true,
            StockQuantity = 100
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/products", createReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = await postResp.Content.ReadFromJsonAsync<ProductDto>();
        product.ShouldNotBeNull();
        product.Name.ShouldBe(createReq.Name);
        product.UnitPrice.ShouldBe(1200m);

        // 2. Query products
        var listResp = await admin.Client.GetAsync("/api/v1/products");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await listResp.Content.ReadFromJsonAsync<List<ProductDto>>();
        list.ShouldNotBeNull();
        list.ShouldContain(p => p.Id == product.Id);

        // 3. Update Product
        var updateReq = new SaveProductRequest
        {
            Name = "Enterprise Cloud License v2",
            Sku = product.Sku,
            Category = "Software",
            Type = ProductType.Subscription,
            UnitPrice = 1350m,
            Currency = "USD",
            TaxRate = 10m,
            BillingFrequency = BillingFrequency.Annual,
            IsActive = true
        };

        var putResp = await admin.Client.PutAsJsonAsync($"/api/v1/products/{product.Id}", updateReq);
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<ProductDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Enterprise Cloud License v2");
        updated.UnitPrice.ShouldBe(1350m);
    }

    [Fact]
    public async Task Can_create_price_books_and_override_prices()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create a Product
        var prodResp = await admin.Client.PostAsJsonAsync("/api/v1/products", new SaveProductRequest
        {
            Name = "Implementation Service",
            Sku = $"SKU-SVC-{Guid.NewGuid():N}"[..16],
            UnitPrice = 5000m,
            Currency = "USD"
        });
        var prod = await prodResp.Content.ReadFromJsonAsync<ProductDto>();
        prod.ShouldNotBeNull();

        // 2. Create Price Book
        var pbResp = await admin.Client.PostAsJsonAsync("/api/v1/price-books", new SavePriceBookRequest
        {
            Name = "Partner Wholesale Price Book",
            Currency = "USD",
            IsStandard = false
        });
        pbResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var priceBook = await pbResp.Content.ReadFromJsonAsync<PriceBookDto>();
        priceBook.ShouldNotBeNull();

        // 3. Set Price Book Entry
        var entryResp = await admin.Client.PostAsJsonAsync($"/api/v1/price-books/{priceBook.Id}/entries", new SavePriceBookEntryRequest
        {
            ProductId = prod.Id,
            ListPrice = 4000m,
            UseStandardPrice = false
        });
        entryResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = await entryResp.Content.ReadFromJsonAsync<PriceBookEntryDto>();
        entry.ShouldNotBeNull();
        entry.ListPrice.ShouldBe(4000m);
        entry.ProductId.ShouldBe(prod.Id);
    }

    [Fact]
    public async Task Can_create_quote_with_calculations_and_sync_to_deal()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create a Deal
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipelines = (await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        var stageId = pipelines[0].Stages[0].Id;

        var dealReq = new SaveDealRequest("Acme Global Expansion", pipelines[0].Id, stageId, 1000m, "AED", null, null, null, null, null, null, null);
        var dealResp = await admin.Client.PostAsJsonAsync("/api/v1/deals", dealReq);
        dealResp.StatusCode.ShouldBe(HttpStatusCode.OK, await dealResp.Content.ReadAsStringAsync());
        var dealJson = await dealResp.Content.ReadFromJsonAsync<DealDto>();
        dealJson.ShouldNotBeNull();

        // 2. Create Quote with line items and discounts
        var quoteReq = new SaveQuoteRequest
        {
            Title = "Acme Deployment Proposal",
            DealId = dealJson.Id,
            Currency = "AED",
            DiscountPercent = 5m, // 5% overall discount
            LineItems =
            [
                new SaveQuoteLineItemRequest
                {
                    ProductName = "Hardware Appliance",
                    Quantity = 2,
                    UnitPrice = 3000m, // 6,000
                    DiscountPercent = 10m, // 10% line discount -> 600 disc -> 5,400 net
                    TaxRate = 10m // 10% tax -> 540 tax -> 5,940 line total
                },
                new SaveQuoteLineItemRequest
                {
                    ProductName = "Consulting Package",
                    Quantity = 1,
                    UnitPrice = 4000m, // 4,000
                    DiscountPercent = 0m,
                    TaxRate = 0m // 0 tax -> 4,000 line total
                }
            ]
        };

        var quoteResp = await admin.Client.PostAsJsonAsync("/api/v1/quotes", quoteReq);
        quoteResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var quote = await quoteResp.Content.ReadFromJsonAsync<QuoteDto>();
        quote.ShouldNotBeNull();
        quote.QuoteNumber.ShouldStartWith("Q-");
        quote.LineItems.Count.ShouldBe(2);

        // 3. Verify math calculations
        quote.SubTotal.ShouldBe(10000m); // 6000 + 4000
        quote.TaxAmount.ShouldBe(540m);
        quote.GrandTotal.ShouldBeGreaterThan(8000m);

        // 4. Sync Quote to Deal
        var syncResp = await admin.Client.PostAsync($"/api/v1/quotes/{quote.Id}/sync", null);
        syncResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var syncedQuote = await syncResp.Content.ReadFromJsonAsync<QuoteDto>();
        syncedQuote.ShouldNotBeNull();
        syncedQuote.IsSyncedToDeal.ShouldBeTrue();

        // 5. Verify Deal amount was updated to match quote GrandTotal
        var updatedDealResp = await admin.Client.GetAsync($"/api/v1/deals/{dealJson.Id}");
        updatedDealResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedDeal = await updatedDealResp.Content.ReadFromJsonAsync<DealDto>();
        updatedDeal.ShouldNotBeNull();
        updatedDeal.Amount.ShouldBe(syncedQuote.GrandTotal);

        // 6. Update Quote status to Accepted
        var statusResp = await admin.Client.PostAsync($"/api/v1/quotes/{quote.Id}/status?status={QuoteStatus.Accepted}", null);
        statusResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var acceptedQuote = await statusResp.Content.ReadFromJsonAsync<QuoteDto>();
        acceptedQuote.ShouldNotBeNull();
        acceptedQuote.Status.ShouldBe(QuoteStatus.Accepted);
    }


    [Fact]
    public async Task Can_convert_quote_to_invoice_and_record_payments()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create a Quote
        var quoteResp = await admin.Client.PostAsJsonAsync("/api/v1/quotes", new SaveQuoteRequest
        {
            Title = "Annual Subscription Order",
            Currency = "USD",
            LineItems =
            [
                new SaveQuoteLineItemRequest
                {
                    ProductName = "Core Platform",
                    Quantity = 1,
                    UnitPrice = 10000m,
                    TaxRate = 10m // 1,000 tax
                }
            ]
        });
        var quote = await quoteResp.Content.ReadFromJsonAsync<QuoteDto>();
        quote.ShouldNotBeNull();
        quote.GrandTotal.ShouldBe(11000m);

        // 2. Convert Quote to Invoice
        var convertResp = await admin.Client.PostAsync($"/api/v1/quotes/{quote.Id}/convert-to-invoice", null);
        convertResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var invoice = await convertResp.Content.ReadFromJsonAsync<InvoiceDto>();
        invoice.ShouldNotBeNull();
        invoice.InvoiceNumber.ShouldStartWith("INV-");
        invoice.GrandTotal.ShouldBe(11000m);
        invoice.BalanceDue.ShouldBe(11000m);
        invoice.PaidAmount.ShouldBe(0m);
        invoice.Status.ShouldBe(InvoiceStatus.Draft);

        // 3. Record Partial Payment
        var partPayResp = await admin.Client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new RecordPaymentRequest
        {
            Amount = 5000m,
            Method = PaymentMethod.BankTransfer,
            ReferenceNumber = "WIRE-99211"
        });
        partPayResp.StatusCode.ShouldBe(HttpStatusCode.OK, await partPayResp.Content.ReadAsStringAsync());

        // Verify invoice balance due and status
        var getInvResp1 = await admin.Client.GetAsync($"/api/v1/invoices/{invoice.Id}");
        var inv1 = await getInvResp1.Content.ReadFromJsonAsync<InvoiceDto>();
        inv1.ShouldNotBeNull();
        inv1.PaidAmount.ShouldBe(5000m);
        inv1.BalanceDue.ShouldBe(6000m);
        inv1.Status.ShouldBe(InvoiceStatus.PartiallyPaid);
        inv1.Payments.Count.ShouldBe(1);

        // 4. Record Final Payment
        var fullPayResp = await admin.Client.PostAsJsonAsync($"/api/v1/invoices/{invoice.Id}/payments", new RecordPaymentRequest
        {
            Amount = 6000m,
            Method = PaymentMethod.CreditCard,
            ReferenceNumber = "CC-4482"
        });
        fullPayResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var getInvResp2 = await admin.Client.GetAsync($"/api/v1/invoices/{invoice.Id}");
        var inv2 = await getInvResp2.Content.ReadFromJsonAsync<InvoiceDto>();
        inv2.ShouldNotBeNull();
        inv2.PaidAmount.ShouldBe(11000m);
        inv2.BalanceDue.ShouldBe(0m);
        inv2.Status.ShouldBe(InvoiceStatus.Paid);
        inv2.Payments.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Can_upload_document_and_process_ocr_intelligence()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Upload business invoice document
        var content = "INVOICE\nVendor: Global Logistics Inc.\nInvoice #: INV-55421\nDate: 2026-10-15\nTotal Amount Due: $3,250.00\nLine 1: Freight: $3,250.00";
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(fileContent, "file", "vendor_invoice.txt");
        form.Add(new StringContent("Invoice"), "category");

        var uploadResp = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        uploadResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = await uploadResp.Content.ReadFromJsonAsync<BusinessDocumentDto>();
        doc.ShouldNotBeNull();
        doc.FileName.ShouldBe("vendor_invoice.txt");
        doc.OcrStatus.ShouldBe(DocumentOcrStatus.Pending);

        // 2. Trigger OCR extraction
        var ocrResp = await admin.Client.PostAsync($"/api/v1/documents/{doc.Id}/ocr", null);
        ocrResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ocr = await ocrResp.Content.ReadFromJsonAsync<OcrResultDto>();
        ocr.ShouldNotBeNull();
        ocr.Status.ShouldBe(DocumentOcrStatus.Completed);
        ocr.DetectedVendor.ShouldBe("Global Logistics Inc.");
        ocr.DetectedInvoiceNumber.ShouldBe("INV-55421");
        ocr.DetectedTotal.ShouldBe(3250.00m);
        ocr.DetectedDate.ShouldNotBeNull();

        // 3. Verify document retrieval reflects extracted metadata
        var getDocResp = await admin.Client.GetAsync($"/api/v1/documents/{doc.Id}");
        getDocResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loadedDoc = await getDocResp.Content.ReadFromJsonAsync<BusinessDocumentDto>();
        loadedDoc.ShouldNotBeNull();
        loadedDoc.OcrStatus.ShouldBe(DocumentOcrStatus.Completed);
        loadedDoc.OcrResult.ShouldNotBeNull();
        loadedDoc.OcrResult.DetectedVendor.ShouldBe("Global Logistics Inc.");
        loadedDoc.OcrResult.DetectedTotal.ShouldBe(3250.00m);

        // 4. Download file
        var fileResp = await admin.Client.GetAsync($"/api/v1/documents/{doc.Id}/file");
        fileResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var downloadedBytes = await fileResp.Content.ReadAsByteArrayAsync();
        downloadedBytes.Length.ShouldBeGreaterThan(0);
        Encoding.UTF8.GetString(downloadedBytes).ShouldContain("Global Logistics Inc.");
    }
}

