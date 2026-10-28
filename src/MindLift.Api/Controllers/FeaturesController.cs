using Microsoft.AspNetCore.Mvc;
using MindLift.Api.Auth;
using MindLift.Application.Abstractions;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Api.Controllers;

/// <summary>Lets a company owner see and switch the new capabilities. Everything starts OFF.</summary>
[ApiController]
[Route("api/v1/features")]
public sealed class FeaturesController(IFeatureFlags features) : ControllerBase
{
    [HasPermission(AppModule.Settings, PermissionAction.View)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FeatureFlagDto>>> List(CancellationToken cancellationToken)
        => Ok(await features.ListAsync(cancellationToken));

    [HasPermission(AppModule.Settings, PermissionAction.Edit)]
    [HttpPut("{key}")]
    public async Task<ActionResult<FeatureFlagDto>> Set(string key, [FromBody] SetFeatureRequest request, CancellationToken cancellationToken)
        => Ok(await features.SetAsync(key, request.Enabled, cancellationToken));
}
