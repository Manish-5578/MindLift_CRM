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
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
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

        using var scope = tenant.Use(tenantGuid, userGuid);
        await next(context);
    }
}
