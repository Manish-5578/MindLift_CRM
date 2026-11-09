using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// Phase 6 / #38: Customer health, NPS and CSAT (customer-health).
/// Survey ingress, sentiment analytics, and account health scoring.
/// </summary>
[ApiController]
[Route("api/v1/customer-health")]
[Authorize]
[RequiresFeature("customer-health")]
public sealed class CustomerHealthController(ICustomerHealthService healthService) : ControllerBase
{
    [HasPermission(AppModule.Accounts, PermissionAction.Create)]
    [HttpPost("surveys")]
    public async Task<ActionResult<CustomerSurveyDto>> SubmitSurvey(
        [FromBody] SubmitSurveyRequest request,
        CancellationToken ct)
    {
        var survey = await healthService.SubmitSurveyAsync(request, ct);
        return Created($"/api/v1/customer-health/surveys/{survey.Id}", survey);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("surveys")]
    public async Task<ActionResult<IReadOnlyList<CustomerSurveyDto>>> GetSurveys(
        [FromQuery] string? type = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] Guid? contactId = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var surveys = await healthService.GetSurveysAsync(type, accountId, contactId, limit, ct);
        return Ok(surveys);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("surveys/summary")]
    public async Task<ActionResult<SurveySummaryDto>> GetSurveySummary(
        [FromQuery] string? type = null,
        [FromQuery] Guid? accountId = null,
        CancellationToken ct = default)
    {
        var summary = await healthService.GetSurveySummaryAsync(type, accountId, ct);
        return Ok(summary);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("accounts/{id:guid}")]
    public async Task<ActionResult<AccountHealthScoreDto>> GetAccountHealth(
        Guid id,
        CancellationToken ct)
    {
        var health = await healthService.GetAccountHealthAsync(id, ct);
        return Ok(health);
    }

    [HasPermission(AppModule.Accounts, PermissionAction.View)]
    [HttpGet("portfolio")]
    public async Task<ActionResult<PortfolioHealthOverviewDto>> GetPortfolioOverview(
        CancellationToken ct)
    {
        var overview = await healthService.GetPortfolioOverviewAsync(ct);
        return Ok(overview);
    }
}
