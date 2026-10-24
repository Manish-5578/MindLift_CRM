using MindLift.Infrastructure.Platform;

namespace MindLift.Api.Middleware;

/// <summary>
/// For a support view (a token with an "imp" claim): the session must still be open, nothing may be changed, and every data page read is
/// added to the session's record. Ending the session is the one change allowed.
/// </summary>
public sealed class SupportSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SupportAccessService support)
    {
        if (context.User.FindFirst("imp")?.Value is { } value && Guid.TryParse(value, out var sessionId))
        {
            var path = context.Request.Path;
            // Lists are read with a POST to .../query and reports with .../run; those change nothing, so a support view may use them.
            var readingPost = HttpMethods.IsPost(context.Request.Method)
                && (path.Value?.EndsWith("/query", StringComparison.OrdinalIgnoreCase) == true
                    || (path.StartsWithSegments("/api/v1/reports") && path.Value?.EndsWith("/run", StringComparison.OrdinalIgnoreCase) == true));
            var isRead = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || readingPost;
            var isEnd = path.StartsWithSegments("/api/v1/support/end");
            var record = isRead && path.StartsWithSegments("/api/v1") && !path.StartsWithSegments("/api/v1/notifications") ? path.Value : null;

            if (!await support.TouchAsync(sessionId, record, context.RequestAborted))
            {
                await Refuse(context, StatusCodes.Status401Unauthorized, "support_ended", "This support session has ended.");
                return;
            }

            if (!isRead && !isEnd && path.StartsWithSegments("/api"))
            {
                await Refuse(context, StatusCodes.Status403Forbidden, "support_read_only", "A support view is read-only. Nothing can be changed from it.");
                return;
            }
        }

        await next(context);
    }

    private static Task Refuse(HttpContext context, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = status, Title = "Support view", Detail = detail, Extensions = { ["code"] = code } });
    }
}
