using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

/// <summary>The channel account a webhook request proved it belongs to. Handlers use it instead of looking the account up again from the body.</summary>
public sealed record VerifiedChannel(Guid AccountId, Guid TenantId);

/// <summary>
/// Checks that an inbound webhook really comes from the provider. Meta signs the body with HMAC-SHA256 and Twilio signs the URL and form values with
/// HMAC-SHA1, both with a secret that only the provider and the workspace know. A channel with no secret is refused, so an unconfigured channel never
/// accepts anonymous input.
/// </summary>
public sealed class WebhookAuthenticator(AppDbContext db, ISecretProtector secretProtector, IConfiguration configuration)
{
    /// <summary>Compares the token Meta sends when a webhook is first registered with the one set in <c>Communication:WhatsAppVerifyToken</c>.</summary>
    public bool VerifyRegistrationToken(string? supplied)
    {
        var expected = configuration["Communication:WhatsAppVerifyToken"];
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied))
        {
            return false;
        }

        return FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    }

    /// <summary>Finds the WhatsApp channel named in the request and verifies the body against its secret.</summary>
    public async Task<VerifiedChannel?> VerifyWhatsAppAsync(string? phoneNumberId, string? displayNumber, byte[] body, string? signatureHeader, CancellationToken cancellationToken)
    {
        var query = db.ChannelAccounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.Channel == CommunicationChannel.WhatsApp && a.IsActive && !a.IsDeleted);
        if (!string.IsNullOrWhiteSpace(phoneNumberId))
        {
            query = query.Where(a => a.PhoneNumberId == phoneNumberId);
        }
        else if (!string.IsNullOrWhiteSpace(displayNumber))
        {
            var clean = displayNumber.Trim().TrimStart('+');
            query = query.Where(a => a.SenderAddress == displayNumber || a.SenderAddress == clean || a.PhoneNumberId == clean);
        }
        else
        {
            return null;
        }

        var matches = await query.Take(2).ToListAsync(cancellationToken);
        if (matches.Count != 1)
        {
            // Unknown, or ambiguous: either way nothing is trusted.
            return null;
        }

        var account = matches[0];
        var secret = WebhookSecrets.Open(secretProtector, account.WebhookSecret);
        if (string.IsNullOrEmpty(secret) || !VerifyMetaSignature(body, signatureHeader, secret))
        {
            return null;
        }

        return new VerifiedChannel(account.Id, account.TenantId);
    }

    /// <summary>Finds the Twilio channel named by the account SID and verifies the signed URL and form values against its auth token.</summary>
    public async Task<VerifiedChannel?> VerifyTwilioAsync(string url, IReadOnlyDictionary<string, string> form, string? signatureHeader, CancellationToken cancellationToken)
    {
        form.TryGetValue("AccountSid", out var accountSid);
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrEmpty(signatureHeader))
        {
            return null;
        }

        var candidates = await db.ChannelAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Channel == CommunicationChannel.Sms && a.IsActive && !a.IsDeleted && a.AccountSid == accountSid)
            .ToListAsync(cancellationToken);

        // Several channels can share one Twilio account; the number that received or sent the message picks the right one.
        form.TryGetValue("To", out var to);
        form.TryGetValue("From", out var from);
        var matches = candidates.Where(a => Matches(a.SenderAddress, to) || Matches(a.SenderAddress, from)).ToList();
        if (matches.Count == 0 && candidates.Count == 1)
        {
            matches = candidates;
        }

        if (matches.Count != 1)
        {
            return null;
        }

        var account = matches[0];
        var protectedToken = account.EncryptedAuthToken ?? account.EncryptedPassword;
        if (string.IsNullOrEmpty(protectedToken))
        {
            return null;
        }

        string authToken;
        try
        {
            authToken = secretProtector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }

        return VerifyTwilioSignature(url, form, signatureHeader, authToken) ? new VerifiedChannel(account.Id, account.TenantId) : null;
    }

    /// <summary>Meta sends <c>sha256=&lt;hex&gt;</c>, the HMAC-SHA256 of the raw body keyed with the app secret.</summary>
    public static bool VerifyMetaSignature(byte[] body, string? header, string secret)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        return FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(header[prefix.Length..].Trim().ToLowerInvariant()));
    }

    /// <summary>Twilio signs the full URL followed by every form key and value in key order, with HMAC-SHA1 and the auth token, as base64.</summary>
    public static bool VerifyTwilioSignature(string url, IReadOnlyDictionary<string, string> form, string header, string authToken)
    {
        var data = new StringBuilder(url);
        foreach (var pair in form.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            data.Append(pair.Key).Append(pair.Value);
        }

        var expected = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(authToken), Encoding.UTF8.GetBytes(data.ToString())));
        return FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(header.Trim()));
    }

    private static bool Matches(string sender, string? number)
        => !string.IsNullOrWhiteSpace(number) && string.Equals(sender.Trim().TrimStart('+'), number.Trim().TrimStart('+'), StringComparison.Ordinal);

    private static bool FixedTimeEquals(byte[] left, byte[] right)
        => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}
