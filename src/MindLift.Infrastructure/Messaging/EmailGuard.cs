using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Communication;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;

namespace MindLift.Infrastructure.Messaging;

/// <summary>
/// Protects the platform's sending reputation from any one company's automated mail: no mail to an address that unsubscribed or bounced,
/// at most Email:DailyLimitPerCompany automated mails a day per company, and an unsubscribe link at the foot of every one.
/// Mail a person needs about their own account (sign-in, invitations, receipts) does not go through here.
/// </summary>
public sealed class EmailGuard(AppDbContext db, ITenantContext tenant, NumberSequences numbers, ISecretProtector protector, IConfiguration configuration, IClock clock, ILogger<EmailGuard> logger)
{
    public const int DefaultDailyLimit = 500;

    public int DailyLimit => configuration.GetValue<int?>("Email:DailyLimitPerCompany") ?? DefaultDailyLimit;

    private Guid TenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    private string DayKey => "mail:" + clock.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether an automated mail to <paramref name="recipient"/> may go out now. When it may, it is counted against today's limit.
    /// The reason is given when it may not.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> TryReserveAsync(string recipient, CancellationToken cancellationToken = default)
    {
        var address = recipient.Trim().ToLowerInvariant();
        if (await db.EmailSuppressions.AnyAsync(s => s.Email == address, cancellationToken))
        {
            return (false, "The recipient unsubscribed or their address does not accept mail.");
        }

        var sent = await numbers.NextAsync(DayKey, () => Task.FromResult(1L), cancellationToken);
        if (sent > DailyLimit)
        {
            logger.LogWarning("Company {TenantId} reached its daily limit of {Limit} automated emails", TenantId, DailyLimit);
            return (false, $"The daily limit of {DailyLimit} automated emails has been reached. Sending resumes tomorrow.");
        }

        return (true, null);
    }

    /// <summary>How many automated mails went out today (for the settings page).</summary>
    public async Task<int> SentTodayAsync(CancellationToken cancellationToken = default)
        => (int)(await db.NumberSequences.AsNoTracking().Where(n => n.TenantId == TenantId && n.Key == DayKey).Select(n => (long?)n.LastValue).FirstOrDefaultAsync(cancellationToken) ?? 0);

    /// <summary>The text added under every automated mail: why it was received and how to stop it.</summary>
    public string Footer(string recipient)
    {
        var baseUrl = (configuration["App:BaseUrl"] ?? string.Empty).TrimEnd('/');
        return $"\n\n--\nYou received this email because of your contact with us. To stop receiving these emails: {baseUrl}/unsubscribe?t={Uri.EscapeDataString(UnsubscribeToken(recipient))}";
    }

    public string UnsubscribeToken(string recipient) => protector.Protect($"{TenantId:N}|{recipient.Trim().ToLowerInvariant()}")!;

    /// <summary>Applies an unsubscribe link. Returns the address it was for, or null when the link is not valid. Works without signing in.</summary>
    public async Task<string?> UnsubscribeAsync(string token, CancellationToken cancellationToken = default)
    {
        var plain = protector.Unprotect(token);
        var parts = plain?.Split('|', 2);
        if (parts is not { Length: 2 } || !Guid.TryParseExact(parts[0], "N", out var tenantId) || !parts[1].Contains('@'))
        {
            return null;
        }

        using var scope = tenant.Use(tenantId);
        await SuppressAsync(parts[1], "unsubscribed", cancellationToken);
        return parts[1];
    }

    public async Task SuppressAsync(string email, string reason, CancellationToken cancellationToken = default)
    {
        var address = email.Trim().ToLowerInvariant();
        if (!await db.EmailSuppressions.AnyAsync(s => s.Email == address, cancellationToken))
        {
            db.EmailSuppressions.Add(new EmailSuppression { Email = address, Reason = reason });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
