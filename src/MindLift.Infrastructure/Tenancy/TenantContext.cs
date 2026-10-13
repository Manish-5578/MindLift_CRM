using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Tenancy;

/// <summary>
/// Scoped per request or job. Set by TenantResolutionMiddleware (from the JWT) or by <see cref="Use"/>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsPlatformAdmin { get; private set; }

    public IDisposable Use(Guid tenantId, Guid? userId = null)
    {
        var previousTenant = TenantId;
        var previousUser = UserId;

        TenantId = tenantId;
        UserId = userId;

        return new Scope(() =>
        {
            TenantId = previousTenant;
            UserId = previousUser;
        });
    }

    /// <summary>Set by authentication once the token's platform-admin claim is validated.</summary>
    public void SetPlatformAdmin(bool isPlatformAdmin) => IsPlatformAdmin = isPlatformAdmin;

    private sealed class Scope(Action restore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                restore();
            }
        }
    }
}
