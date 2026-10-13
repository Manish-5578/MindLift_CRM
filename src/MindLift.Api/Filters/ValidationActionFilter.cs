using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.Filters;
namespace MindLift.Api.Filters;

/// <summary>
/// Runs every registered FluentValidation <c>IValidator&lt;T&gt;</c> for each action argument before the action.
/// Failures become a <see cref="ValidationException"/>, which is returned as HTTP 400 (plan section 3).
/// </summary>
public sealed class ValidationActionFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var argument in context.ActionArguments.Values)
        {
            // Only reference-type arguments (DTOs) can have validators. Skips CancellationToken, Guid, int and so on.
            if (argument is null || argument.GetType().IsValueType)
            {
                continue;
            }

            var method = typeof(ValidationActionFilter)
                .GetMethod(nameof(ValidateArgument), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(argument.GetType());

            var result = await (Task<IReadOnlyDictionary<string, string[]>?>)method.Invoke(null, [services, argument])!;
            if (result is null)
            {
                continue;
            }

            foreach (var (key, messages) in result)
            {
                errors[key] = errors.TryGetValue(key, out var existing) ? [.. existing, .. messages] : messages;
            }
        }

        if (errors.Count > 0)
        {
            throw new MindLift.Application.Common.Exceptions.ValidationException(errors);
        }

        await next();
    }

    private static async Task<IReadOnlyDictionary<string, string[]>?> ValidateArgument<T>(IServiceProvider services, object argument)
        where T : class
    {
        if (services.GetService(typeof(IValidator<T>)) is not IValidator<T> validator)
        {
            return null;
        }

        ValidationResult result = await validator.ValidateAsync((T)argument);
        if (result.IsValid)
        {
            return null;
        }

        return result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
    }
}
