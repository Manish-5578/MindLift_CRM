using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Cpq;

public sealed class QuoteESignTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task<QuoteDto> CreateTestQuoteAsync(Session session, string title = "Enterprise SLA Quote")
    {
        var quoteReq = new SaveQuoteRequest
        {
            Title = title,
            Currency = "USD",
            DiscountPercent = 10m,
            TermsAndConditions = "Payment due within 30 days of electronic signature.",
            Notes = "Custom configuration with enterprise support included.",
            LineItems =
            [
                new SaveQuoteLineItemRequest
                {
                    ProductName = "Cloud Infrastructure Suite",
                    Quantity = 1,
                    UnitPrice = 12000m,
                    DiscountPercent = 5m,
                    TaxRate = 10m
                },
                new SaveQuoteLineItemRequest
                {
                    ProductName = "24/7 Dedicated Support",
                    Quantity = 12,
                    UnitPrice = 500m,
                    DiscountPercent = 0m,
                    TaxRate = 0m
                }
            ]
        };

        var postResp = await session.Client.PostAsJsonAsync("/api/v1/quotes", quoteReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var quote = await postResp.Content.ReadFromJsonAsync<QuoteDto>();
        quote.ShouldNotBeNull();
        return quote;
    }

    [Fact]
    public async Task Returns_404_when_quote_esign_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var quote = await CreateTestQuoteAsync(admin, "Disabled Feature Quote");

        // Attempt to send for signature while feature flag is disabled
        var sendReq = new SendQuoteESignRequest
        {
            SignerEmail = "client@example.com",
            SignerName = "Alice Smith"
        };
        var sendResp = await admin.Client.PostAsJsonAsync($"/api/v1/quotes/{quote.Id}/esign/send", sendReq);
        sendResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Attempt to get e-sign details while feature flag is disabled
        var getResp = await admin.Client.GetAsync($"/api/v1/quotes/{quote.Id}/esign");
        getResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_esign_workflow_send_view_sign_and_verify_certificate()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Enable quote-esign feature flag
        var flagResp = await admin.Client.PutAsJsonAsync("/api/v1/features/quote-esign", new SetFeatureRequest(true));
        flagResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var quote = await CreateTestQuoteAsync(admin, "Signature Full Lifecycle Quote");

        // 1. Send quote for signature
        var sendReq = new SendQuoteESignRequest
        {
            SignerEmail = "sarah.connor@cyberdyne.test",
            SignerName = "Sarah Connor",
            SignerTitle = "VP of Operations",
            ExpiresInDays = 7,
            CustomMessage = "Please review and sign our annual support agreement."
        };

        var sendResp = await admin.Client.PostAsJsonAsync($"/api/v1/quotes/{quote.Id}/esign/send", sendReq);
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sentDetails = await sendResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        sentDetails.ShouldNotBeNull();
        sentDetails.ESignStatus.ShouldBe("Sent");
        sentDetails.SignerEmail.ShouldBe("sarah.connor@cyberdyne.test");
        sentDetails.SignerName.ShouldBe("Sarah Connor");
        sentDetails.SignerTitle.ShouldBe("VP of Operations");
        sentDetails.SigningToken.ShouldNotBeNullOrWhiteSpace();
        sentDetails.SigningUrl.ShouldContain(sentDetails.SigningToken);
        sentDetails.AuditTrail.Count.ShouldBeGreaterThanOrEqualTo(1);
        sentDetails.AuditTrail.ShouldContain(a => a.Action == "Sent");

        var token = sentDetails.SigningToken;

        // Anonymous public client (no auth header)
        var publicClient = factory.CreateClient();

        // 2. Public view of quote by token
        var viewResp = await publicClient.GetAsync($"/api/v1/quotes/public/esign/{token}");
        viewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var viewedDetails = await viewResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        viewedDetails.ShouldNotBeNull();
        viewedDetails.ESignStatus.ShouldBe("Viewed");
        viewedDetails.LineItems.Count.ShouldBe(2);
        viewedDetails.GrandTotal.ShouldBe(quote.GrandTotal);
        viewedDetails.AuditTrail.ShouldContain(a => a.Action == "Viewed");

        // 3. Public signature submission
        var signReq = new SignQuoteRequest
        {
            SigningToken = token,
            SignerName = "Sarah Connor",
            SignerTitle = "VP of Operations",
            SignatureData = "data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjxwYXRoIGQ9Ik0xMCAxMCIvPjwvc3ZnPg==",
            ConsentAgreed = true
        };

        var signResp = await publicClient.PostAsJsonAsync("/api/v1/quotes/public/esign/sign", signReq);
        signResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var signedDetails = await signResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        signedDetails.ShouldNotBeNull();
        signedDetails.ESignStatus.ShouldBe("Signed");
        signedDetails.SignedAt.ShouldNotBeNull();
        signedDetails.SecurityHash.ShouldNotBeNullOrWhiteSpace();
        signedDetails.SignatureData.ShouldBe(signReq.SignatureData);
        signedDetails.AuditTrail.ShouldContain(a => a.Action == "Signed");

        // 4. Verify Quote entity is updated to Accepted status in CRM
        var quoteResp = await admin.Client.GetAsync($"/api/v1/quotes/{quote.Id}");
        quoteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedQuote = await quoteResp.Content.ReadFromJsonAsync<QuoteDto>();
        updatedQuote.ShouldNotBeNull();
        updatedQuote.Status.ShouldBe(QuoteStatus.Accepted);

        // 5. Verify Certificate of Completion
        var certResp = await admin.Client.GetAsync($"/api/v1/quotes/{quote.Id}/esign/certificate");
        certResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var certificate = await certResp.Content.ReadFromJsonAsync<CertificateOfCompletionDto>();
        certificate.ShouldNotBeNull();
        certificate.QuoteId.ShouldBe(quote.Id);
        certificate.QuoteNumber.ShouldBe(quote.QuoteNumber);
        certificate.ESignStatus.ShouldBe("Signed");
        certificate.SignerEmail.ShouldBe("sarah.connor@cyberdyne.test");
        certificate.SignerName.ShouldBe("Sarah Connor");
        certificate.SecurityHash.ShouldBe(signedDetails.SecurityHash);
        certificate.AuditTrail.Count.ShouldBeGreaterThanOrEqualTo(3); // Sent, Viewed, Signed

        // 6. Security verification: Re-signing a signed quote fails
        var duplicateSignResp = await publicClient.PostAsJsonAsync("/api/v1/quotes/public/esign/sign", signReq);
        duplicateSignResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Decline_workflow_records_reason_and_audit_trail()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Enable feature flag
        (await admin.Client.PutAsJsonAsync("/api/v1/features/quote-esign", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var quote = await CreateTestQuoteAsync(admin, "Declined Quote Proposal");

        // Send for signature
        var sendResp = await admin.Client.PostAsJsonAsync($"/api/v1/quotes/{quote.Id}/esign/send", new SendQuoteESignRequest
        {
            SignerEmail = "finance@apex.test",
            SignerName = "Michael Scott"
        });
        sendResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sent = await sendResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        sent.ShouldNotBeNull();

        var publicClient = factory.CreateClient();

        // Recipient declines quote
        var declineReq = new DeclineQuoteESignRequest
        {
            SigningToken = sent.SigningToken!,
            Reason = "Price exceeds current quarterly budget allocations",
            DeclinedBy = "Michael Scott"
        };

        var declineResp = await publicClient.PostAsJsonAsync("/api/v1/quotes/public/esign/decline", declineReq);
        declineResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var declined = await declineResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        declined.ShouldNotBeNull();
        declined.ESignStatus.ShouldBe("Declined");
        declined.DeclineReason.ShouldBe("Price exceeds current quarterly budget allocations");
        declined.AuditTrail.ShouldContain(a => a.Action == "Declined");

        // Verify quote status in CRM is Rejected
        var quoteResp = await admin.Client.GetAsync($"/api/v1/quotes/{quote.Id}");
        quoteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedQuote = await quoteResp.Content.ReadFromJsonAsync<QuoteDto>();
        updatedQuote.ShouldNotBeNull();
        updatedQuote.Status.ShouldBe(QuoteStatus.Rejected);
    }

    [Fact]
    public async Task Revoke_workflow_cancels_pending_signature_request()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Enable feature flag
        (await admin.Client.PutAsJsonAsync("/api/v1/features/quote-esign", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var quote = await CreateTestQuoteAsync(admin, "Revoked Quote Proposal");

        // Send for signature
        var sendResp = await admin.Client.PostAsJsonAsync($"/api/v1/quotes/{quote.Id}/esign/send", new SendQuoteESignRequest
        {
            SignerEmail = "vendor@test.local",
            SignerName = "Vendor Rep"
        });
        var sent = await sendResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        sent.ShouldNotBeNull();

        // Revoke signature request
        var revokeResp = await admin.Client.PostAsJsonAsync($"/api/v1/quotes/{quote.Id}/esign/revoke", new RevokeQuoteESignRequest
        {
            Reason = "Client requested adjusted payment schedule"
        });
        revokeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revoked = await revokeResp.Content.ReadFromJsonAsync<QuoteESignDetailsDto>();
        revoked.ShouldNotBeNull();
        revoked.ESignStatus.ShouldBe("Revoked");
        revoked.SigningToken.ShouldBeNull();
        revoked.AuditTrail.ShouldContain(a => a.Action == "Revoked");

        // Public access with old token should now return 404
        var publicClient = factory.CreateClient();
        var publicResp = await publicClient.GetAsync($"/api/v1/quotes/public/esign/{sent.SigningToken}");
        publicResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        // Enable feature flag on tenant A
        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/quote-esign", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        // Enable feature flag on tenant B as well
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/quote-esign", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var quoteA = await CreateTestQuoteAsync(tenantA, "Tenant A Confidential Quote");

        // Send quote A for signature
        var sendRespA = await tenantA.Client.PostAsJsonAsync($"/api/v1/quotes/{quoteA.Id}/esign/send", new SendQuoteESignRequest
        {
            SignerEmail = "userA@secret.test",
            SignerName = "Secret Agent A"
        });
        sendRespA.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tenant B attempts to read quote A's e-sign details -> 404
        var getRespB = await tenantB.Client.GetAsync($"/api/v1/quotes/{quoteA.Id}/esign");
        getRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to view quote A's audit trail -> 404
        var trailRespB = await tenantB.Client.GetAsync($"/api/v1/quotes/{quoteA.Id}/esign/audit-trail");
        trailRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to view quote A's certificate -> 404
        var certRespB = await tenantB.Client.GetAsync($"/api/v1/quotes/{quoteA.Id}/esign/certificate");
        certRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to revoke quote A -> 404
        var revokeRespB = await tenantB.Client.PostAsJsonAsync($"/api/v1/quotes/{quoteA.Id}/esign/revoke", new RevokeQuoteESignRequest
        {
            Reason = "Malicious revoke attempt"
        });
        revokeRespB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
