using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using MindLift.Application.Abstractions;

namespace MindLift.Api.Middleware;

/// <summary>
/// Sets the tenant and user for the request from the authenticated principal
/// (claims <c>tid</c> and <c>sub</c>, plan section 4.1.2 and 4.3).
/// Must run after authentication. Requests without a tenant claim run with no tenant (nothing is visible).
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, MindLift.Infrastructure.Caching.RequestStateCache state)
    {
        var user = context.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            var authResult = await context.AuthenticateAsync("Portal");
            if (authResult.Succeeded && authResult.Principal != null)
            {
                user = authResult.Principal;
                context.User = user;
            }
        }

        var tenantId = user.FindFirstValue("tid");
        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            await next(context);
            return;
        }

        if (tenant is MindLift.Infrastructure.Tenancy.TenantContext concrete)
        {
            concrete.SetPlatformAdmin(context.User.FindFirstValue("plat") == "true");
        }

        var userId = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid? userGuid = Guid.TryParse(userId, out var parsedUser) ? parsedUser : null;

        // A staff token stays valid only while the person is active and their session stamp is unchanged (password changed, signed out everywhere,
        // or deactivated). Without this a token would keep working until it expires. Portal tokens have no stamp and are checked by the portal.
        var stamp = context.User.FindFirstValue("sst");
        if (userGuid is { } staffId && stamp is not null)
        {
            var current = await state.UserAsync(staffId, context.RequestAborted);
            if (current is null || !current.IsActive || current.TenantId != tenantGuid || !string.Equals(current.SecurityStamp, stamp, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = 401,
                    Title = "Session ended",
                    Detail = "Your session has ended. Please sign in again.",
                    Extensions = { ["code"] = "session_revoked" },
                });
                return;
            }
        }

        using var scope = tenant.Use(tenantGuid, userGuid);
        await next(context);
    }
}
