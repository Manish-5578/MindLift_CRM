using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Mobile;

public sealed class MobileContractsAndHashingTests
{
    [Fact]
    public void TokenHash_computes_deterministic_sha256()
    {
        var token = "fcm_test_device_token_xyz_1234567890";
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        hash1.ShouldBe(hash2);
        hash1.Length.ShouldBe(64);
        hash1.ShouldNotContain(" ");
    }

    [Fact]
    public void DataProtector_encrypts_and_decrypts_push_token()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var protector = dataProtectionProvider.CreateProtector("MindLift.Mobile.DeviceTokens");

        var rawToken = "apns_super_secret_push_token_for_staff_user_device";
        var encryptedCiphertext = protector.Protect(rawToken);

        encryptedCiphertext.ShouldNotBe(rawToken);
        encryptedCiphertext.ShouldNotContain(rawToken);

        var decrypted = protector.Unprotect(encryptedCiphertext);
        decrypted.ShouldBe(rawToken);
    }

    [Fact]
    public void MobileDevice_entity_tracks_push_diagnostics_and_install_uniqueness()
    {
        var installId = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var device = new MobileDevice
        {
            TenantId = tenantId,
            UserId = userId,
            InstallationId = installId,
            Platform = "android",
            Environment = "production",
            Provider = "fcm",
            ProviderVersion = "v1",
            TokenCiphertext = "encrypted_blob",
            TokenHash = "abc123hash",
            AppVersion = "1.0.0",
            Locale = "ar",
            TimeZoneId = "Asia/Riyadh",
            NotificationsAllowed = true,
            LastSeenAtUtc = now,
            FailureCount = 0
        };

        device.InstallationId.ShouldBe(installId);
        device.Platform.ShouldBe("android");
        device.Locale.ShouldBe("ar");
        device.FailureCount.ShouldBe(0);
        device.RevokedAtUtc.ShouldBeNull();

        // Increment failure
        device.FailureCount++;
        device.LastPushFailureCode = "UNREGISTERED";
        device.RevokedAtUtc = now;

        device.FailureCount.ShouldBe(1);
        device.RevokedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void OfflineMutationLog_stores_idempotency_state()
    {
        var log = new OfflineMutationLog
        {
            TenantId = Guid.NewGuid(),
            ClientMutationId = "mutation_abc_123",
            BatchId = "batch_001",
            EntityType = "lead",
            Operation = "create",
            Status = "applied",
            ServerEntityId = Guid.NewGuid(),
            NewVersion = 1,
            CompletedAtUtc = DateTime.UtcNow
        };

        log.ClientMutationId.ShouldBe("mutation_abc_123");
        log.Status.ShouldBe("applied");
        log.ServerEntityId.ShouldNotBeNull();
        log.NewVersion.ShouldBe(1);
    }
}

