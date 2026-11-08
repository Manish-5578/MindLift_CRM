namespace MindLift.Application.Abstractions;

/// <summary>
/// The tenant and user the current request, job or command runs for.
/// Every tenant-owned query is filtered by <see cref="TenantId"/>; with no tenant set, nothing is returned.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid? UserId { get; }
    string? UserName { get; }
    string? UserEmail { get; }
    string? IpAddress { get; }
    bool IsPlatformAdmin { get; }

    /// <summary>"mobile" when the request comes from the mobile app (X-Client header), otherwise null.</summary>
    string? Client => null;

    /// <summary>True once an audit row was written for the current request.</summary>
    bool AuditedThisRequest => false;

    void MarkAudited() { }

    /// <summary>
    /// Sets the tenant (and optionally the user) until the returned scope is disposed.
    /// Used by request middleware, background jobs, webhooks and public endpoints.
    /// </summary>
    IDisposable Use(Guid tenantId, Guid? userId = null, string? userName = null, string? userEmail = null, string? ipAddress = null);
}
