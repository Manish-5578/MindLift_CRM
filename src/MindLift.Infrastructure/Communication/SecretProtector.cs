using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MindLift.Infrastructure.Communication;

public interface ISecretProtector
{
    string? Protect(string? plainText);

    /// <summary>The original text, or null when the value cannot be opened (altered, or made with a different key).</summary>
    string? Unprotect(string? cipherText);
}

/// <summary>
/// Encrypts the secrets a workspace stores (mail passwords, provider tokens, webhook secrets).
/// New values use a versioned format (<c>v2:</c>) with AES-256 and an HMAC over the result, keyed from <c>Security:SecretKey</c> or, failing that,
/// <c>Jwt:SigningKey</c>, so an altered value is refused. Values saved by earlier versions had no version marker and were encrypted without an HMAC, with a key taken from
/// <c>Security:SecretKey</c>, <c>Jwt:Key</c> or a key built into the program; they are still read, with each of those keys in turn.
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Version2 = "v2:";
    private static readonly string FallbackKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;
    private readonly IReadOnlyList<byte[]> _legacyKeys;

    public SecretProtector(IConfiguration configuration, ILogger<SecretProtector>? logger = null)
    {
        var configured = FirstNonEmpty(configuration["Security:SecretKey"], configuration["Jwt:SigningKey"]);
        if (configured is null)
        {
            logger?.LogWarning("Neither Security:SecretKey nor Jwt:SigningKey is set, so stored channel secrets are encrypted with a process-generated ephemeral key. Set Security:SecretKey in production.");
            configured = FallbackKey;
        }

        var master = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        _encryptionKey = HMACSHA256.HashData(master, "mindlift-secret-encryption"u8);
        _macKey = HMACSHA256.HashData(master, "mindlift-secret-integrity"u8);

        // Keys earlier versions could have used, most likely first.
        _legacyKeys = new[] { configuration["Security:SecretKey"], configuration["Jwt:Key"], FallbackKey }
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => SHA256.HashData(Encoding.UTF8.GetBytes(key!)))
            .DistinctBy(Convert.ToHexString)
            .ToList();
    }

    public string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plainText), aes.IV);

        var body = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(body, 0);
        cipher.CopyTo(body, aes.IV.Length);
        var mac = HMACSHA256.HashData(_macKey, body);

        var combined = new byte[body.Length + mac.Length];
        body.CopyTo(combined, 0);
        mac.CopyTo(combined, body.Length);
        return Version2 + Convert.ToBase64String(combined);
    }

    public string? Unprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        if (cipherText.StartsWith(Version2, StringComparison.Ordinal))
        {
            return OpenVersion2(cipherText[Version2.Length..]);
        }

        return OpenLegacy(cipherText) ?? cipherText; // Not an encrypted value at all: an older plain-text value is returned as it is.
    }

    private string? OpenVersion2(string encoded)
    {
        try
        {
            var combined = Convert.FromBase64String(encoded);
            if (combined.Length < 16 + 16 + 32)
            {
                return null;
            }

            var body = combined.AsSpan(0, combined.Length - 32);
            var mac = combined.AsSpan(combined.Length - 32);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_macKey, body), mac))
            {
                return null;
            }

            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            var plain = aes.DecryptCbc(body[16..], body[..16]);
            return new UTF8Encoding(false, true).GetString(plain);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The older format: IV, then AES-CBC ciphertext, no integrity check. A key is accepted only when the result is valid UTF-8, which a wrong key almost never gives.</summary>
    private string? OpenLegacy(string encoded)
    {
        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return null;
        }

        if (combined.Length < 32 || combined.Length % 16 != 0)
        {
            return null;
        }

        foreach (var key in _legacyKeys)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Key = key;
                var plain = aes.DecryptCbc(combined.AsSpan(16), combined.AsSpan(0, 16));
                return new UTF8Encoding(false, true).GetString(plain);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                // Wrong key or not text: try the next one.
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));
}
