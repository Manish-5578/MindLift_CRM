using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// Encrypts every secret with <see cref="ISecretProtector"/> before it reaches the database and decrypts it only for code that asks by name.
/// A revoked credential has its stored secret erased, not just hidden. The company comes from the tenant context and the query filter,
/// so one company can never read another's credential.
/// </summary>
public sealed partial class CredentialVault(AppDbContext db, ITenantContext tenant, ISecretProtector protector, IClock clock) : ICredentialVault
{
    private const int MaxSecret = 4096;
    private const int MaxSettings = 4000;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$")]
    private static partial Regex ProviderPattern();

    private Guid CurrentTenantId => tenant.TenantId ?? throw new ForbiddenException();

    public async Task<CredentialInfo> StoreAsync(string provider, string label, string secret, string? settingsJson = null, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId;
        provider = CleanProvider(provider);
        label = CleanLabel(label);
        if (string.IsNullOrEmpty(secret) || secret.Length > MaxSecret)
        {
            throw new ValidationException("secret", $"A secret of 1 to {MaxSecret} characters is required.");
        }

        CheckSettings(settingsJson);

        var encrypted = protector.Protect(secret)!;
        var row = await db.Set<IntegrationCredential>().FirstOrDefaultAsync(c => c.Provider == provider && c.Label == label, cancellationToken);
        if (row is null)
        {
            row = new IntegrationCredential { TenantId = tenantId, Provider = provider, Label = label };
            db.Set<IntegrationCredential>().Add(row);
        }
        else
        {
            row.RotatedAt = clock.UtcNow;
        }

        row.EncryptedSecret = encrypted;
        row.SettingsJson = settingsJson;
        row.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<string?> GetSecretAsync(string provider, string label, CancellationToken cancellationToken = default)
    {
        _ = CurrentTenantId;
        provider = CleanProvider(provider);
        label = CleanLabel(label);
        var row = await db.Set<IntegrationCredential>().AsNoTracking().FirstOrDefaultAsync(c => c.Provider == provider && c.Label == label && c.IsActive, cancellationToken);
        if (row is null || string.IsNullOrEmpty(row.EncryptedSecret))
        {
            return null;
        }

        var now = clock.UtcNow;
        await db.Set<IntegrationCredential>().Where(c => c.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, now), cancellationToken);
        return protector.Unprotect(row.EncryptedSecret);
    }

    public async Task<IReadOnlyList<CredentialInfo>> ListAsync(string? provider = null, CancellationToken cancellationToken = default)
    {
        _ = CurrentTenantId;
        var query = db.Set<IntegrationCredential>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(provider))
        {
            var p = CleanProvider(provider);
            query = query.Where(c => c.Provider == p);
        }

        return (await query.OrderBy(c => c.Provider).ThenBy(c => c.Label).ToListAsync(cancellationToken)).Select(Map).ToList();
    }

    public async Task<bool> RevokeAsync(string provider, string label, CancellationToken cancellationToken = default)
    {
        _ = CurrentTenantId;
        provider = CleanProvider(provider);
        label = CleanLabel(label);
        var row = await db.Set<IntegrationCredential>().FirstOrDefaultAsync(c => c.Provider == provider && c.Label == label, cancellationToken);
        if (row is null)
        {
            return false;
        }

        row.IsActive = false;
        row.EncryptedSecret = string.Empty;
        row.RotatedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string CleanProvider(string provider)
    {
        var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
        if (!ProviderPattern().IsMatch(p))
        {
            throw new ValidationException("provider", "A provider is 2 to 64 lower case letters, digits or dashes.");
        }

        return p;
    }

    private static string CleanLabel(string label)
    {
        var l = (label ?? string.Empty).Trim();
        if (l.Length is < 1 or > 100)
        {
            throw new ValidationException("label", "A label of 1 to 100 characters is required.");
        }

        return l;
    }

    private static void CheckSettings(string? settingsJson)
    {
        if (settingsJson is null)
        {
            return;
        }

        if (settingsJson.Length > MaxSettings)
        {
            throw new ValidationException("settings", $"Settings can have at most {MaxSettings} characters.");
        }

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException("settings", "Settings must be a JSON object.");
            }
        }
        catch (JsonException)
        {
            throw new ValidationException("settings", "Settings must be valid JSON.");
        }
    }

    private static CredentialInfo Map(IntegrationCredential c) => new(c.Id, c.Provider, c.Label, c.IsActive, c.SettingsJson, c.LastUsedAt, c.RotatedAt);
}
