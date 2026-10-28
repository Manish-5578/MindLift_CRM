using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Infrastructure.Onboarding;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>
/// The setup wizard (plan 6.3). Owners and Admins only: every endpoint needs the Settings module.
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
public sealed class OnboardingController(OnboardingService onboarding) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<OnboardingDto>> Get(CancellationToken cancellationToken)
        => Ok(await onboarding.GetAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("company")]
    public async Task<ActionResult<OnboardingDto>> SaveCompany([FromBody] OnboardingCompanyRequest request, CancellationToken cancellationToken)
        => Ok(await onboarding.SaveCompanyAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet("industries")]
    public ActionResult<IReadOnlyList<IndustrySummaryDto>> ListIndustries()
        => Ok(onboarding.ListIndustries());

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("industry")]
    public async Task<ActionResult<OnboardingDto>> ApplyIndustry([FromBody] ApplyIndustryRequest request, CancellationToken cancellationToken)
        => Ok(await onboarding.ApplyIndustryAsync(request.IndustryKey, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("pipeline")]
    public async Task<ActionResult<OnboardingDto>> SavePipeline([FromBody] SaveOnboardingPipelineRequest request, CancellationToken cancellationToken)
        => Ok(await onboarding.SavePipelineAsync(request, cancellationToken));

    [HasPermission(AppModule.Users, PermissionAction.Create)]
    [HttpPost("team")]
    public async Task<ActionResult<OnboardingDto>> InviteTeam([FromBody] OnboardingTeamRequest request, CancellationToken cancellationToken)
        => Ok(await onboarding.InviteTeamAsync(request, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("data")]
    public async Task<ActionResult<OnboardingDto>> ChooseData([FromBody] OnboardingDataRequest request, CancellationToken cancellationToken)
        => Ok(await onboarding.ChooseDataAsync(request.Mode, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("skip/{step:int}")]
    public async Task<ActionResult<OnboardingDto>> Skip(int step, CancellationToken cancellationToken)
        => Ok(await onboarding.SkipAsync(step, cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("complete")]
    public async Task<ActionResult<OnboardingDto>> Complete(CancellationToken cancellationToken)
        => Ok(await onboarding.CompleteAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Delete)]
    [HttpDelete("sample-data")]
    public async Task<ActionResult<SampleDataDto>> RemoveSampleData(CancellationToken cancellationToken)
        => Ok(await onboarding.RemoveSampleDataAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPost("checklist/dismiss")]
    public async Task<IActionResult> DismissChecklist(CancellationToken cancellationToken)
    {
        await onboarding.DismissChecklistAsync(cancellationToken);
        return NoContent();
    }
}
