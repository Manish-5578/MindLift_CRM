using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MindLift.Application.Common.Exceptions;

namespace MindLift.Api.Middleware;

/// <summary>
/// Maps application exceptions to RFC 7807 ProblemDetails (plan section 3):
/// NotFound → 404, Forbidden → 403, Conflict → 409, BusinessRule → 422, Validation → 400, everything else → 500.
/// Stack traces and internal messages are never returned for 500 errors (plan section 12).
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            NotFoundException e => Problem(StatusCodes.Status404NotFound, "Not found", e.Message, e.Code),
            UnauthorizedException e => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", e.Message, e.Code),
            ForbiddenException e => Problem(StatusCodes.Status403Forbidden, "Forbidden", e.Message, e.Code),
            PlanLimitException e => Problem(StatusCodes.Status402PaymentRequired, "Plan limit reached", e.Message, e.Code),
            ConflictException e => Problem(StatusCodes.Status409Conflict, "Conflict", e.Message, e.Code),
            BusinessRuleException e => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Message, e.Code),
            ValidationException e => ValidationProblem(e),
            _ => UnexpectedProblem(exception),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            Sentry.SentrySdk.CaptureException(exception);   // does nothing unless Sentry:Dsn is set
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ProblemDetails Problem(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = $"https://httpstatuses.io/{status}",
        Extensions = { ["code"] = code },
    };

    private static ProblemDetails ValidationProblem(ValidationException exception)
    {
        var problem = Problem(StatusCodes.Status400BadRequest, "Validation failed", exception.Message, exception.Code);
        problem.Extensions["errors"] = exception.Errors;
        return problem;
    }

    private static ProblemDetails UnexpectedProblem(Exception _) => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "Something went wrong",
        Detail = "An unexpected error occurred. Please try again.",
        Type = "https://httpstatuses.io/500",
        Extensions = { ["code"] = "internal_error" },
    };
}
