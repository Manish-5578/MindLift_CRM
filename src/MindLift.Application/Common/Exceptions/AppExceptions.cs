namespace MindLift.Application.Common.Exceptions;

/// <summary>
/// Base type for errors the API maps to RFC 7807 ProblemDetails.
/// <see cref="Code"/> is a stable machine-readable value the client maps to localized text (plan section 4.13).
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>404. The message never reveals whether a record exists in another tenant.</summary>
public sealed class NotFoundException(string entityName, object? id)
    : AppException("not_found", $"{entityName} '{id}' was not found.")
{
}

/// <summary>403.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to do this.", string code = "forbidden")
    : AppException(code, message)
{
}

/// <summary>409. For example a duplicate unique value or a stale version.</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message)
{
}

/// <summary>400. Field-level validation errors keyed by field name.</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation_failed", "One or more fields are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public ValidationException(string message)
        : this(new Dictionary<string, string[]> { ["general"] = [message] })
    {
    }

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}

/// <summary>401. Failed sign-in, expired or reused tokens. The message is deliberately generic.</summary>
public sealed class UnauthorizedException(string code, string message) : AppException(code, message)
{
}

/// <summary>402. The tenant's plan does not allow the action (for example too many users).</summary>
public sealed class PlanLimitException(string code, string message) : AppException(code, message)
{
}

/// <summary>422. The request is well formed but breaks a business rule, for example "lost reason is required".</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message)
{
}
