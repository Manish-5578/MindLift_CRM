using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MindLift.Infrastructure.Platform;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>The support tab's side of a support view: opening it with the one-time code, and ending it.</summary>
[ApiController]
[Route("api/v1/support")]
public sealed class SupportController(SupportAccessService support) : ControllerBase
{
    [HttpPost("exchange")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public")]
    public async Task<ActionResult<TokenResponse>> Exchange([FromBody] SupportExchangeRequest request, CancellationToken cancellationToken)
        => Ok(await support.ExchangeAsync(request.Code, cancellationToken));

    [HttpPost("end")]
    [Authorize]
    public async Task<IActionResult> End(CancellationToken cancellationToken)
    {
        if (User.FindFirst("imp")?.Value is not { } value || !Guid.TryParse(value, out var sessionId))
        {
            return BadRequest();
        }

        await support.EndAsync(sessionId, cancellationToken);
        return NoContent();
    }
}
