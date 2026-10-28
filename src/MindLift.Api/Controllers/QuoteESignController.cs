using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 3 / #19: Quote E-Signature (quote-esign).
/// Endpoints for initiating, tracking, verifying, and executing legally defensible quote e-signatures.
/// </summary>
[ApiController]
public sealed class QuoteESignController(IQuoteESignService eSignService) : ControllerBase
{
    // ================= Authenticated Endpoints =================

    [HttpPost("api/v1/quotes/{id:guid}/esign/send")]
    [Authorize]
    [RequiresFeature("quote-esign")]
    public async Task<ActionResult<QuoteESignDetailsDto>> SendForSignature(
        Guid id,
        [FromBody] SendQuoteESignRequest request,
        CancellationToken ct)
    {
        var details = await eSignService.SendForSignatureAsync(id, request, ct);
        return Ok(details);
    }

    [HttpGet("api/v1/quotes/{id:guid}/esign")]
    [Authorize]
    [RequiresFeature("quote-esign")]
    public async Task<ActionResult<QuoteESignDetailsDto>> GetESignDetails(
        Guid id,
        CancellationToken ct)
    {
        var details = await eSignService.GetESignDetailsByQuoteIdAsync(id, ct);
        return Ok(details);
    }

    [HttpPost("api/v1/quotes/{id:guid}/esign/revoke")]
    [Authorize]
    [RequiresFeature("quote-esign")]
    public async Task<ActionResult<QuoteESignDetailsDto>> Revoke(
        Guid id,
        [FromBody] RevokeQuoteESignRequest request,
        CancellationToken ct)
    {
        var details = await eSignService.RevokeAsync(id, request.Reason, ct);
        return Ok(details);
    }

    [HttpGet("api/v1/quotes/{id:guid}/esign/audit-trail")]
    [Authorize]
    [RequiresFeature("quote-esign")]
    public async Task<ActionResult<IReadOnlyList<QuoteSignatureLogDto>>> GetAuditTrail(
        Guid id,
        CancellationToken ct)
    {
        var trail = await eSignService.GetAuditTrailAsync(id, ct);
        return Ok(trail);
    }

    [HttpGet("api/v1/quotes/{id:guid}/esign/certificate")]
    [Authorize]
    [RequiresFeature("quote-esign")]
    public async Task<ActionResult<CertificateOfCompletionDto>> GetCertificate(
        Guid id,
        CancellationToken ct)
    {
        var cert = await eSignService.GetCertificateAsync(id, ct);
        return Ok(cert);
    }

    // ================= Public Recipient Endpoints =================

    [HttpGet("api/v1/quotes/public/esign/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<QuoteESignDetailsDto>> GetPublicSigningDetails(
        string token,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var details = await eSignService.GetSigningDetailsByTokenAsync(token, ip, userAgent, ct);
        return Ok(details);
    }

    [HttpPost("api/v1/quotes/public/esign/sign")]
    [AllowAnonymous]
    public async Task<ActionResult<QuoteESignDetailsDto>> SignPublic(
        [FromBody] SignQuoteRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var details = await eSignService.SignAsync(request, ip, userAgent, ct);
        return Ok(details);
    }

    [HttpPost("api/v1/quotes/public/esign/decline")]
    [AllowAnonymous]
    public async Task<ActionResult<QuoteESignDetailsDto>> DeclinePublic(
        [FromBody] DeclineQuoteESignRequest request,
        CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var details = await eSignService.DeclineAsync(request, ip, userAgent, ct);
        return Ok(details);
    }
}
