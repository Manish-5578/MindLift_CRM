using System;
using System.Collections.Generic;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Cpq;

public sealed class QuoteESignAndPaymentLinksTests
{
    [Fact]
    public void QuoteESign_SendRequest_Properties_And_Defaults()
    {
        var req = new SendQuoteESignRequest
        {
            SignerName = "Jane Doe",
            SignerEmail = "jane@client.com",
            SignerTitle = "VP Engineering",
            ExpiresInDays = 21,
            CustomMessage = "Please review proposal."
        };

        req.SignerName.ShouldBe("Jane Doe");
        req.SignerEmail.ShouldBe("jane@client.com");
        req.SignerTitle.ShouldBe("VP Engineering");
        req.ExpiresInDays.ShouldBe(21);
        req.CustomMessage.ShouldBe("Please review proposal.");
    }

    [Fact]
    public void QuoteESign_SignRequest_Payload_Structure()
    {
        var token = "ESIGN_" + Guid.NewGuid().ToString("N");
        var req = new SignQuoteRequest
        {
            SigningToken = token,
            SignerName = "Jane Doe",
            SignerTitle = "VP Engineering",
            SignatureData = "DIGITAL_SIGNATURE:Jane Doe:2026-11-05T00:00:00Z",
            ConsentAgreed = true
        };

        req.SigningToken.ShouldBe(token);
        req.ConsentAgreed.ShouldBeTrue();
        req.SignatureData.ShouldStartWith("DIGITAL_SIGNATURE:");
    }

    [Fact]
    public void QuoteESign_DetailsDto_Preserves_Audit_Fields()
    {
        var quoteId = Guid.NewGuid();
        var signedAt = DateTime.UtcNow;
        var dto = new QuoteESignDetailsDto
        {
            QuoteId = quoteId,
            QuoteNumber = "QUO-1004",
            Title = "Cloud Infrastructure Annual License",
            Currency = "USD",
            GrandTotal = 24500m,
            ESignStatus = "Signed",
            SignerName = "Alice Smith",
            SignerEmail = "alice@enterprise.org",
            SignerTitle = "CTO",
            SignedAt = signedAt,
            SecurityHash = "SHA256:abcd1234efgh5678",
            LineItems =
            [
                new QuoteLineItemDto
                {
                    ProductName = "Enterprise Tier",
                    Quantity = 1,
                    UnitPrice = 24500m,
                    TotalAmount = 24500m
                }
            ]
        };

        dto.QuoteId.ShouldBe(quoteId);
        dto.ESignStatus.ShouldBe("Signed");
        dto.SignerName.ShouldBe("Alice Smith");
        dto.SignedAt.ShouldBe(signedAt);
        dto.LineItems.Count.ShouldBe(1);
        dto.LineItems[0].TotalAmount.ShouldBe(24500m);
    }

    [Fact]
    public void PaymentLinks_CreateRequest_Defaults_And_Calculations()
    {
        var invoiceId = Guid.NewGuid();
        var req = new CreatePaymentLinkRequest
        {
            Title = "Settlement for INV-2026-009",
            Currency = "AED",
            Amount = 15000m,
            InvoiceId = invoiceId,
            CustomerName = "Apex Solutions",
            CustomerEmail = "finance@apex.com",
            ExpiresInDays = 30,
            AllowPartial = true,
            GatewayProvider = "Hosted"
        };

        req.InvoiceId.ShouldBe(invoiceId);
        req.Currency.ShouldBe("AED");
        req.Amount.ShouldBe(15000m);
        req.AllowPartial.ShouldBeTrue();
        req.ExpiresInDays.ShouldBe(30);
    }

    [Fact]
    public void PaymentLinks_PublicCheckout_Calculates_Remaining_And_Status()
    {
        var linkId = Guid.NewGuid();
        var checkout = new PublicCheckoutDto
        {
            Id = linkId,
            LinkNumber = "PL-1002",
            Title = "Quarterly Retainer",
            Currency = "USD",
            Amount = 5000m,
            PaidAmount = 2000m,
            RemainingAmount = 3000m,
            Status = "Active",
            CustomerName = "John Smith",
            CustomerEmail = "john@client.com",
            PaymentToken = "PAY_TOKEN_ABC123",
            AllowPartial = true
        };

        checkout.Amount.ShouldBe(5000m);
        checkout.PaidAmount.ShouldBe(2000m);
        checkout.RemainingAmount.ShouldBe(3000m);
        checkout.Status.ShouldBe("Active");
        checkout.RemainingAmount.ShouldBe(checkout.Amount - checkout.PaidAmount);
    }

    [Fact]
    public void PaymentReceipt_Record_Reflects_Successful_Processing()
    {
        var linkId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var receipt = new PaymentReceiptDto
        {
            ReceiptNumber = "REC-9948",
            PaymentLinkId = linkId,
            LinkNumber = "PL-1002",
            AmountPaid = 3000m,
            TotalAmount = 5000m,
            RemainingBalance = 0m,
            Currency = "USD",
            Status = "Paid",
            PaidAt = now,
            PaymentMethodUsed = "card",
            TransactionId = "TXN_STRIPE_98765"
        };

        receipt.ReceiptNumber.ShouldBe("REC-9948");
        receipt.RemainingBalance.ShouldBe(0m);
        receipt.Status.ShouldBe("Paid");
        receipt.TransactionId.ShouldBe("TXN_STRIPE_98765");
    }
}

