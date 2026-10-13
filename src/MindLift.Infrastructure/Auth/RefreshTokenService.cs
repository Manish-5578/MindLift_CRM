using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Auth;

/// <summary>
/// Refresh-token rotation with reuse detection (plan 4.3): every refresh issues a new token and revokes the old one.
/// Presenting a revoked token revokes its whole family, because it means the token was stolen or copied.
/// </summary>
public sealed class RefreshTokenService(AppDbContext db, IClock clock, TokenService tokens)
{
    /// <summary>Issues a new refresh token and returns the raw value (only the hash is stored).</summary>
    public async Task<string> IssueAsync(Guid userId, Guid tenantId, Guid? familyId = null, CancellationToken cancellationToken = default)
    {
        var raw = TokenService.NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TenantId = tenantId,
            TokenHash = TokenService.Hash(raw),
            FamilyId = familyId ?? Guid.CreateVersion7(),
            CreatedAt = clock.UtcNow,
            ExpiresAt = tokens.RefreshExpiresAt(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return raw;
    }

    /// <summary>Exchanges a valid refresh token for a new one, and returns the owner of the token.</summary>
    public async Task<(Guid UserId, Guid TenantId, string RefreshToken)> RotateAsync(string raw, CancellationToken cancellationToken = default)
    {
        var hash = TokenService.Hash(raw);
        var row = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken)
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");

        var now = clock.UtcNow;

        if (row.RevokedAt is not null)
        {
            await RevokeFamilyAsync(row.FamilyId, cancellationToken);
            throw new UnauthorizedException("refresh_token_reused", "Your session has ended. Please sign in again.");
        }

        if (row.ExpiresAt <= now)
        {
            throw new UnauthorizedException("refresh_token_expired", "Your session has ended. Please sign in again.");
        }

        var next = await IssueAsync(row.UserId, row.TenantId, row.FamilyId, cancellationToken);
        row.RevokedAt = now;
        row.ReplacedByHash = TokenService.Hash(next);
        await db.SaveChangesAsync(cancellationToken);

        return (row.UserId, row.TenantId, next);
    }

    /// <summary>Revokes the family of a token (sign out). Unknown tokens are ignored.</summary>
    public async Task RevokeAsync(string raw, CancellationToken cancellationToken = default)
    {
        var hash = TokenService.Hash(raw);
        var row = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);
        if (row is not null)
        {
            await RevokeFamilyAsync(row.FamilyId, cancellationToken);
        }
    }

    /// <summary>Revokes every active refresh token of a user (password reset, deactivation).</summary>
    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var active = await db.RefreshTokens.Where(r => r.UserId == userId && r.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in active)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var family = await db.RefreshTokens.Where(r => r.FamilyId == familyId && r.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in family)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
