using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Auth;

/// <summary>
/// Runs daily. Removes refresh tokens that expired more than <see cref="GraceDays"/> days ago.
/// A revoked token is kept until it would have expired anyway: using one again is how a stolen token is noticed
/// and the whole session family is ended, so deleting it earlier would remove that protection.
/// </summary>
public sealed class RefreshTokenCleanupJob(AppDbContext db, IClock clock)
{
    public const int GraceDays = 7;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddDays(-GraceDays);
        return await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
