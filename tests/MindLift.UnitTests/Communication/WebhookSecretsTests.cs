using Microsoft.Extensions.Configuration;
using MindLift.Infrastructure.Communication;
using Shouldly;

namespace MindLift.UnitTests.Communication;

/// <summary>Webhook secrets are stored sealed and read back from either a sealed or an older plain-text value.</summary>
public sealed class WebhookSecretsTests
{
    /// <summary>A reversible stand-in for the real protector, so the tests show the wrapping and not the cipher.</summary>
    private sealed class ReversingProtector : ISecretProtector
    {
        public string? Protect(string? plainText) => plainText is null ? null : "ct(" + new string(plainText.Reverse().ToArray()) + ")";

        public string? Unprotect(string? cipherText)
            => cipherText is { } c && c.StartsWith("ct(") && c.EndsWith(')') ? new string(c[3..^1].Reverse().ToArray()) : cipherText;
    }

    private static readonly ISecretProtector Protector = new ReversingProtector();

    [Fact]
    public void A_sealed_secret_is_marked_and_does_not_contain_the_plain_text()
    {
        var sealedValue = WebhookSecrets.Seal(Protector, "my-app-secret");

        WebhookSecrets.IsSealed(sealedValue).ShouldBeTrue();
        sealedValue.ShouldNotContain("my-app-secret");
    }

    [Fact]
    public void A_sealed_secret_opens_to_the_original()
    {
        WebhookSecrets.Open(Protector, WebhookSecrets.Seal(Protector, "my-app-secret")).ShouldBe("my-app-secret");
    }

    [Fact]
    public void An_older_plain_text_secret_is_still_read_as_it_is()
    {
        WebhookSecrets.IsSealed("plain-secret").ShouldBeFalse();
        WebhookSecrets.Open(Protector, "plain-secret").ShouldBe("plain-secret");
    }

    [Fact]
    public void Sealing_twice_does_not_seal_the_sealed_value_again()
    {
        var once = WebhookSecrets.Seal(Protector, "my-app-secret");

        WebhookSecrets.Seal(Protector, once).ShouldBe(once);
    }

    [Fact]
    public void An_empty_secret_stays_empty()
    {
        WebhookSecrets.Seal(Protector, null).ShouldBeNull();
        WebhookSecrets.Seal(Protector, string.Empty).ShouldBe(string.Empty);
        WebhookSecrets.Open(Protector, null).ShouldBeNull();
        WebhookSecrets.Open(Protector, string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void The_real_protector_round_trips_a_secret_and_hides_it()
    {
        var real = new SecretProtector(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:SecretKey"] = "a-test-only-key-for-the-unit-test-0123456789" }).Build());

        var sealedValue = WebhookSecrets.Seal(real, "my-app-secret");

        sealedValue.ShouldNotContain("my-app-secret");
        WebhookSecrets.Open(real, sealedValue).ShouldBe("my-app-secret");
    }
}
