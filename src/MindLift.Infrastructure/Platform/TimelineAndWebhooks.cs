using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Platform;

/// <summary>Writes and reads the timeline of a record for the current company, after checking the shape of what it is given.</summary>
public sealed partial class TimelineEventBus(AppDbContext db, ITenantContext tenant, IClock clock) : ITimelineEventBus
{
    private const int MaxSummary = 300;
    private const int MaxDetail = 8000;
    private const int MaxTake = 200;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,47}$")]
    private static partial Regex TokenPattern();

    public async Task<TimelineEventDto> PublishAsync(TimelineEventInput input, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();
        if (!TokenPattern().IsMatch(input.EntityType ?? string.Empty))
        {
            throw new ValidationException("entityType", "An entity type is lower case letters, digits, dots, dashes or underscores.");
        }

        if (!TokenPattern().IsMatch(input.Kind ?? string.Empty))
        {
            throw new ValidationException("kind", "A kind is lower case letters, digits, dots, dashes or underscores, for example call.completed.");
        }

        if (!TokenPattern().IsMatch(input.Source ?? string.Empty))
        {
            throw new ValidationException("source", "A source is lower case letters, digits, dots, dashes or underscores.");
        }

        var summary = (input.Summary ?? string.Empty).Trim();
        if (summary.Length is < 1 or > MaxSummary)
        {
            throw new ValidationException("summary", $"A summary of 1 to {MaxSummary} characters is required.");
        }

        if (input.EntityId == Guid.Empty)
        {
            throw new ValidationException("entityId", "A record is required.");
        }

        if (input.DetailJson is { } detail)
        {
            if (detail.Length > MaxDetail)
            {
                throw new ValidationException("detail", $"Detail can have at most {MaxDetail} characters.");
            }

            try
            {
                using var _ = JsonDocument.Parse(detail);
            }
            catch (JsonException)
            {
                throw new ValidationException("detail", "Detail must be valid JSON.");
            }
        }

        var row = new TimelineEvent
        {
            TenantId = tenantId,
            EntityType = input.EntityType!,
            EntityId = input.EntityId,
            Kind = input.Kind!,
            Summary = summary,
            DetailJson = input.DetailJson,
            Source = input.Source!,
            ActorUserId = input.ActorUserId ?? tenant.UserId,
            OccurredAt = input.OccurredAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : clock.UtcNow,
        };
        db.Set<TimelineEvent>().Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<IReadOnlyList<TimelineEventDto>> ListAsync(string entityType, Guid entityId, int take = 50, CancellationToken cancellationToken = default)
    {
        _ = tenant.TenantId ?? throw new ForbiddenException();
        take = Math.Clamp(take, 1, MaxTake);
        var rows = await db.Set<TimelineEvent>().AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    private static TimelineEventDto Map(TimelineEvent e) => new(e.Id, e.EntityType, e.EntityId, e.Kind, e.Summary, e.Source, e.DetailJson, e.ActorUserId, e.OccurredAt);
}

/// <summary>
/// Replay protection for webhooks. The caller verifies the signature first (see <see cref="WebhookSignature"/>); this refuses events that are
/// too old or from the future, and events whose id was already seen, so a captured request cannot be sent again.
/// </summary>
public sealed partial class WebhookIngress(IIdempotencyStore idempotency, IClock clock) : IWebhookIngress
{
    /// <summary>How far a webhook's own timestamp may differ from our clock.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan Remember = TimeSpan.FromHours(24);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$")]
    private static partial Regex ProviderPattern();

    public async Task<WebhookDecision> AcceptAsync(string provider, string eventId, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        if (!ProviderPattern().IsMatch(provider ?? string.Empty))
        {
            throw new ValidationException("provider", "Unknown provider.");
        }

        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 200)
        {
            throw new ValidationException("eventId", "An event id of 1 to 200 characters is required.");
        }

        var now = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);
        if (sentAt < now - Tolerance || sentAt > now + Tolerance)
        {
            return WebhookDecision.Stale;
        }

        return await idempotency.TryBeginAsync($"webhook:{provider}", eventId, Remember, cancellationToken)
            ? WebhookDecision.Accepted
            : WebhookDecision.Duplicate;
    }
}

/// <summary>Checks the HMAC signature a provider puts on a webhook. Compares in constant time so the check does not leak how much matched.</summary>
public static class WebhookSignature
{
    /// <summary>Accepts the signature as hex, with or without a <c>sha256=</c> prefix.</summary>
    public static bool VerifyHmacSha256(string secret, ReadOnlySpan<byte> payload, string? signature)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var hex = signature.Trim();
        if (hex.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            hex = hex[7..];
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return provided.Length == expected.Length && CryptographicOperations.FixedTimeEquals(provided, expected);
    }
}
