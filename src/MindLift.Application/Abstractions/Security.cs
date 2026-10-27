using MindLift.Shared.Enums;

namespace MindLift.Application.Abstractions;

/// <summary>What the current user may do in one module, as resolved from their role.</summary>
public sealed record ModulePermission(
    AppModule Module,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete,
    bool CanExport,
    bool CanImport,
    DataScope Scope);

/// <summary>Checks the current user's permissions (plan 4.4.4). Implemented in Infrastructure with a cache.</summary>
public interface IPermissionService
{
    /// <summary>True when the module is enabled for the tenant and the user's role allows the action.</summary>
    Task<bool> HasAsync(AppModule module, PermissionAction action, CancellationToken cancellationToken = default);

    /// <summary>The current user's permission for a module, or null when the user has none.</summary>
    Task<ModulePermission?> GetModulePermissionAsync(AppModule module, CancellationToken cancellationToken = default);

    /// <summary>Removes cached permissions for a role after it changes.</summary>
    Task InvalidateRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
}

/// <summary>Row-level scope: which owners the current user may see (plan 4.4.5).</summary>
public interface IDataScopeService
{
    /// <summary>Owner ids in scope, or null when the scope is All (no filter needed).</summary>
    Task<IReadOnlyList<Guid>?> GetScopeUserIdsAsync(AppModule module, CancellationToken cancellationToken = default);

    /// <summary>The people a user's Team scope covers: the user, their team's members and everyone who reports to them.</summary>
    Task<IReadOnlyList<Guid>> TeamOfAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Sends transactional email (plan 20.8, 4.3). Implemented with SMTP (Mailpit in development).</summary>
/// <summary>A file sent with an email, for example a calendar invitation.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default);

    /// <summary>Sends a message with an HTML version beside the text one. Senders that cannot send HTML send the text only.</summary>
    Task SendHtmlAsync(string to, string subject, string textBody, string htmlBody, CancellationToken cancellationToken = default)
        => SendAsync(to, subject, textBody, cancellationToken);

    /// <summary>Sends an email with attachments. Senders that cannot attach files send the text only.</summary>
    Task SendAsync(string to, string subject, string textBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken = default)
        => SendAsync(to, subject, textBody, cancellationToken);
}

/// <summary>Enforces the tenant's plan limits (plan 6.9).</summary>
public interface IPlanLimitService
{
    /// <summary>Throws PlanLimitException when no more active seats (users plus pending invitations) are available.</summary>
    Task EnsureSeatAvailableAsync(CancellationToken cancellationToken = default);
}
