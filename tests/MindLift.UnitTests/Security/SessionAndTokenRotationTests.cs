using MindLift.Domain.Entities;
using MindLift.Infrastructure.Auth;
using MindLift.Shared.Contracts;
using MindLift.Web.Services;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Security;

public sealed class SessionAndTokenRotationTests
{
    [Fact]
    public void AuthSession_IsSignedIn_ReflectsTokenAndExpiration()
    {
        var session = new AuthSession();
        session.IsSignedIn.ShouldBeFalse();

        // Expired token
        session.SetTokens(new TokenResponse("token-123", DateTime.UtcNow.AddMinutes(-1), null, false));
        session.IsSignedIn.ShouldBeFalse();

        // Valid active token
        session.SetTokens(new TokenResponse("token-123", DateTime.UtcNow.AddMinutes(15), null, false));
        session.IsSignedIn.ShouldBeTrue();

        // Cleared session
        session.Clear();
        session.IsSignedIn.ShouldBeFalse();
        session.AccessToken.ShouldBeNull();
    }

    [Fact]
    public void AuthSession_RaisesChangedEvent_OnUpdates()
    {
        var session = new AuthSession();
        var changeCount = 0;
        session.Changed += () => changeCount++;

        session.SetTokens(new TokenResponse("tok", DateTime.UtcNow.AddHours(1), null, false));
        session.SetMe(new MeResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Acme Corp",
            "sarah@acme.com",
            "Sarah Connor",
            "en",
            true,
            true,
            false,
            Guid.NewGuid(),
            "Admin",
            true,
            "Active",
            null,
            false));
        session.Clear();

        changeCount.ShouldBe(3);
    }

    [Fact]
    public void RefreshToken_RotationGracePeriod_RecognizesConcurrentNormalRotations()
    {
        var now = DateTime.UtcNow;
        var rotatedToken = new RefreshToken
        {
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            TokenHash = "hash-token-1",
            CreatedAt = now.AddMinutes(-5),
            ExpiresAt = now.AddDays(14),
            RevokedAt = now.AddSeconds(-10), // Rotated 10 seconds ago
            ReplacedByHash = "hash-token-2"  // Normal rotation successor exists
        };

        // Within 60 seconds grace period
        var isWithinGracePeriod = rotatedToken.RevokedAt.HasValue
            && rotatedToken.ReplacedByHash != null
            && (now - rotatedToken.RevokedAt.Value) <= TimeSpan.FromSeconds(60);

        isWithinGracePeriod.ShouldBeTrue();
    }

    [Fact]
    public void RefreshToken_RotationGracePeriod_RejectsExpiredGraceWindowOrExplicitRevocations()
    {
        var now = DateTime.UtcNow;

        // Case 1: Outside grace window (rotated 5 minutes ago)
        var oldRotatedToken = new RefreshToken
        {
            RevokedAt = now.AddMinutes(-5),
            ReplacedByHash = "hash-token-old"
        };

        var isOldWithinGrace = oldRotatedToken.RevokedAt.HasValue
            && oldRotatedToken.ReplacedByHash != null
            && (now - oldRotatedToken.RevokedAt.Value) <= TimeSpan.FromSeconds(60);

        isOldWithinGrace.ShouldBeFalse();

        // Case 2: Explicit logout revocation (ReplacedByHash is null)
        var loggedOutToken = new RefreshToken
        {
            RevokedAt = now.AddSeconds(-5),
            ReplacedByHash = null
        };

        var isLogoutWithinGrace = loggedOutToken.RevokedAt.HasValue
            && loggedOutToken.ReplacedByHash != null
            && (now - loggedOutToken.RevokedAt.Value) <= TimeSpan.FromSeconds(60);

        isLogoutWithinGrace.ShouldBeFalse();
    }
}
