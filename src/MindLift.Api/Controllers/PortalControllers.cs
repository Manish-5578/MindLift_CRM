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
    private const string RefreshCookieName = "ml_portal_refresh";
    private const string MobileHeader = "X-Client";

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
        => Issue(slug, await portalService.LoginAsync(slug, req, ct));

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
        => Issue(slug, await portalService.VerifyMagicLinkAsync(slug, req, ct));

    [AllowAnonymous]
    [HttpPost("{slug}/auth/accept-invite")]
    public async Task<ActionResult<PortalLoginResponse>> AcceptInvite(string slug, [FromBody] AcceptPortalInviteRequest req, CancellationToken ct = default)
        => Issue(slug, await portalService.AcceptInviteAsync(slug, req, ct));

    [AllowAnonymous]
    [HttpPost("{slug}/auth/refresh")]
    public async Task<ActionResult<PortalLoginResponse>> Refresh(string slug, [FromBody] PortalRefreshRequest? body, CancellationToken ct = default)
    {
        var token = Request.Cookies[RefreshCookieName] ?? body?.RefreshToken
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");
        return Issue(slug, await portalService.RefreshAsync(slug, token, ct));
    }

    [AllowAnonymous]
    [HttpPost("{slug}/auth/logout")]
    public async Task<IActionResult> Logout(string slug, [FromBody] PortalRefreshRequest? body, CancellationToken ct = default)
    {
        var token = Request.Cookies[RefreshCookieName] ?? body?.RefreshToken;
        if (!string.IsNullOrEmpty(token))
        {
            await portalService.LogoutAsync(slug, token, ct);
        }

        Response.Cookies.Delete(RefreshCookieName, CookieOptionsFor(slug, DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    /// <summary>Browsers get the refresh token as an HttpOnly cookie that scripts cannot read; mobile clients get it in the body.</summary>
    private PortalLoginResponse Issue(string slug, PortalLoginResponse result)
    {
        var isMobile = string.Equals(Request.Headers[MobileHeader], "mobile", StringComparison.OrdinalIgnoreCase);
        if (isMobile || result.RefreshToken is null)
        {
            return result;
        }

        Response.Cookies.Append(RefreshCookieName, result.RefreshToken, CookieOptionsFor(slug, DateTimeOffset.UtcNow.AddDays(30)));
        return result with { RefreshToken = null };
    }

    private static CookieOptions CookieOptionsFor(string slug, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = $"/api/v1/portal/{Uri.EscapeDataString(slug)}/auth",
        Expires = expires,
    };
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

    /// <summary>A customer may open a shared document linked to their contact, or to their account when they have one.</summary>
    private bool OwnsDocument(BusinessDocumentDto doc)
    {
        if (!doc.SharedWithPortal) return false;
        if (doc.AssociatedContactId == GetContactId()) return true;
        var accountId = GetAccountId();
        return accountId.HasValue && doc.AssociatedAccountId == accountId;
    }

    [HttpGet("home")]
    public async Task<ActionResult<PortalHomeDto>> GetHome(CancellationToken ct = default)
    {
        var home = await portalService.GetHomeAsync(GetPortalUserId(), GetContactId(), GetAccountId(), ct);
        return Ok(home);
    }

    [HttpGet("me")]
    public async Task<ActionResult<PortalProfileDto>> GetProfile(CancellationToken ct = default)
        => Ok(await portalService.GetProfileAsync(GetPortalUserId(), ct));

    [HttpPut("me")]
    public async Task<ActionResult<PortalProfileDto>> UpdateProfile([FromBody] UpdatePortalProfileRequest req, CancellationToken ct = default)
        => Ok(await portalService.UpdateProfileAsync(GetPortalUserId(), req, ct));

    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePortalPasswordRequest req, CancellationToken ct = default)
    {
        await portalService.ChangePasswordAsync(GetPortalUserId(), req, ct);
        return NoContent();
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
        if (doc == null || !OwnsDocument(doc)) return NotFound();

        return Ok(doc);
    }

    [HttpGet("documents/{id:guid}/file")]
    public async Task<IActionResult> GetDocumentFile(Guid id, CancellationToken ct = default)
    {
        var doc = await ocrService.GetDocumentByIdAsync(id, ct);
        if (doc == null || !OwnsDocument(doc)) return NotFound();

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
        if (file.Length > 25 * 1024 * 1024) return BadRequest("File exceeds maximum allowed size of 25 MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        string[] blockedExtensions = [".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".msi", ".scr", ".jar"];
        if (blockedExtensions.Contains(ext))
        {
            return BadRequest($"File extension '{ext}' is prohibited for security reasons.");
        }

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

    [HttpGet("contacts/{id:guid}/portal")]
    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    public async Task<ActionResult<PortalAccessStatusDto>> GetContactAccess(Guid id, CancellationToken ct = default)
        => Ok(await portalService.GetContactAccessAsync(id, ct));

    /// <summary>Sends the invite email. The token is never returned to staff.</summary>
    [HttpPost("contacts/{id:guid}/portal/invite")]
    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    public async Task<ActionResult<PortalAccessStatusDto>> InviteContact(Guid id, [FromBody] InvitePortalUserRequest req, CancellationToken ct = default)
    {
        await portalService.InviteContactAsync(req with { ContactId = id }, ct);
        return Ok(await portalService.GetContactAccessAsync(id, ct));
    }

    [HttpPost("document-requests/{id:guid}/status")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<ActionResult<PortalDocumentRequestDto>> UpdateRequestStatus(Guid id, [FromBody] UpdateDocumentRequestStatusRequest req, CancellationToken ct = default)
        => Ok(await portalService.UpdateDocumentRequestStatusAsync(id, req.Status, ct));

    [HttpPut("documents/{id:guid}/portal-sharing")]
    [HasPermission(AppModule.Documents, PermissionAction.Edit)]
    public async Task<IActionResult> SetDocumentSharing(Guid id, [FromBody] SetDocumentSharingRequest req, CancellationToken ct = default)
    {
        await portalService.SetDocumentSharingAsync(id, req.SharedWithPortal, ct);
        return NoContent();
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
