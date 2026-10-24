using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Messaging;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>The company's email sending: its daily limit, its own sending domains and the addresses it must not email.</summary>
[ApiController]
[Route("api/v1/settings/email-sending")]
[Authorize]
public sealed class EmailSendingController(SendingDomainService domains) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    public async Task<ActionResult<EmailSendingDto>> Get(CancellationToken cancellationToken) => Ok(await domains.GetAsync(cancellationToken));

    [HttpPost("domains")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<SendingDomainDto>> AddDomain([FromBody] AddSendingDomainRequest request, CancellationToken cancellationToken)
        => Ok(await domains.AddAsync(request, cancellationToken));

    [HttpPost("domains/{id:guid}/check")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<ActionResult<SendingDomainDto>> Check(Guid id, CancellationToken cancellationToken) => Ok(await domains.CheckAsync(id, cancellationToken));

    [HttpDelete("domains/{id:guid}")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<IActionResult> RemoveDomain(Guid id, CancellationToken cancellationToken)
    {
        await domains.RemoveDomainAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("suppressions")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<IActionResult> AddSuppression([FromBody] AddSuppressionRequest request, CancellationToken cancellationToken)
    {
        await domains.AddSuppressionAsync(request.Email, cancellationToken);
        return NoContent();
    }

    [HttpDelete("suppressions/{id:guid}")]
    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    public async Task<IActionResult> RemoveSuppression(Guid id, CancellationToken cancellationToken)
    {
        await domains.RemoveSuppressionAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>
/// The unsubscribe link at the foot of automated mail. Opening it shows a confirmation button; pressing it (a POST) unsubscribes, so link
/// checkers that open every link in a mail cannot unsubscribe anyone by accident. Mail clients' one-click unsubscribe also posts here.
/// </summary>
[ApiController]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
public sealed class UnsubscribeController(EmailGuard guard) : ControllerBase
{
    [HttpGet("/unsubscribe")]
    public ContentResult Confirm([FromQuery] string? t)
        => Page("Unsubscribe", "Press the button to stop receiving these emails.",
            $"<form method=\"post\" action=\"/unsubscribe?t={Uri.EscapeDataString(t ?? string.Empty)}\"><button type=\"submit\">Unsubscribe</button></form>");

    [HttpPost("/unsubscribe")]
    public async Task<ContentResult> Apply([FromQuery] string? t, CancellationToken cancellationToken)
    {
        var address = string.IsNullOrEmpty(t) ? null : await guard.UnsubscribeAsync(t, cancellationToken);
        return address is null
            ? Page("Link not valid", "This unsubscribe link is not valid. Reply to the email instead and ask to be removed.", string.Empty, 400)
            : Page("You are unsubscribed", $"{System.Net.WebUtility.HtmlEncode(address)} will not receive these emails any more.", string.Empty);
    }

    private ContentResult Page(string title, string message, string extra, int status = 200)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'";
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{{title}}</title>
            <style>body{font-family:system-ui,sans-serif;background:#F8FAFC;color:#0F172A;display:grid;place-items:center;min-height:100vh;margin:0}
            main{background:#fff;border:1px solid #E2E8F0;border-radius:16px;padding:32px;max-width:420px;text-align:center;box-shadow:0 10px 30px rgba(15,23,42,.08)}
            button{background:#4F46E5;color:#fff;border:0;border-radius:10px;padding:12px 22px;font-size:15px;cursor:pointer;margin-top:12px}</style></head>
            <body><main><h1 style="font-size:22px">{{title}}</h1><p>{{message}}</p>{{extra}}</main></body></html>
            """;
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = status };
    }
}
