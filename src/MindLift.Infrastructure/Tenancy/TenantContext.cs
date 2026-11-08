using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Tenancy;

/// <summary>
/// Scoped per request or job. Set by TenantResolutionMiddleware (from the JWT) or by <see cref="Use"/>.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? UserEmail { get; private set; }
    public string? IpAddress { get; private set; }
    public bool IsPlatformAdmin { get; private set; }
    public string? Client { get; private set; }
    public bool AuditedThisRequest { get; private set; }

    public void MarkAudited() => AuditedThisRequest = true;
    public void SetClient(string? client) => Client = client;

    public IDisposable Use(Guid tenantId, Guid? userId = null, string? userName = null, string? userEmail = null, string? ipAddress = null)
    {
        var previousTenant = TenantId;
        var previousUser = UserId;
        var previousName = UserName;
        var previousEmail = UserEmail;
        var previousIp = IpAddress;

        TenantId = tenantId;
        UserId = userId;
        UserName = userName ?? previousName;
        UserEmail = userEmail ?? previousEmail;
        IpAddress = ipAddress ?? previousIp;

        return new Scope(() =>
        {
            TenantId = previousTenant;
            UserId = previousUser;
            UserName = previousName;
            UserEmail = previousEmail;
            IpAddress = previousIp;
        });
    }

    /// <summary>Set by authentication once the token's platform-admin claim is validated.</summary>
    public void SetPlatformAdmin(bool isPlatformAdmin) => IsPlatformAdmin = isPlatformAdmin;

    /// <summary>Set by TenantResolutionMiddleware from the authenticated user and request.</summary>
    public void SetUserInfo(string? userName, string? userEmail, string? ipAddress)
    {
        UserName = userName;
        UserEmail = userEmail;
        IpAddress = ipAddress;
    }

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
