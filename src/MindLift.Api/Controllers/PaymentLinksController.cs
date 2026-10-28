using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #20: Online Payment Links (payment-links).
/// Endpoints for creating payment links, public checkout processing, and webhook callbacks.
/// </summary>
[ApiController]
public sealed class PaymentLinksController(IPaymentLinkService paymentLinks) : ControllerBase
{
    // ================= Authenticated Endpoints =================

    [HttpPost("api/v1/payment-links")]
    [Authorize]
    [RequiresFeature("payment-links")]
    public async Task<ActionResult<PaymentLinkDto>> Create(
        [FromBody] CreatePaymentLinkRequest request,
        CancellationToken ct)
    {
        var created = await paymentLinks.CreatePaymentLinkAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("api/v1/payment-links")]
    [Authorize]
    [RequiresFeature("payment-links")]
    public async Task<ActionResult<IReadOnlyList<PaymentLinkDto>>> List(
        [FromQuery] Guid? invoiceId,
        [FromQuery] Guid? quoteId,
        CancellationToken ct)
    {
        var list = await paymentLinks.ListPaymentLinksAsync(invoiceId, quoteId, ct);
        return Ok(list);
    }

    [HttpGet("api/v1/payment-links/{id:guid}")]
    [Authorize]
    [RequiresFeature("payment-links")]
    public async Task<ActionResult<PaymentLinkDto>> GetById(
        Guid id,
        CancellationToken ct)
    {
        var link = await paymentLinks.GetPaymentLinkByIdAsync(id, ct);
        return Ok(link);
    }

    [HttpPost("api/v1/payment-links/{id:guid}/cancel")]
    [Authorize]
    [RequiresFeature("payment-links")]
    public async Task<ActionResult<PaymentLinkDto>> Cancel(
        Guid id,
        CancellationToken ct)
    {
        var link = await paymentLinks.CancelPaymentLinkAsync(id, ct);
        return Ok(link);
    }

    // ================= Public Checkout Endpoints =================

    [HttpGet("api/v1/payment-links/public/checkout/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicCheckoutDto>> GetPublicCheckout(
        string token,
        CancellationToken ct)
    {
        var checkout = await paymentLinks.GetPublicCheckoutAsync(token, ct);
        return Ok(checkout);
    }

    [HttpPost("api/v1/payment-links/public/pay")]
    [AllowAnonymous]
    public async Task<ActionResult<PaymentReceiptDto>> Pay(
        [FromBody] ProcessPaymentRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var receipt = await paymentLinks.ProcessPublicPaymentAsync(request, ip, ct);
        return Ok(receipt);
    }

    // ================= Public Webhook Endpoint =================

    [HttpPost("api/v1/payment-links/webhooks/{provider}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(
        string provider,
        CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault()
            ?? Request.Headers["X-Razorpay-Signature"].FirstOrDefault();

        var handled = await paymentLinks.HandleWebhookAsync(provider, payload, signature, ct);
        if (!handled)
        {
            return BadRequest();
        }

        return Ok(new { received = true });
    }
}
