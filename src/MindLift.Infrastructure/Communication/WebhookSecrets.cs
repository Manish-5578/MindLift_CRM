using Microsoft.EntityFrameworkCore;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Communication;

/// <summary>
/// Keeps a channel's webhook secret encrypted at rest. A stored value is either sealed (it starts with <c>enc1:</c>) or an older plain-text value that has not been
/// converted yet. Both are read the same way, so nothing breaks while old values are being converted.
/// </summary>
public static class WebhookSecrets
{
    private const string Prefix = "enc1:";

    public static bool IsSealed(string? stored) => stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The value to store for a secret: encrypted, and marked so it can be told from an old plain-text value.</summary>
    public static string? Seal(ISecretProtector protector, string? plain)
        => string.IsNullOrEmpty(plain) || IsSealed(plain) ? plain : Prefix + protector.Protect(plain);

    /// <summary>The secret as the provider knows it, from either kind of stored value.</summary>
    public static string? Open(ISecretProtector protector, string? stored)
        => string.IsNullOrEmpty(stored) ? stored : IsSealed(stored) ? protector.Unprotect(stored[Prefix.Length..]) : stored;

    /// <summary>
    /// Converts every plain-text secret to a sealed one, once. Each row is updated on its own, so a row changed by someone else in the meantime is left alone,
    /// and no audit entry or tenant context is needed. Returns how many were converted.
    /// </summary>
    public static async Task<int> SealAllAsync(AppDbContext db, ISecretProtector protector, CancellationToken cancellationToken = default)
    {
        var legacy = await db.ChannelAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.WebhookSecret != null && a.WebhookSecret != string.Empty && !a.WebhookSecret.StartsWith(Prefix))
            .Select(a => new { a.Id, a.WebhookSecret })
            .ToListAsync(cancellationToken);

        var converted = 0;
        foreach (var row in legacy)
        {
            var sealedValue = Seal(protector, row.WebhookSecret);
            var changed = await db.ChannelAccounts.IgnoreQueryFilters()
                .Where(a => a.Id == row.Id && a.WebhookSecret == row.WebhookSecret)
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.WebhookSecret, sealedValue), cancellationToken);
            converted += changed;
        }

        return converted;
    }
}
