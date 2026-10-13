namespace MindLift.Application.Abstractions;

/// <summary>
/// The tenant and user the current request, job or command runs for.
/// Every tenant-owned query is filtered by <see cref="TenantId"/>; with no tenant set, nothing is returned.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    bool IsPlatformAdmin { get; }

    /// <summary>
    /// Sets the tenant (and optionally the user) until the returned scope is disposed.
    /// Used by request middleware, background jobs, webhooks and public endpoints.
    /// </summary>
    IDisposable Use(Guid tenantId, Guid? userId = null);
}
