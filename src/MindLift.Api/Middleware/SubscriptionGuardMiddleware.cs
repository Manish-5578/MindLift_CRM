using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;

namespace MindLift.Api.Middleware;

/// <summary>
/// Refuses writes for workspaces whose subscription has lapsed (plan 6.9): PastDue, Cancelled and a trial that has ended return 402,
/// Suspended returns 403. Reads keep working so the owner can still see their data and pay.
/// Sign-in, sign-up and platform administration are never blocked.
/// </summary>
public sealed class SubscriptionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, AppDbContext db, IClock clock)
    {
        var path = context.Request.Path;
        var isWrite = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
        var exempt = path.StartsWithSegments("/api/v1/auth") || path.StartsWithSegments("/api/v1/platform");

        if (isWrite && !exempt && path.StartsWithSegments("/api/v1") && tenant.TenantId is { } tenantId)
        {
            var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync();

            // A trial past its end date counts as lapsed straight away, without waiting for the daily job to move it to PastDue.
            if (workspace is not null && workspace.Status is not "Suspended" && TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow))
            {
                await Refuse(context, StatusCodes.Status402PaymentRequired, "subscription_required",
                    "Your subscription has ended. Choose a plan to make changes. Your data is still available to view.");
                return;
            }

            if (workspace?.Status is "Suspended")
            {
                await Refuse(context, StatusCodes.Status403Forbidden, "tenant_suspended", "Your workspace is suspended. Contact your administrator.");
                return;
            }
        }

        await next(context);
    }

    private static async Task Refuse(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = status == 402 ? "Subscription required" : "Workspace suspended",
            Detail = detail,
            Extensions = { ["code"] = code },
        });
    }
}
