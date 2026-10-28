using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Cpq;

public sealed class PaymentLinkTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<InvoiceDto> CreateTestInvoiceAsync(Session session, decimal amount = 5000m)
    {
        var invReq = new SaveInvoiceRequest
        {
            Currency = "USD",
            PaymentTerms = "Due upon receipt",
            Notes = "Automated hosting and maintenance charges.",
            LineItems =
            [
                new SaveInvoiceLineItemRequest
                {
                    ProductName = "Cloud Infrastructure Provisioning",
                    Quantity = 1,
                    UnitPrice = amount
                }
            ]
        };

        var postResp = await session.Client.PostAsJsonAsync("/api/v1/invoices", invReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var inv = await postResp.Content.ReadFromJsonAsync<InvoiceDto>();
        inv.ShouldNotBeNull();
        return inv;
    }

    [Fact]
    public async Task Returns_404_when_payment_links_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var inv = await CreateTestInvoiceAsync(admin, 1000m);

        // Attempt to create payment link when feature is disabled
        var createReq = new CreatePaymentLinkRequest
        {
            Title = "Monthly Invoice Payment",
            InvoiceId = inv.Id,
            Amount = 1000m
        };
        var resp = await admin.Client.PostAsJsonAsync("/api/v1/payment-links", createReq);
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Attempt to list payment links
        var listResp = await admin.Client.GetAsync("/api/v1/payment-links");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_invoice_payment_link_and_checkout_fulfillment()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Enable feature flag
        (await admin.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var inv = await CreateTestInvoiceAsync(admin, 5000m);

        // 1. Create Payment Link
        var createReq = new CreatePaymentLinkRequest
        {
            Title = "Annual Cloud Subscription Payment",
            Description = "Full upfront payment for 12 months",
            InvoiceId = inv.Id,
            Amount = 5000m,
            Currency = "USD",
            CustomerEmail = "billing@client.corp",
            CustomerName = "Client Accounts Payable",
            ExpiresInDays = 30
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/payment-links", createReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var link = await createResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        link.ShouldNotBeNull();
        link.Status.ShouldBe("Active");
        link.LinkNumber.ShouldStartWith("PL-");
        link.PaymentToken.ShouldNotBeNullOrWhiteSpace();
        link.CheckoutUrl.ShouldContain(link.PaymentToken);
        link.InvoiceId.ShouldBe(inv.Id);

        var token = link.PaymentToken;

        // Anonymous public client (no auth token)
        var publicClient = factory.CreateClient();

        // 2. Public view of checkout link
        var viewResp = await publicClient.GetAsync($"/api/v1/payment-links/public/checkout/{token}");
        viewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var checkout = await viewResp.Content.ReadFromJsonAsync<PublicCheckoutDto>();
        checkout.ShouldNotBeNull();
        checkout.Title.ShouldBe("Annual Cloud Subscription Payment");
        checkout.Amount.ShouldBe(5000m);
        checkout.RemainingAmount.ShouldBe(5000m);
        checkout.Status.ShouldBe("Active");
        checkout.InvoiceNumber.ShouldBe(inv.InvoiceNumber);

        // 3. Process payment
        var payReq = new ProcessPaymentRequest
        {
            PaymentToken = token,
            Amount = 5000m,
            PaymentMethod = "card",
            CardHolderName = "Client Accounts Payable",
            CardLast4 = "4242",
            Reference = "ch_live_test_001"
        };

        var payResp = await publicClient.PostAsJsonAsync("/api/v1/payment-links/public/pay", payReq);
        payResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt = await payResp.Content.ReadFromJsonAsync<PaymentReceiptDto>();
        receipt.ShouldNotBeNull();
        receipt.Status.ShouldBe("Paid");
        receipt.AmountPaid.ShouldBe(5000m);
        receipt.RemainingBalance.ShouldBe(0m);
        receipt.ReceiptNumber.ShouldStartWith("RCT-");

        // 4. Verify in CRM that Payment Link is Paid
        var getLinkResp = await admin.Client.GetAsync($"/api/v1/payment-links/{link.Id}");
        getLinkResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedLink = await getLinkResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        updatedLink.ShouldNotBeNull();
        updatedLink.Status.ShouldBe("Paid");
        updatedLink.PaidAmount.ShouldBe(5000m);
        updatedLink.PaidAt.ShouldNotBeNull();
        updatedLink.ReceiptNumber.ShouldBe(receipt.ReceiptNumber);

        // 5. Verify in CRM that Invoice status is Paid and PaymentRecord exists
        var getInvResp = await admin.Client.GetAsync($"/api/v1/invoices/{inv.Id}");
        getInvResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedInv = await getInvResp.Content.ReadFromJsonAsync<InvoiceDto>();
        updatedInv.ShouldNotBeNull();
        updatedInv.Status.ShouldBe(InvoiceStatus.Paid);
        updatedInv.PaidAmount.ShouldBe(5000m);
        updatedInv.BalanceDue.ShouldBe(0m);
        updatedInv.Payments.Count.ShouldBeGreaterThanOrEqualTo(1);
        updatedInv.Payments.ShouldContain(p => p.Amount == 5000m);
    }

    [Fact]
    public async Task Partial_payments_workflow()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var inv = await CreateTestInvoiceAsync(admin, 2000m);

        // Create link with partial payments enabled
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/payment-links", new CreatePaymentLinkRequest
        {
            Title = "Installment Billing Plan",
            InvoiceId = inv.Id,
            Amount = 2000m,
            AllowPartial = true
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var link = await createResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        link.ShouldNotBeNull();

        var publicClient = factory.CreateClient();

        // 1. Pay first installment: $800
        var payResp1 = await publicClient.PostAsJsonAsync("/api/v1/payment-links/public/pay", new ProcessPaymentRequest
        {
            PaymentToken = link.PaymentToken,
            Amount = 800m
        });
        payResp1.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt1 = await payResp1.Content.ReadFromJsonAsync<PaymentReceiptDto>();
        receipt1.ShouldNotBeNull();
        receipt1.Status.ShouldBe("PartiallyPaid");
        receipt1.AmountPaid.ShouldBe(800m);
        receipt1.RemainingBalance.ShouldBe(1200m);

        // Verify Invoice in CRM is PartiallyPaid
        var invResp1 = await admin.Client.GetAsync($"/api/v1/invoices/{inv.Id}");
        var inv1 = await invResp1.Content.ReadFromJsonAsync<InvoiceDto>();
        inv1.ShouldNotBeNull();
        inv1.Status.ShouldBe(InvoiceStatus.PartiallyPaid);
        inv1.PaidAmount.ShouldBe(800m);
        inv1.BalanceDue.ShouldBe(1200m);

        // 2. Pay remaining installment: $1200
        var payResp2 = await publicClient.PostAsJsonAsync("/api/v1/payment-links/public/pay", new ProcessPaymentRequest
        {
            PaymentToken = link.PaymentToken,
            Amount = 1200m
        });
        payResp2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var receipt2 = await payResp2.Content.ReadFromJsonAsync<PaymentReceiptDto>();
        receipt2.ShouldNotBeNull();
        receipt2.Status.ShouldBe("Paid");
        receipt2.AmountPaid.ShouldBe(1200m);
        receipt2.RemainingBalance.ShouldBe(0m);

        // Verify Invoice in CRM is now Paid in full
        var invResp2 = await admin.Client.GetAsync($"/api/v1/invoices/{inv.Id}");
        var inv2 = await invResp2.Content.ReadFromJsonAsync<InvoiceDto>();
        inv2.ShouldNotBeNull();
        inv2.Status.ShouldBe(InvoiceStatus.Paid);
        inv2.PaidAmount.ShouldBe(2000m);
        inv2.BalanceDue.ShouldBe(0m);
        inv2.Payments.Count.ShouldBe(2);

        // 3. Attempting extra payment when already paid in full fails
        var overpayResp = await publicClient.PostAsJsonAsync("/api/v1/payment-links/public/pay", new ProcessPaymentRequest
        {
            PaymentToken = link.PaymentToken,
            Amount = 100m
        });
        overpayResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_idempotent_processing()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var inv = await CreateTestInvoiceAsync(admin, 1500m);

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/payment-links", new CreatePaymentLinkRequest
        {
            Title = "Webhook Test Invoice",
            InvoiceId = inv.Id,
            Amount = 1500m
        });
        var link = await createResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        link.ShouldNotBeNull();

        var publicClient = factory.CreateClient();

        var webhookPayload = new WebhookEventPayload
        {
            EventId = "evt_" + Guid.NewGuid().ToString("N"),
            EventType = "payment_intent.succeeded",
            PaymentToken = link.PaymentToken,
            Amount = 1500m,
            Currency = "USD",
            TransactionReference = "pi_stripe_webhook_001"
        };
        var jsonContent = new StringContent(JsonSerializer.Serialize(webhookPayload), Encoding.UTF8, "application/json");

        // First webhook delivery
        var webhookResp1 = await publicClient.PostAsync("/api/v1/payment-links/webhooks/stripe", jsonContent);
        webhookResp1.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify link is Paid
        var getLink = await admin.Client.GetFromJsonAsync<PaymentLinkDto>($"/api/v1/payment-links/{link.Id}");
        getLink.ShouldNotBeNull();
        getLink.Status.ShouldBe("Paid");

        // Duplicate webhook delivery with same EventId
        var jsonContent2 = new StringContent(JsonSerializer.Serialize(webhookPayload), Encoding.UTF8, "application/json");
        var webhookResp2 = await publicClient.PostAsync("/api/v1/payment-links/webhooks/stripe", jsonContent2);
        webhookResp2.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify invoice was not double-credited
        var invResp = await admin.Client.GetFromJsonAsync<InvoiceDto>($"/api/v1/invoices/{inv.Id}");
        invResp.ShouldNotBeNull();
        invResp.PaidAmount.ShouldBe(1500m);
        invResp.Payments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_link_rejects_payments()
    {
        var admin = await Workspaces.SignupAsync(factory);

        (await admin.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/payment-links", new CreatePaymentLinkRequest
        {
            Title = "To Be Cancelled Link",
            Amount = 500m
        });
        var link = await createResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        link.ShouldNotBeNull();

        // Cancel the link
        var cancelResp = await admin.Client.PostAsync($"/api/v1/payment-links/{link.Id}/cancel", null);
        cancelResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cancelledLink = await cancelResp.Content.ReadFromJsonAsync<PaymentLinkDto>();
        cancelledLink.ShouldNotBeNull();
        cancelledLink.Status.ShouldBe("Cancelled");

        // Public attempt to pay cancelled link fails
        var publicClient = factory.CreateClient();
        var payResp = await publicClient.PostAsJsonAsync("/api/v1/payment-links/public/pay", new ProcessPaymentRequest
        {
            PaymentToken = link.PaymentToken,
            Amount = 500m
        });
        payResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/payment-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates link
        var createRespA = await tenantA.Client.PostAsJsonAsync("/api/v1/payment-links", new CreatePaymentLinkRequest
        {
            Title = "Tenant A Confidential Link",
            Amount = 3000m
        });
        var linkA = await createRespA.Content.ReadFromJsonAsync<PaymentLinkDto>();
        linkA.ShouldNotBeNull();

        // Tenant B attempts to read Tenant A's link -> 404
        var getRespB = await tenantB.Client.GetAsync($"/api/v1/payment-links/{linkA.Id}");
        getRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to cancel Tenant A's link -> 404
        var cancelRespB = await tenantB.Client.PostAsync($"/api/v1/payment-links/{linkA.Id}/cancel", null);
        cancelRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
