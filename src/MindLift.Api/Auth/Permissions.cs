using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Shared.Enums;

namespace MindLift.Api.Auth;

/// <summary>
/// Requires a permission on a module, for example <c>[HasPermission(AppModule.Leads, PermissionAction.Create)]</c> (plan 4.4.4).
/// The check runs in <see cref="PermissionHandler"/> through the policy <c>perm:{module}:{action}</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class HasPermissionAttribute(AppModule module, PermissionAction action)
    : AuthorizeAttribute($"perm:{module}:{action}");

/// <summary>Requirement that the current user holds a permission.</summary>
public sealed class PermissionRequirement(AppModule module, PermissionAction action) : IAuthorizationRequirement
{
    public AppModule Module { get; } = module;
    public PermissionAction Action { get; } = action;
}

/// <summary>Grants access when the current user holds the required permission.</summary>
public sealed class PermissionHandler(IPermissionService permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await permissions.HasAsync(requirement.Module, requirement.Action))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Builds policies for <c>perm:…</c> names on the fly, and delegates every other policy to the default provider.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private const string Prefix = "perm:";
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var parts = policyName.Split(':');
        if (parts.Length != 3
            || !Enum.TryParse<AppModule>(parts[1], out var module)
            || !Enum.TryParse<PermissionAction>(parts[2], out var action))
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        var policy = new AuthorizationPolicyBuilder(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(module, action))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}
