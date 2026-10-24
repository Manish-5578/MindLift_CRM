using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Billing;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// A company's own billing: its plan, invoices, choosing a plan and cancelling. Only people who can manage the company's settings reach it.
/// It stays open when the subscription has lapsed, so the company can always pay.
/// </summary>
[ApiController]
[Route("api/v1/billing")]
[Authorize]
public sealed class BillingController(BillingService billing, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    public async Task<ActionResult<BillingOverviewDto>> Get(CancellationToken cancellationToken)
        => Ok(await billing.OverviewAsync(cancellationToken));

    [HttpPost("checkout")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<CheckoutResultDto>> Checkout([FromBody] StartCheckoutRequest request, CancellationToken cancellationToken)
        => Ok(await billing.CheckoutAsync(request, (configuration["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/'), cancellationToken));

    [HttpPost("cancel")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<BillingOverviewDto>> Cancel(CancellationToken cancellationToken)
        => Ok(await billing.CancelAsync(cancellationToken));

    [HttpPost("resume")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<BillingOverviewDto>> Resume(CancellationToken cancellationToken)
        => Ok(await billing.ResumeAsync(cancellationToken));
}

/// <summary>Where a payment gateway reports payments. Checked by the gateway's own signature; no sign-in.</summary>
[ApiController]
[Route("api/v1/billing/webhooks")]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("webhook")]
public sealed class BillingWebhookController(BillingService billing, IPaymentProvider provider, ILogger<BillingWebhookController> logger) : ControllerBase
{
    [HttpPost("{key}")]
    public async Task<IActionResult> Receive(string key, CancellationToken cancellationToken)
    {
        if (!provider.Online || !string.Equals(key, provider.Key, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        try
        {
            var events = await provider.ReadWebhookAsync(body, headers, cancellationToken);
            await billing.ApplyAsync(events, cancellationToken);
            return Ok();
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogWarning("Billing webhook for {Provider} had a wrong signature", key);
            return Unauthorized();
        }
    }
}
