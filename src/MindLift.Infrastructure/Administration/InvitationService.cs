using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Administration;

/// <summary>
/// Invites people to a workspace (plan 7.1, 6.3). Invitations expire after 7 days.
/// Accepting an invitation creates the user, so it must be done before the tenant is known (anonymous).
/// </summary>
public sealed class InvitationService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantContext tenant,
    IClock clock,
    IEmailSender email,
    IPlanLimitService planLimits,
    AuthService auth,
    IOptions<AppOptions> app)
{
    public const int ValidDays = 7;

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Invitations.AsNoTracking().OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<InvitationDto> CreateAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = users.NormalizeEmail(request.Email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, cancellationToken))
        {
            throw new ConflictException("email_taken", "An account with this email already exists.");
        }

        if (await db.Invitations.AnyAsync(i => i.NormalizedEmail == normalized && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > clock.UtcNow, cancellationToken))
        {
            throw new ConflictException("invitation_pending", "This person already has a pending invitation.");
        }

        await planLimits.EnsureSeatAvailableAsync(cancellationToken);
        var role = await EnsureRoleAndTeamAsync(request.RoleId, request.TeamId, cancellationToken);
        await EnsureCallerCanGrantRoleAsync(role, cancellationToken);

        var raw = TokenService.NewRefreshToken();
        var invitation = new Invitation
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            RoleId = request.RoleId,
            TeamId = request.TeamId,
            TokenHash = TokenService.Hash(raw),
            ExpiresAt = clock.UtcNow.AddDays(ValidDays),
            InvitedById = tenant.UserId ?? Guid.Empty,
        };

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        await SendInviteEmailAsync(invitation, raw, cancellationToken);
        return Map(invitation);
    }

    public async Task<InvitationDto> ResendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException("Invitation", id);

        if (invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
        {
            throw new ConflictException("invitation_closed", "This invitation has already been accepted or revoked.");
        }

        var role = await EnsureRoleAndTeamAsync(invitation.RoleId, invitation.TeamId, cancellationToken);
        await EnsureCallerCanGrantRoleAsync(role, cancellationToken);

        var raw = TokenService.NewRefreshToken();
        invitation.TokenHash = TokenService.Hash(raw);
        invitation.ExpiresAt = clock.UtcNow.AddDays(ValidDays);
        await db.SaveChangesAsync(cancellationToken);
        await SendInviteEmailAsync(invitation, raw, cancellationToken);
        return Map(invitation);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new NotFoundException("Invitation", id);

        var role = await EnsureRoleAndTeamAsync(invitation.RoleId, invitation.TeamId, cancellationToken);
        if (role.IsAdmin && !await IsCallerAdminAsync(cancellationToken))
        {
            throw new ForbiddenException("Only administrators can revoke invitations with the Admin role.", "admin_role_required");
        }

        invitation.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Shows the invitation to the person who clicked the link. Only shows what they need to decide.</summary>
    public async Task<InvitationPreview> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var invitation = await FindValidAsync(token, cancellationToken);
        var workspace = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == invitation.TenantId, cancellationToken);
        return new InvitationPreview(invitation.Email, invitation.FirstName, invitation.LastName, workspace.Name, invitation.ExpiresAt);
    }

    /// <summary>Creates the user in the inviting tenant and signs them in.</summary>
    public async Task<AuthResult> AcceptAsync(string token, string password, CancellationToken cancellationToken = default)
    {
        var invitation = await FindValidAsync(token, cancellationToken);
        using var scope = tenant.Use(invitation.TenantId);

        var now = clock.UtcNow;
        var claimed = await db.Invitations.IgnoreQueryFilters()
            .Where(i => i.Id == invitation.Id && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now && !i.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AcceptedAt, now), cancellationToken);
        if (claimed == 0)
        {
            throw new ConflictException("invitation_accepted", "This invitation has already been used or expired.");
        }

        await planLimits.EnsureSeatAvailableAsync(cancellationToken);

        var assignedRole = await db.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == invitation.RoleId && r.TenantId == invitation.TenantId && !r.IsDeleted, cancellationToken)
            ?? throw new ConflictException("role_invalid", "The role assigned to this invitation is no longer valid.");

        if (invitation.TeamId is { } teamId)
        {
            var teamValid = await db.Teams.IgnoreQueryFilters()
                .AnyAsync(t => t.Id == teamId && t.TenantId == invitation.TenantId && !t.IsDeleted, cancellationToken);
            if (!teamValid)
            {
                invitation.TeamId = null;
            }
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = invitation.TenantId,
            UserName = invitation.Email,
            Email = invitation.Email,
            FirstName = invitation.FirstName,
            LastName = invitation.LastName,
            RoleId = assignedRole.Id,
            TeamId = invitation.TeamId,
            IsActive = true,
            EmailConfirmed = true,
            LockoutEnabled = true,
            CreatedAt = clock.UtcNow,
        };

        try
        {
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                {
                    throw new ConflictException("invitation_accepted", "This invitation has already been accepted.");
                }

                throw new ValidationException(new Dictionary<string, string[]>
                {
                    ["password"] = result.Errors.Select(e => e.Description).ToArray(),
                });
            }
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("invitation_accepted", "This invitation has already been accepted.");
        }

        return await auth.IssueForUserAsync(user, cancellationToken);
    }

    private async Task<Invitation> FindValidAsync(string token, CancellationToken cancellationToken)
    {
        var hash = TokenService.Hash(token);
        var invitation = await db.Invitations.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken)
            ?? throw new UnauthorizedException("invitation_invalid", "This invitation link is invalid.");

        if (invitation.IsDeleted || invitation.RevokedAt is not null)
        {
            throw new UnauthorizedException("invitation_invalid", "This invitation link is invalid.");
        }

        if (invitation.AcceptedAt is not null)
        {
            throw new ConflictException("invitation_accepted", "This invitation has already been used. Sign in instead.");
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            throw new UnauthorizedException("invitation_expired", "This invitation has expired. Ask for a new one.");
        }

        return invitation;
    }

    private async Task EnsureCallerCanGrantRoleAsync(Role targetRole, CancellationToken cancellationToken)
    {
        if (tenant.IsPlatformAdmin) return;
        if (tenant.UserId is not { } callerId)
            throw new ForbiddenException("Authentication required.", "unauthorized");

        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken);
        if (caller == null)
            throw new ForbiddenException("Caller not found.", "forbidden");

        if (caller.IsTenantOwner) return;

        if (caller.RoleId is null)
            throw new ForbiddenException("You do not have a role and cannot grant roles.", "grant_forbidden");

        var callerRole = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == caller.RoleId, cancellationToken);
        if (callerRole == null)
            throw new ForbiddenException("Caller role not found.", "forbidden");

        if (callerRole.IsAdmin) return;

        if (targetRole.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can grant or invite the Admin role.", "admin_role_required");
        }

        // Caller is a non-admin. Compare caller's role permissions against target role's permissions.
        var callerPermissions = await db.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == callerRole.Id)
            .ToDictionaryAsync(p => p.Module, cancellationToken);

        var targetPermissions = await db.RolePermissions.AsNoTracking()
            .Where(p => p.RoleId == targetRole.Id)
            .ToListAsync(cancellationToken);

        foreach (var targetPerm in targetPermissions)
        {
            callerPermissions.TryGetValue(targetPerm.Module, out var callerPerm);

            if (targetPerm.CanView && (callerPerm == null || !callerPerm.CanView))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing View permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanCreate && (callerPerm == null || !callerPerm.CanCreate))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Create permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanEdit && (callerPerm == null || !callerPerm.CanEdit))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Edit permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanDelete && (callerPerm == null || !callerPerm.CanDelete))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Delete permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanExport && (callerPerm == null || !callerPerm.CanExport))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Export permission on {targetPerm.Module}.", "privilege_escalation");

            if (targetPerm.CanImport && (callerPerm == null || !callerPerm.CanImport))
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': missing Import permission on {targetPerm.Module}.", "privilege_escalation");

            if (callerPerm != null && targetPerm.Scope > callerPerm.Scope)
                throw new ForbiddenException($"Cannot grant role '{targetRole.Name}': requested scope ({targetPerm.Scope}) exceeds your scope ({callerPerm.Scope}) on {targetPerm.Module}.", "privilege_escalation");
        }
    }

    private async Task<bool> IsCallerAdminAsync(CancellationToken cancellationToken)
    {
        if (tenant.IsPlatformAdmin) return true;
        if (tenant.UserId is not { } callerId) return false;
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken);
        if (caller == null) return false;
        if (caller.IsTenantOwner) return true;
        if (caller.RoleId is null) return false;
        return await db.Roles.AsNoTracking().AnyAsync(r => r.Id == caller.RoleId && r.IsAdmin, cancellationToken);
    }

    private async Task<Role> EnsureRoleAndTeamAsync(Guid roleId, Guid? teamId, CancellationToken cancellationToken)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new NotFoundException("Role", roleId);

        if (teamId is { } team && !await db.Teams.AnyAsync(t => t.Id == team, cancellationToken))
        {
            throw new NotFoundException("Team", team);
        }

        return role;
    }

    private async Task SendInviteEmailAsync(Invitation invitation, string raw, CancellationToken cancellationToken)
    {
        var workspace = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == invitation.TenantId, cancellationToken);
        var link = $"{app.Value.BaseUrl}/accept-invite?token={Uri.EscapeDataString(raw)}";
        await email.SendAsync(
            invitation.Email,
            $"You're invited to {workspace.Name} on MindLift",
            $"Hello {invitation.FirstName},\n\n{workspace.Name} invited you to MindLift. Accept within 7 days:\n{link}",
            cancellationToken);
    }

    private static InvitationDto Map(Invitation i)
    {
        var status = i.AcceptedAt is not null ? "Accepted"
            : i.RevokedAt is not null ? "Revoked"
            : i.ExpiresAt <= DateTime.UtcNow ? "Expired"
            : "Pending";

        return new InvitationDto(i.Id, i.Email, i.FirstName, i.LastName, i.RoleId, i.TeamId, status, i.ExpiresAt, i.CreatedAt);
    }
}

