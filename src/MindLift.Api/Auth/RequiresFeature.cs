using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MindLift.Application.Abstractions;

namespace MindLift.Api.Auth;

/// <summary>
/// Hides an endpoint, answering 404, while its feature is off for the caller's company. Use it next to the usual
/// <see cref="HasPermissionAttribute"/>, never instead of it: this only decides whether the feature exists for the company.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresFeatureAttribute : TypeFilterAttribute
{
    public RequiresFeatureAttribute(string key)
        : base(typeof(RequiresFeatureFilter))
    {
        Key = key;
        Arguments = [key];
    }

    public string Key { get; }
}

/// <summary>Runs after authorization, so an anonymous caller still gets 401 and never learns which features exist.</summary>
public sealed class RequiresFeatureFilter(string key, IFeatureFlags features) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!await features.IsEnabledAsync(key, context.HttpContext.RequestAborted))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
