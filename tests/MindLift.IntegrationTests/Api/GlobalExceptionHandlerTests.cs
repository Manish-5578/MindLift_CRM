using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using MindLift.Api.Middleware;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

public sealed class GlobalExceptionHandlerTests
{
    [Theory]
    [InlineData(404, "not_found")]
    [InlineData(403, "forbidden")]
    [InlineData(409, "conflict")]
    [InlineData(422, "lost_reason_required")]
    public async Task Application_exceptions_map_to_the_expected_status_and_code(int status, string code)
    {
        var handler = new GlobalExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        Exception exception = status switch
        {
            404 => new MindLift.Application.Common.Exceptions.NotFoundException("Lead", Guid.Empty),
            403 => new MindLift.Application.Common.Exceptions.ForbiddenException(),
            409 => new MindLift.Application.Common.Exceptions.ConflictException(code, "Duplicate."),
            _ => new MindLift.Application.Common.Exceptions.BusinessRuleException(code, "Rule broken."),
        };

        await handler.TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.StatusCode.ShouldBe(status);
        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        doc.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_problem_details_without_stack_trace()
    {
        var handler = new GlobalExceptionHandler(Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("secret detail"), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe((int)HttpStatusCode.InternalServerError);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        doc.RootElement.GetProperty("title").GetString().ShouldBe("Something went wrong");
        doc.RootElement.GetProperty("detail").GetString()!.ShouldNotContain("secret detail");
    }
}

