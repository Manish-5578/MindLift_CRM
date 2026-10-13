using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

// ================= PUBLIC / ANONYMOUS PORTAL AUTH =================

[ApiController]
[Route("api/v1/portal")]
public sealed class PortalAuthController(IPortalService portalService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{slug}/branding")]
    public async Task<ActionResult<PortalBrandingDto>> GetBranding(string slug, CancellationToken ct = default)
    {
        var branding = await portalService.GetBrandingAsync(slug, ct);
        return Ok(branding);
    }

    [AllowAnonymous]
    [HttpPost("{slug}/auth/login")]
    public async Task<ActionResult<PortalLoginResponse>> Login(string slug, [FromBody] PortalLoginRequest req, CancellationToken ct = default)
    {
        var resp = await portalService.LoginAsync(slug, req, ct);
        return Ok(resp);
    }

    [AllowAnonymous]
    [HttpPost("{slug}/auth/magic-link")]
    public async Task<IActionResult> SendMagicLink(string slug, [FromBody] PortalMagicLinkRequest req, CancellationToken ct = default)
    {
        await portalService.SendMagicLinkAsync(slug, req, ct);
        return Ok(new { message = "If your email is registered, a sign-in link has been sent." });
    }

    [AllowAnonymous]
    [HttpPost("{slug}/auth/magic-link/verify")]
    public async Task<ActionResult<PortalLoginResponse>> VerifyMagicLink(string slug, [FromBody] PortalMagicLinkVerifyRequest req, CancellationToken ct = default)
    {
        var resp = await portalService.VerifyMagicLinkAsync(slug, req, ct);
        return Ok(resp);
    }

    [AllowAnonymous]
    [HttpPost("{slug}/auth/accept-invite")]
    public async Task<ActionResult<PortalLoginResponse>> AcceptInvite(string slug, [FromBody] AcceptPortalInviteRequest req, CancellationToken ct = default)
    {
        var resp = await portalService.AcceptInviteAsync(slug, req, ct);
        return Ok(resp);
    }
}

// ================= CUSTOMER PORTAL AUTHENTICATED AREA =================

[ApiController]
[Authorize(AuthenticationSchemes = "Portal", Policy = "PortalUser")]
[Route("api/v1/portal")]
public sealed class PortalCustomerController(IPortalService portalService, IDocumentOcrService ocrService) : ControllerBase
{
    private Guid GetPortalUserId() => Guid.Parse(User.FindFirstValue("sub") ?? throw new UnauthorizedException("invalid_portal_token", "Invalid token."));
    private Guid GetContactId() => Guid.Parse(User.FindFirstValue("cid") ?? throw new UnauthorizedException("invalid_portal_token", "Missing contact claim."));
    private Guid? GetAccountId() => Guid.TryParse(User.FindFirstValue("aid"), out var a) ? a : null;

    [HttpGet("home")]
    public async Task<ActionResult<PortalHomeDto>> GetHome(CancellationToken ct = default)
    {
        var home = await portalService.GetHomeAsync(GetPortalUserId(), GetContactId(), GetAccountId(), ct);
        return Ok(home);
    }

    [HttpGet("documents")]
    public async Task<ActionResult<IReadOnlyList<BusinessDocumentDto>>> GetDocuments(
        [FromQuery] string? category,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var docs = await portalService.GetCustomerDocumentsAsync(GetContactId(), GetAccountId(), category, search, ct);
        return Ok(docs);
    }

    [HttpGet("documents/{id:guid}")]
    public async Task<ActionResult<BusinessDocumentDto>> GetDocumentById(Guid id, CancellationToken ct = default)
    {
        var doc = await ocrService.GetDocumentByIdAsync(id, ct);
        if (doc == null || !doc.SharedWithPortal) return NotFound();

        // Ensure belongs to customer contact or account
        if (doc.AssociatedContactId != GetContactId() && doc.AssociatedAccountId != GetAccountId())
            return NotFound();

        return Ok(doc);
    }

    [HttpGet("documents/{id:guid}/file")]
    public async Task<IActionResult> GetDocumentFile(Guid id, CancellationToken ct = default)
    {
        var doc = await ocrService.GetDocumentByIdAsync(id, ct);
        if (doc == null || !doc.SharedWithPortal) return NotFound();

        if (doc.AssociatedContactId != GetContactId() && doc.AssociatedAccountId != GetAccountId())
            return NotFound();

        var streamResult = await ocrService.GetDocumentStreamAsync(id, ct);
        if (streamResult == null) return NotFound();

        return File(streamResult.Value.Stream, streamResult.Value.ContentType, streamResult.Value.FileName);
    }

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<PortalDocumentRequestDto>>> GetRequests(CancellationToken ct = default)
    {
        var reqs = await portalService.GetCustomerRequestsAsync(GetContactId(), ct);
        return Ok(reqs);
    }

    [HttpPost("requests/{id:guid}/upload")]
    public async Task<ActionResult<BusinessDocumentDto>> UploadForRequest(
        Guid id,
        [FromForm] IFormFile file,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0) return BadRequest("File is empty.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var doc = await portalService.SubmitDocumentRequestAsync(
            GetContactId(),
            GetAccountId(),
            id,
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            bytes,
            ct);

        return Ok(doc);
    }
}

// ================= STAFF MANAGEMENT FOR PORTAL =================

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class PortalStaffController(IPortalService portalService) : ControllerBase
{
    [HttpGet("settings/portal")]
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    public async Task<ActionResult<PortalSettingsDto>> GetSettings(CancellationToken ct = default)
    {
        var settings = await portalService.GetSettingsAsync(ct);
        return Ok(settings);
    }

    [HttpPut("settings/portal")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<PortalSettingsDto>> SaveSettings([FromBody] SavePortalSettingsRequest req, CancellationToken ct = default)
    {
        var settings = await portalService.SaveSettingsAsync(req, ct);
        return Ok(settings);
    }

    [HttpPost("contacts/{id:guid}/portal/invite")]
    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    public async Task<ActionResult<string>> InviteContact(Guid id, [FromBody] InvitePortalUserRequest req, CancellationToken ct = default)
    {
        var token = await portalService.InviteContactAsync(req with { ContactId = id }, ct);
        return Ok(new { token });
    }

    [HttpGet("document-requests")]
    [HasPermission(AppModule.Documents, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<PortalDocumentRequestDto>>> ListRequests(
        [FromQuery] Guid? contactId,
        [FromQuery] Guid? accountId,
        [FromQuery] Guid? dealId,
        CancellationToken ct = default)
    {
        var list = await portalService.GetDocumentRequestsForRecordAsync(contactId, accountId, dealId, ct);
        return Ok(list);
    }

    [HttpPost("document-requests")]
    [HasPermission(AppModule.Documents, PermissionAction.Create)]
    public async Task<ActionResult<PortalDocumentRequestDto>> CreateRequest([FromBody] SaveDocumentRequest req, CancellationToken ct = default)
    {
        var created = await portalService.CreateDocumentRequestAsync(req, ct);
        return Ok(created);
    }
}
