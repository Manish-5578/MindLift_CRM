using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Lets the platform owner see a company's workspace the way its owner sees it, to help with a support question, without ever asking for
/// a password. A support session is read-only, lasts at most <see cref="Length"/>, needs a written reason, and is recorded: when it started
/// and ended, who opened it, why, and every page of data it looked at.
/// </summary>
public sealed class SupportAccessService(AppDbContext db, ITenantContext tenant, TokenService tokens, IClock clock)
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(2);
    private const int MaxRecordedPaths = 500;

    /// <summary>Starts a session and returns a one-time code; the code is exchanged for the session in a new browser tab within two minutes.</summary>
    public async Task<SupportSessionStartedDto> StartAsync(Guid tenantId, StartSupportSessionRequest request, CancellationToken cancellationToken = default)
    {
        var reason = (request.Reason ?? string.Empty).Trim();
        if (reason.Length < 5 || reason.Length > 500)
        {
            throw new ValidationException("reason", "Say why you need to look (5 to 500 characters). It is kept in the audit trail.");
        }

        var company = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken) ?? throw new NotFoundException("Company", tenantId);
        if (company.Slug == PlatformAdminSeeder.PlatformSlug)
        {
            throw new ForbiddenException("The platform's own workspace is not a company.");
        }

        var owner = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsTenantOwner && u.IsActive).OrderBy(u => u.CreatedAt).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("no_owner", "This company has no active owner to view as.");

        var me = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == tenant.UserId).Select(u => new { u.Id, u.FirstName, u.LastName }).FirstAsync(cancellationToken);
        var name = $"{me.FirstName} {me.LastName}".Trim();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = clock.UtcNow;
        var session = new SupportSession
        {
            TenantId = tenantId,
            ViewAsUserId = owner.Id,
            PlatformUserId = me.Id,
            PlatformUserName = name.Length > 0 ? name : "Platform",
            Reason = reason,
            CodeHash = Hash(code),
            CodeExpiresAt = now + CodeLife,
            StartedAt = now,
            ExpiresAt = now + Length,
        };
        db.SupportSessions.Add(session);
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            TenantId = tenantId, Company = company.Name, ActorId = me.Id, Actor = session.PlatformUserName, Action = "support.started",
            Summary = $"Read-only support view opened for {Length.TotalMinutes:0} minutes. Reason: {reason}", CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new SupportSessionStartedDto(session.Id, code, session.ExpiresAt);
    }

    /// <summary>Turns the one-time code into the session's sign-in. A code works once and only for two minutes.</summary>
    public async Task<TokenResponse> ExchangeAsync(string code, CancellationToken cancellationToken = default)
    {
        var hash = Hash(code ?? string.Empty);
        var now = clock.UtcNow;
        var session = await db.SupportSessions.FirstOrDefaultAsync(s => s.CodeHash == hash, cancellationToken);
        if (session is null || session.CodeExpiresAt < now || session.EndedAt is not null || session.ExpiresAt < now)
        {
            throw new UnauthorizedException("support_code_invalid", "This support link is not valid any more. Start a new support session.");
        }

        session.CodeHash = null;   // used up
        await db.SaveChangesAsync(cancellationToken);

        var owner = await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == session.ViewAsUserId, cancellationToken);
        var (access, expires) = tokens.CreateSupportToken(owner, session.Id, session.PlatformUserName, session.ExpiresAt);
        return new TokenResponse(access, expires, null);
    }

    /// <summary>Checks a support session on each request: true while it may be used. Data pages it reads are added to its record.</summary>
    public async Task<bool> TouchAsync(Guid sessionId, string? readPath, CancellationToken cancellationToken = default)
    {
        var session = await db.SupportSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.EndedAt is not null || session.ExpiresAt < clock.UtcNow)
        {
            return false;
        }

        if (readPath is not null && session.VisitedPaths.Length < MaxRecordedPaths && !session.VisitedPaths.Contains(readPath))
        {
            session.VisitedPaths = [.. session.VisitedPaths, readPath];
            await db.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task EndAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.SupportSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken) ?? throw new NotFoundException("Support session", sessionId);
        if (session.EndedAt is not null)
        {
            return;
        }

        session.EndedAt = clock.UtcNow;
        var company = await db.Tenants.AsNoTracking().Where(t => t.Id == session.TenantId).Select(t => t.Name).FirstOrDefaultAsync(cancellationToken);
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            TenantId = session.TenantId, Company = company, ActorId = session.PlatformUserId, Actor = session.PlatformUserName, Action = "support.ended",
            Summary = $"Support view closed after {(session.EndedAt.Value - session.StartedAt).TotalMinutes:0} minutes. Pages viewed: {session.VisitedPaths.Length}"
                + (session.VisitedPaths.Length == 0 ? string.Empty : $" ({string.Join(", ", session.VisitedPaths.Take(12))}{(session.VisitedPaths.Length > 12 ? ", ..." : string.Empty)})"),
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupportSessionDto>> SessionsAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => (await db.SupportSessions.AsNoTracking().Where(s => s.TenantId == tenantId).OrderByDescending(s => s.StartedAt).Take(20).ToListAsync(cancellationToken))
            .Select(s => new SupportSessionDto(s.Id, s.PlatformUserName, s.Reason, s.StartedAt, s.EndedAt ?? (s.ExpiresAt < clock.UtcNow ? s.ExpiresAt : null), s.VisitedPaths)).ToList();

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
