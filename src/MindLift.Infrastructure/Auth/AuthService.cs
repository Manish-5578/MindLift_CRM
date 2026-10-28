using Hangfire;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Portal;
using MindLift.Infrastructure.Tenancy;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Auth;

/// <summary>Result of a successful sign-in or refresh. The refresh token is raw and must only be sent to the client once.</summary>
public sealed record AuthResult(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

/// <summary>
/// Sign-in, sign-up, refresh, sign-out, password and email flows (plan 4.3, 6.2).
/// Users are looked up across tenants (login happens before the tenant is known), then the tenant
/// is set for everything else (plan 4.1.6).
/// </summary>
public sealed class AuthService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantContext tenant,
    TokenService tokens,
    RefreshTokenService refreshTokens,
    TenantSignupService signup,
    IEmailSender email,
    IClock clock,
    Microsoft.Extensions.Options.IOptions<AppOptions> app,
    IBackgroundJobClient jobs,
    ISecretProtector secretProtector)
{
    private const string GenericLoginFailure = "Email or password is incorrect.";
    private static readonly string StandInHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));

    public async Task<AuthResult> SignUpAsync(SignupRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = users.NormalizeEmail(request.Email);
        var emailExists = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailExists)
        {
            // Do not reveal email existence to unauthenticated callers (prevent enumeration oracle).
            var appUrl = app.Value.BaseUrl.TrimEnd('/');
            var alertBody = $"Hello {request.FirstName},\n\nA new workspace registration was attempted using your email address ({request.Email}). If this was you, you already have an account and can sign in at {appUrl}/login or reset your password.\n\nRegards,\nMindLift Team";
            var protectedBody = secretProtector.Protect(alertBody);
            try
            {
                jobs.Enqueue<MindLift.Infrastructure.Portal.PortalEmailJob>(job => job.SendAsync(request.Email, "Sign-up attempt on your MindLift account", protectedBody, CancellationToken.None));
            }
            catch
            {
                // Silently ignore queue failures
            }

            var dummyUser = new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                IsActive = false
            };
            var (access, expires) = tokens.CreateAccessToken(dummyUser, false);
            var dummyRefresh = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            return new AuthResult(access, expires, dummyRefresh);
        }

        var user = await signup.CreateTenantWithOwnerAsync(request, cancellationToken);
        return await IssueForUserAsync(user, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(string emailAddress, string password, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAcrossTenantsAsync(emailAddress, cancellationToken);
        if (user == null)
        {
            _ = users.PasswordHasher.VerifyHashedPassword(new AppUser(), StandInHash, password);
            throw new UnauthorizedException("invalid_credentials", GenericLoginFailure);
        }

        using var scope = tenant.Use(user.TenantId, user.Id);

        var tenantRow = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == user.TenantId, cancellationToken);
        if (tenantRow.Status is "Suspended" or "Cancelled")
        {
            throw new ForbiddenException("Your workspace is not active. Contact your administrator.", "tenant_suspended");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("invalid_credentials", GenericLoginFailure);
        }

        if (await users.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException("account_locked", "Too many attempts. Try again in 15 minutes.");
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            // CheckPasswordAsync only verifies. Recording the failure is our job, and it may start the lockout.
            await users.AccessFailedAsync(user);
            await AuditPlatformAsync(user, "auth.failed", "Wrong password at sign-in", cancellationToken);
            if (await users.IsLockedOutAsync(user))
            {
                throw new UnauthorizedException("account_locked", "Too many attempts. Try again in 15 minutes.");
            }

            throw new UnauthorizedException("invalid_credentials", GenericLoginFailure);
        }

        await users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = clock.UtcNow;
        await users.UpdateAsync(user);
        await AuditPlatformAsync(user, "auth.signin", "Signed in to the platform console", cancellationToken);

        return await IssueForUserAsync(user, cancellationToken);
    }

    /// <summary>What the platform owner does is on record, sign-ins included. Company users are not part of that trail.</summary>
    private async Task AuditPlatformAsync(AppUser user, string action, string summary, CancellationToken cancellationToken)
    {
        if (!user.IsPlatformAdmin)
        {
            return;
        }

        var name = $"{user.FirstName} {user.LastName}".Trim();
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            ActorId = user.Id,
            Actor = name.Length > 0 ? name : user.Email ?? "Platform",
            Action = action,
            Summary = summary,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var (userId, tenantId, next) = await refreshTokens.RotateAsync(refreshToken, cancellationToken);

        using var scope = tenant.Use(tenantId, userId);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");

        if (!user.IsActive)
        {
            await refreshTokens.RevokeAllForUserAsync(userId, cancellationToken);
            throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");
        }

        var (access, expires) = tokens.CreateAccessToken(user, user.IsPlatformAdmin);
        return new AuthResult(access, expires, next);
    }

    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
        => refreshTokens.RevokeAsync(refreshToken, cancellationToken);

    public async Task ForgotPasswordAsync(string emailAddress, CancellationToken cancellationToken = default)
    {
        // Always succeeds from the caller's point of view, so the endpoint cannot reveal which emails exist.
        var user = await FindUserAcrossTenantsAsync(emailAddress, cancellationToken);
        if (user is null || !user.IsActive)
        {
            _ = users.PasswordHasher.VerifyHashedPassword(new AppUser(), StandInHash, emailAddress);
            return;
        }

        using var scope = tenant.Use(user.TenantId, user.Id);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = $"{app.Value.BaseUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        var body = $"Hello {user.FirstName},\n\nUse this link to choose a new password (valid for one hour):\n{link}\n\nIf you did not ask for this, ignore this email.";
        var protectedBody = secretProtector.Protect(body);

        try
        {
            jobs.Enqueue<PortalEmailJob>(job => job.SendAsync(user.Email!, "Reset your MindLift password", protectedBody, CancellationToken.None));
        }
        catch
        {
            // Silently ignore queue failures to prevent timing and status leaking
        }
    }

    public async Task ResetPasswordAsync(string emailAddress, string token, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAcrossTenantsAsync(emailAddress, cancellationToken)
            ?? throw new UnauthorizedException("invalid_token", "This link is invalid or has expired.");

        using var scope = tenant.Use(user.TenantId, user.Id);
        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (!result.Succeeded)
        {
            throw ToValidation(result, "This link is invalid or has expired.");
        }

        await refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
    }

    public async Task VerifyEmailAsync(string emailAddress, string token, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAcrossTenantsAsync(emailAddress, cancellationToken)
            ?? throw new UnauthorizedException("invalid_token", "This link is invalid or has expired.");

        using var scope = tenant.Use(user.TenantId, user.Id);
        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw new UnauthorizedException("invalid_token", "This link is invalid or has expired.");
        }
    }

    public async Task SendVerificationEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{app.Value.BaseUrl}/verify-email?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        await email.SendAsync(
            user.Email!,
            "Confirm your MindLift email",
            $"Hello {user.FirstName},\n\nConfirm your email address within 7 days:\n{link}",
            cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded)
        {
            throw ToValidation(result, "Your current password is incorrect.");
        }

        await refreshTokens.RevokeAllForUserAsync(userId, cancellationToken);
    }

    public async Task<MeResponse> GetMeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var tenantRow = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == user.TenantId, cancellationToken);
        var role = user.RoleId is null
            ? null
            : await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == user.RoleId, cancellationToken);

        return new MeResponse(
            user.Id,
            user.TenantId,
            tenantRow.Name,
            user.Email ?? string.Empty,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Language,
            user.EmailConfirmed,
            user.IsTenantOwner,
            user.IsPlatformAdmin,
            user.RoleId,
            role?.Name,
            role?.IsAdmin ?? false,
            tenantRow.Status,
            tenantRow.TrialEndsAt);
    }

    /// <summary>Issues tokens for a user whose tenant is already set by the caller.</summary>
    public async Task<AuthResult> IssueForUserAsync(AppUser user, CancellationToken cancellationToken = default)
    {
        var (access, expires) = tokens.CreateAccessToken(user, user.IsPlatformAdmin);
        var refresh = await refreshTokens.IssueAsync(user.Id, user.TenantId, cancellationToken: cancellationToken);
        return new AuthResult(access, expires, refresh);
    }

    /// <summary>
    /// Finds a user by email in any tenant. Emails are globally unique (plan 4.1.6).
    /// The entity is tracked on purpose: Identity reloads users by id during password and lockout updates,
    /// and must receive the same instance rather than a second copy.
    /// </summary>
    internal async Task<AppUser?> FindUserAcrossTenantsAsync(string emailAddress, CancellationToken cancellationToken)
    {
        var normalized = users.NormalizeEmail(emailAddress);
        return await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
    }

    /// <summary>Maps Identity password errors to a field error; anything else becomes a generic 401.</summary>
    internal static Exception ToValidation(IdentityResult result, string fallbackMessage)
    {
        var passwordErrors = result.Errors
            .Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal))
            .Select(e => e.Description)
            .ToArray();

        return passwordErrors.Length > 0
            ? new MindLift.Application.Common.Exceptions.ValidationException(new Dictionary<string, string[]> { ["password"] = passwordErrors })
            : new UnauthorizedException("invalid_token", fallbackMessage);
    }
}

