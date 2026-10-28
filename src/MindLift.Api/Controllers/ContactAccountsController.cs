using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Contacts;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>Extra accounts for a contact. Hidden (404) until the company turns on "contact-multi-account".</summary>
[ApiController]
[Route("api/v1/contacts/{contactId:guid}/accounts")]
[RequiresFeature("contact-multi-account")]
public sealed class ContactAccountsController(ContactAccountLinkService links) : ControllerBase
{
    [HttpGet]
    [HasPermission(AppModule.Contacts, PermissionAction.View)]
    public async Task<ActionResult<IReadOnlyList<ContactAccountDto>>> List(Guid contactId, CancellationToken ct)
        => Ok(await links.ListAsync(contactId, ct));

    [HttpPost]
    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    public async Task<ActionResult<IReadOnlyList<ContactAccountDto>>> Add(Guid contactId, [FromBody] AddContactAccountRequest request, CancellationToken ct)
        => Ok(await links.AddAsync(contactId, request, ct));

    [HttpDelete("{linkId:guid}")]
    [HasPermission(AppModule.Contacts, PermissionAction.Edit)]
    public async Task<IActionResult> Remove(Guid contactId, Guid linkId, CancellationToken ct)
    {
        await links.RemoveAsync(contactId, linkId, ct);
        return NoContent();
    }
}
