using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Notifications;
using MindLift.Infrastructure.Leads;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
public sealed class LeadsController(LeadService leads, ImportNotifier imports) : ControllerBase
{
    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [HttpPost("query")]
    public async Task<ActionResult<PagedResult<LeadListItemDto>>> Query([FromBody] ListQuery query, CancellationToken cancellationToken)
        => Ok(await leads.QueryAsync(query, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LeadDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await leads.GetAsync(id, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Create)]
    [HttpPost]
    public async Task<ActionResult<LeadDto>> Create([FromBody] SaveLeadRequest request, CancellationToken cancellationToken)
        => Ok(await leads.CreateAsync(request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<LeadDto>> Update(Guid id, [FromBody] SaveLeadRequest request, CancellationToken cancellationToken)
        => Ok(await leads.UpdateAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Delete)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await leads.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Change many leads at once (the list's bulk bar). Each one is checked and saved as if edited on its own page.</summary>
    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPost("bulk")]
    public async Task<ActionResult<BulkActionResult>> Bulk([FromBody] BulkActionRequest request, CancellationToken cancellationToken)
        => Ok(await leads.BulkAsync(request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Delete)]
    [HttpPost("bulk-delete")]
    public async Task<ActionResult<BulkActionResult>> BulkDelete([FromBody] BulkDeleteRequest request, CancellationToken cancellationToken)
        => Ok(await leads.BulkDeleteAsync(request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPost("{id:guid}/disqualify")]
    public async Task<ActionResult<LeadDto>> Disqualify(Guid id, [FromBody] DisqualifyLeadRequest request, CancellationToken cancellationToken)
        => Ok(await leads.DisqualifyAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Edit)]
    [HttpPost("{id:guid}/convert")]
    public async Task<ActionResult<ConvertLeadResult>> Convert(Guid id, [FromBody] ConvertLeadRequest request, CancellationToken cancellationToken)
        => Ok(await leads.ConvertAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [RequiresFeature("lead-conversion")]
    [HttpGet("{id:guid}/convert-options")]
    public async Task<ActionResult<LeadConvertOptionsDto>> GetConvertOptions(Guid id, CancellationToken cancellationToken)
        => Ok(await leads.GetConvertOptionsAsync(id, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Create)]
    [HttpPost("check-duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicateCandidateDto>>> CheckDuplicates([FromBody] CheckDuplicatesRequest request, CancellationToken cancellationToken)
        => Ok(await leads.CheckDuplicatesAsync(request, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.View)]
    [RequiresFeature("duplicate-merge")]
    [HttpGet("{id:guid}/duplicates")]
    public async Task<ActionResult<IReadOnlyList<DuplicateCandidateDto>>> GetDuplicates(Guid id, CancellationToken cancellationToken)
        => Ok(await leads.GetDuplicatesForLeadAsync(id, cancellationToken));

    [HasPermission(AppModule.Leads, PermissionAction.Delete)]
    [HttpPost("{id:guid}/merge")]
    public async Task<ActionResult<LeadDto>> Merge(Guid id, [FromBody] MergeLeadsRequest request, CancellationToken cancellationToken)
        => Ok(await leads.MergeAsync(id, request.DuplicateIds, request.FieldOverrides, cancellationToken));

    /// <summary>CSV body (text/csv). Max 5,000 rows.</summary>
    [HasPermission(AppModule.Leads, PermissionAction.Import)]
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var csv = await reader.ReadToEndAsync(cancellationToken);
        var result = await leads.ImportCsvAsync(csv, cancellationToken);
        await imports.NotifyAsync("leads", "/leads", result, cancellationToken);
        return Ok(result);
    }

    [HasPermission(AppModule.Leads, PermissionAction.Export)]
    [HttpPost("export")]
    public async Task<IActionResult> Export([FromBody] ListQuery query, CancellationToken cancellationToken)
    {
        var csv = await leads.ExportCsvAsync(query, cancellationToken);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", "leads.csv");
    }
}

/// <summary>Assignment rules and web forms (settings, Admin by default).</summary>
[ApiController]
[Route("api/v1/settings")]
public sealed class LeadSettingsController(LeadSettingsService settings) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("assignment-rules")]
    public async Task<ActionResult<IReadOnlyList<AssignmentRuleDto>>> ListRules(CancellationToken cancellationToken)
        => Ok(await settings.ListRulesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("assignment-rules")]
    public async Task<ActionResult<AssignmentRuleDto>> CreateRule([FromBody] SaveAssignmentRuleRequest request, CancellationToken cancellationToken)
        => Ok(await settings.SaveRuleAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("assignment-rules/{id:guid}")]
    public async Task<ActionResult<AssignmentRuleDto>> UpdateRule(Guid id, [FromBody] SaveAssignmentRuleRequest request, CancellationToken cancellationToken)
        => Ok(await settings.SaveRuleAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("assignment-rules/{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id, CancellationToken cancellationToken)
    {
        await settings.DeleteRuleAsync(id, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("web-forms")]
    public async Task<ActionResult<IReadOnlyList<WebFormDto>>> ListForms(CancellationToken cancellationToken)
        => Ok(await settings.ListFormsAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("web-forms")]
    public async Task<ActionResult<WebFormDto>> CreateForm([FromBody] SaveWebFormRequest request, CancellationToken cancellationToken)
        => Ok(await settings.SaveFormAsync(null, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("web-forms/{id:guid}")]
    public async Task<ActionResult<WebFormDto>> UpdateForm(Guid id, [FromBody] SaveWebFormRequest request, CancellationToken cancellationToken)
        => Ok(await settings.SaveFormAsync(id, request, cancellationToken));
}

/// <summary>Public endpoint for website forms. Anonymous; the token selects the workspace. Not behind the subscription guard for reads.</summary>
[ApiController]
[Route("api/v1/public/forms")]
[EnableRateLimiting("public")]
public sealed class PublicFormsController(PublicFormService forms) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("{token}")]
    public async Task<ActionResult<PublicFormResult>> Submit(string token, [FromBody] PublicFormSubmission submission, CancellationToken cancellationToken)
        => Ok(await forms.SubmitAsync(token, submission, cancellationToken));
}
