using Microsoft.AspNetCore.Mvc;

namespace MindLift.Api.Middleware;

/// <summary>
/// If a user signed in with a temporary password (mcp claim = true),
/// they are only allowed to access auth endpoints (/api/v1/auth/*), bootstrap (/api/v1/app/bootstrap), and hubs.
/// Any other API call returns 403 Forbidden with code 'password_change_required'.
/// </summary>
public sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.HasClaim("mcp", "true"))
        {
            var path = context.Request.Path;
            var exempt = path.StartsWithSegments("/api/v1/auth") ||
                         path.StartsWithSegments("/api/v1/app/bootstrap") ||
                         path.StartsWithSegments("/hubs");

            if (!exempt && path.StartsWithSegments("/api/v1"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Password change required",
                    Detail = "You must change your temporary password before accessing the system.",
                    Extensions = { ["code"] = "password_change_required" },
                });
                return;
            }
        }

        await next(context);
    }
}
