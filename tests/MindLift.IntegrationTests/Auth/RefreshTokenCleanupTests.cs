using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Auth;

/// <summary>The daily job that removes old refresh tokens without weakening token-reuse detection.</summary>
public sealed class RefreshTokenCleanupTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static RefreshToken Token(RefreshToken template, DateTime expiresAt, DateTime? revokedAt = null) => new()
    {
        UserId = template.UserId,
        TenantId = template.TenantId,
        TokenHash = Guid.NewGuid().ToString("N"),
        FamilyId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow.AddDays(-40),
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
    };

    [Fact]
    public async Task Only_tokens_that_expired_more_than_a_week_ago_are_removed()
    {
        await Workspaces.SignupAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.RefreshTokens.FirstAsync(t => t.TokenHash != "");
        var now = DateTime.UtcNow;

        var longExpired = Token(template, now.AddDays(-30));
        var longExpiredRevoked = Token(template, now.AddDays(-20), revokedAt: now.AddDays(-25));
        var justOutsideGrace = Token(template, now.AddDays(-8));
        var recentlyExpired = Token(template, now.AddDays(-2));
        var stillValid = Token(template, now.AddDays(10));
        var revokedButNotExpired = Token(template, now.AddDays(10), revokedAt: now.AddDays(-1));
        db.RefreshTokens.AddRange(longExpired, longExpiredRevoked, justOutsideGrace, recentlyExpired, stillValid, revokedButNotExpired);
        await db.SaveChangesAsync();

        var removed = await scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupJob>().RunAsync();

        removed.ShouldBeGreaterThanOrEqualTo(3);
        var left = await db.RefreshTokens.AsNoTracking().Select(t => t.Id).ToListAsync();
        left.ShouldNotContain(longExpired.Id);
        left.ShouldNotContain(longExpiredRevoked.Id);
        left.ShouldNotContain(justOutsideGrace.Id);
        left.ShouldContain(recentlyExpired.Id);
        left.ShouldContain(stillValid.Id);
        left.ShouldContain(revokedButNotExpired.Id);
    }

    [Fact]
    public async Task A_revoked_token_that_is_still_valid_stays_so_that_reusing_it_is_still_noticed()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var refreshed = await Workspaces.MobileClient(factory).TryRefreshAsync("/api/v1/auth/refresh", owner.RefreshToken);
        refreshed.ShouldBeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupJob>().RunAsync();

        // The first token was revoked by the refresh. Using it again must still end the session, which needs its row to exist.
        var reuse = await Workspaces.MobileClient(factory).TryRefreshAsync("/api/v1/auth/refresh", owner.RefreshToken);
        reuse.ShouldBeFalse();
    }

    [Fact]
    public async Task Running_it_with_nothing_to_remove_does_nothing()
    {
        await Workspaces.SignupAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<RefreshTokenCleanupJob>();

        await job.RunAsync();
        var again = await job.RunAsync();

        again.ShouldBe(0);
    }
}

internal static class RefreshHelper
{
    /// <summary>Posts a refresh token the way the mobile client does and says whether the server issued new tokens.</summary>
    public static async Task<bool> TryRefreshAsync(this HttpClient client, string url, string refreshToken)
    {
        var response = await client.PostAsJsonAsync(url, new MindLift.Shared.Contracts.RefreshRequest(refreshToken));
        return response.IsSuccessStatusCode;
    }
}
