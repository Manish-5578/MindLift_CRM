using MindLift.Application.Abstractions;

namespace MindLift.Api.Middleware;

/// <summary>Adds the company and the person to every log line written while the request runs, and to any error sent to Sentry.</summary>
public sealed class RequestLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        var tenantId = tenant.TenantId?.ToString() ?? "-";
        var userId = tenant.UserId?.ToString() ?? "-";
        Sentry.SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("tenant_id", tenantId);
            scope.User = new Sentry.SentryUser { Id = userId };
        });

        using (Serilog.Context.LogContext.PushProperty("TenantId", tenantId))
        using (Serilog.Context.LogContext.PushProperty("UserId", userId))
        {
            await next(context);
        }
    }
}
