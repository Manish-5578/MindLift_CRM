using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #39: LinkedIn profile lookup (linkedin-lookup).
/// Lookup person and company LinkedIn intelligence and apply to contacts, leads, and accounts.
/// </summary>
[ApiController]
[Route("api/v1/linkedin")]
[Authorize]
[RequiresFeature("linkedin-lookup")]
public sealed class LinkedInController(ILinkedInLookupService linkedInService) : ControllerBase
{
    [HttpPost("lookup/person")]
    public async Task<ActionResult<LinkedInPersonProfileDto>> LookupPerson(
        [FromBody] LookupLinkedInPersonRequest request,
        CancellationToken ct)
    {
        var profile = await linkedInService.LookupPersonAsync(request, ct);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPost("lookup/company")]
    public async Task<ActionResult<LinkedInCompanyProfileDto>> LookupCompany(
        [FromBody] LookupLinkedInCompanyRequest request,
        CancellationToken ct)
    {
        var profile = await linkedInService.LookupCompanyAsync(request, ct);
        if (profile == null) return NotFound();
        return Ok(profile);
    }

    [HttpPost("apply/contact")]
    public async Task<IActionResult> ApplyToContact(
        [FromBody] ApplyLinkedInToContactRequest request,
        CancellationToken ct)
    {
        await linkedInService.ApplyToContactAsync(request, ct);
        return Ok(new { success = true });
    }

    [HttpPost("apply/lead")]
    public async Task<IActionResult> ApplyToLead(
        [FromBody] ApplyLinkedInToLeadRequest request,
        CancellationToken ct)
    {
        await linkedInService.ApplyToLeadAsync(request, ct);
        return Ok(new { success = true });
    }

    [HttpPost("apply/account")]
    public async Task<IActionResult> ApplyToAccount(
        [FromBody] ApplyLinkedInToAccountRequest request,
        CancellationToken ct)
    {
        await linkedInService.ApplyToAccountAsync(request, ct);
        return Ok(new { success = true });
    }
}
