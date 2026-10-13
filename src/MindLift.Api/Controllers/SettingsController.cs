using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Common.Fields;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Workspace configuration (plan 7.2). Reads and writes need the Settings module, which only the Admin role has by default.
/// </summary>
[ApiController]
[Route("api/v1/settings")]
public sealed class SettingsController(
    CompanySettingsService company,
    FieldSettingsService fields,
    PicklistPipelineService picklistsAndPipelines) : ControllerBase
{
    // ---------- Company, currencies, modules ----------

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("company")]
    public async Task<ActionResult<CompanySettingsDto>> GetCompany(CancellationToken cancellationToken)
        => Ok(await company.GetCompanyAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("company")]
    public async Task<ActionResult<CompanySettingsDto>> UpdateCompany([FromBody] SaveCompanySettingsRequest request, CancellationToken cancellationToken)
        => Ok(await company.UpdateCompanyAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("currencies")]
    public async Task<ActionResult<CurrencySettingsDto>> GetCurrencies(CancellationToken cancellationToken)
        => Ok(await company.GetCurrenciesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("currencies")]
    public async Task<ActionResult<CurrencySettingsDto>> UpdateCurrencies([FromBody] SaveCurrencySettingsRequest request, CancellationToken cancellationToken)
        => Ok(await company.UpdateCurrenciesAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("exchange-rates")]
    public async Task<ActionResult<IReadOnlyList<ExchangeRateDto>>> ListRates(CancellationToken cancellationToken)
        => Ok(await company.ListExchangeRatesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("exchange-rates")]
    public async Task<ActionResult<ExchangeRateDto>> AddRate([FromBody] SaveExchangeRateRequest request, CancellationToken cancellationToken)
        => Ok(await company.AddExchangeRateAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("exchange-rates/{id:guid}")]
    public async Task<IActionResult> DeleteRate(Guid id, CancellationToken cancellationToken)
    {
        await company.DeleteExchangeRateAsync(id, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("modules")]
    public async Task<ActionResult<IReadOnlyList<ModuleSettingDto>>> ListModules(CancellationToken cancellationToken)
        => Ok(await company.ListModulesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("modules/{module}")]
    public async Task<ActionResult<ModuleSettingDto>> UpdateModule(string module, [FromBody] SaveModuleSettingRequest request, CancellationToken cancellationToken)
        => Ok(await company.UpdateModuleAsync(module, request, cancellationToken));

    // ---------- Fields, custom fields, layouts ----------

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("fields/{entity}")]
    public async Task<ActionResult<IReadOnlyList<FieldDto>>> ListFields(string entity, CancellationToken cancellationToken)
        => Ok(await fields.GetFieldsAsync(entity, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("fields/{entity}/{key}/override")]
    public async Task<ActionResult<FieldDto>> OverrideField(string entity, string key, [FromBody] SaveFieldOverrideRequest request, CancellationToken cancellationToken)
        => Ok(await fields.SetOverrideAsync(entity, key, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("custom-fields/{entity}")]
    public async Task<ActionResult<IReadOnlyList<CustomFieldDto>>> ListCustomFields(string entity, CancellationToken cancellationToken)
        => Ok(await fields.ListCustomFieldsAsync(entity, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("custom-fields")]
    public async Task<ActionResult<CustomFieldDto>> CreateCustomField([FromBody] SaveCustomFieldRequest request, CancellationToken cancellationToken)
        => Ok(await fields.CreateCustomFieldAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("custom-fields/{id:guid}")]
    public async Task<ActionResult<CustomFieldDto>> UpdateCustomField(Guid id, [FromBody] SaveCustomFieldRequest request, CancellationToken cancellationToken)
        => Ok(await fields.UpdateCustomFieldAsync(id, request, cancellationToken));

    /// <summary>purgeData=true also removes the field's values from every record. Without it, a field with data is refused.</summary>
    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("custom-fields/{id:guid}")]
    public async Task<IActionResult> DeleteCustomField(Guid id, [FromQuery] bool purgeData, CancellationToken cancellationToken)
    {
        await fields.DeleteCustomFieldAsync(id, purgeData, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("layouts/{entity}/{kind}")]
    public async Task<ActionResult<LayoutDto>> GetLayout(string entity, string kind, CancellationToken cancellationToken)
        => Ok(await fields.GetLayoutAsync(entity, kind, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("layouts/{entity}/{kind}")]
    public async Task<ActionResult<LayoutDto>> SaveLayout(string entity, string kind, [FromBody] SaveLayoutRequest request, CancellationToken cancellationToken)
        => Ok(await fields.SaveLayoutAsync(entity, kind, request, cancellationToken));

    // ---------- Picklists ----------

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("picklists")]
    public async Task<ActionResult<IReadOnlyList<PicklistDto>>> ListPicklists(CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.ListPicklistsAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("picklists/{key}/values")]
    public async Task<ActionResult<PicklistValueDto>> AddPicklistValue(string key, [FromBody] SavePicklistValueRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.AddValueAsync(key, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("picklists/{key}/values/{id:guid}")]
    public async Task<ActionResult<PicklistValueDto>> UpdatePicklistValue(string key, Guid id, [FromBody] SavePicklistValueRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.UpdateValueAsync(key, id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("picklists/{key}/values/{id:guid}")]
    public async Task<IActionResult> DeletePicklistValue(string key, Guid id, [FromQuery] string? replaceWith, CancellationToken cancellationToken)
    {
        await picklistsAndPipelines.DeleteValueAsync(key, id, replaceWith, cancellationToken);
        return NoContent();
    }

    // ---------- Pipelines and stages ----------

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("pipelines")]
    public async Task<ActionResult<IReadOnlyList<PipelineDto>>> ListPipelines(CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.ListPipelinesAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("pipelines")]
    public async Task<ActionResult<PipelineDto>> CreatePipeline([FromBody] SavePipelineRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.CreatePipelineAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("pipelines/{id:guid}")]
    public async Task<ActionResult<PipelineDto>> UpdatePipeline(Guid id, [FromBody] SavePipelineRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.UpdatePipelineAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("pipelines/{id:guid}")]
    public async Task<IActionResult> DeletePipeline(Guid id, [FromQuery] Guid? moveDealsToStageId, CancellationToken cancellationToken)
    {
        await picklistsAndPipelines.DeletePipelineAsync(id, moveDealsToStageId, cancellationToken);
        return NoContent();
    }

    [HasPermission(AppModule.Settings, PermissionAction.Create)]
    [HttpPost("pipelines/{id:guid}/stages")]
    public async Task<ActionResult<PipelineStageDto>> AddStage(Guid id, [FromBody] SaveStageRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.AddStageAsync(id, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("pipelines/{id:guid}/stages/{stageId:guid}")]
    public async Task<ActionResult<PipelineStageDto>> UpdateStage(Guid id, Guid stageId, [FromBody] SaveStageRequest request, CancellationToken cancellationToken)
        => Ok(await picklistsAndPipelines.UpdateStageAsync(id, stageId, request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("pipelines/{id:guid}/stages/{stageId:guid}")]
    public async Task<IActionResult> DeleteStage(Guid id, Guid stageId, [FromQuery] Guid? moveDealsToStageId, CancellationToken cancellationToken)
    {
        await picklistsAndPipelines.DeleteStageAsync(id, stageId, moveDealsToStageId, cancellationToken);
        return NoContent();
    }
}
