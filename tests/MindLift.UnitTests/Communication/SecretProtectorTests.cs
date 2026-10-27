using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using MindLift.Infrastructure.Communication;
using Shouldly;

namespace MindLift.UnitTests.Communication;

/// <summary>The encryption behind stored mail passwords, provider tokens and webhook secrets.</summary>
public sealed class SecretProtectorTests
{
    private static readonly string TestLegacyKey = "legacy-test-fixture-key";

    private static SecretProtector With(params (string Key, string Value)[] settings)
        => new(new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build());

    /// <summary>A value as the earlier version stored it: IV then AES-CBC ciphertext, no version marker and no integrity check, key = SHA-256 of the configured text.</summary>
    private static string LegacyProtect(string plain, string rawKey)
    {
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        aes.GenerateIV();
        var cipher = aes.CreateEncryptor().TransformFinalBlock(Encoding.UTF8.GetBytes(plain), 0, Encoding.UTF8.GetByteCount(plain));
        return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
    }

    [Fact]
    public void A_secret_round_trips_and_the_stored_form_hides_it_and_carries_a_version()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));

        var stored = protector.Protect("smtp-password-123");

        stored.ShouldStartWith("v2:");
        stored.ShouldNotContain("smtp-password-123");
        protector.Unprotect(stored).ShouldBe("smtp-password-123");
    }

    [Fact]
    public void The_same_secret_is_stored_differently_each_time()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));

        protector.Protect("same").ShouldNotBe(protector.Protect("same"));
    }

    [Fact]
    public void An_altered_value_is_refused_instead_of_returning_something_else()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));
        var stored = protector.Protect("token-value")!;
        var bytes = Convert.FromBase64String(stored["v2:".Length..]);
        bytes[20] ^= 0x01; // one changed bit in the ciphertext
        var altered = "v2:" + Convert.ToBase64String(bytes);

        protector.Unprotect(altered).ShouldBeNull();
    }

    [Fact]
    public void A_different_key_cannot_open_a_value()
    {
        var stored = With(("Security:SecretKey", "unit-test-key-one")).Protect("token-value");

        With(("Security:SecretKey", "unit-test-key-two")).Unprotect(stored).ShouldBeNull();
    }

    [Fact]
    public void The_signing_key_is_used_when_no_secret_key_is_set()
    {
        var stored = With(("Jwt:SigningKey", "the-staff-signing-key-value-0123456789")).Protect("token-value");

        With(("Jwt:SigningKey", "the-staff-signing-key-value-0123456789")).Unprotect(stored).ShouldBe("token-value");
        With(("Security:SecretKey", "something-else")).Unprotect(stored).ShouldBeNull();
        With().Unprotect(stored).ShouldBeNull(); // The built-in key is not the signing key.
    }

    [Fact]
    public void The_secret_key_wins_over_the_signing_key()
    {
        var stored = With(("Security:SecretKey", "dedicated"), ("Jwt:SigningKey", "signing")).Protect("token-value");

        With(("Security:SecretKey", "dedicated")).Unprotect(stored).ShouldBe("token-value");
        With(("Jwt:SigningKey", "signing")).Unprotect(stored).ShouldBeNull();
    }

    [Fact]
    public void A_value_stored_by_the_earlier_version_with_a_configured_legacy_key_opens()
    {
        var oldValue = LegacyProtect("old-smtp-password", TestLegacyKey);

        With(("Security:SecretKey", TestLegacyKey)).Unprotect(oldValue).ShouldBe("old-smtp-password");
        With(("Jwt:Key", TestLegacyKey)).Unprotect(oldValue).ShouldBe("old-smtp-password");
    }

    [Fact]
    public void A_value_stored_by_the_earlier_version_with_a_configured_key_still_opens()
    {
        var oldValue = LegacyProtect("old-token", "the-key-the-old-version-used");

        With(("Security:SecretKey", "the-key-the-old-version-used")).Unprotect(oldValue).ShouldBe("old-token");
        With(("Jwt:Key", "the-key-the-old-version-used")).Unprotect(oldValue).ShouldBe("old-token");
    }

    [Fact]
    public void A_plain_text_value_that_was_never_encrypted_is_returned_as_it_is()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));

        protector.Unprotect("just a plain password").ShouldBe("just a plain password");
        protector.Unprotect("not-base64-@@@").ShouldBe("not-base64-@@@");
    }

    [Fact]
    public void Empty_values_stay_empty()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));

        protector.Protect(null).ShouldBeNull();
        protector.Protect(string.Empty).ShouldBe(string.Empty);
        protector.Unprotect(null).ShouldBeNull();
        protector.Unprotect(string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void Text_beyond_ascii_survives()
    {
        var protector = With(("Security:SecretKey", "unit-test-key-one"));

        protector.Unprotect(protector.Protect("كلمة سر ✓ 123")).ShouldBe("كلمة سر ✓ 123");
    }
}
