using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

/// <summary>
/// Phase 5 / #30: Email open and click tracking (email-tracking).
/// Transparent tracking pixel generation, link rewriting, public event capture,
/// timeline notifications, and engagement analytics.
/// </summary>
public sealed partial class EmailTrackingService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITimelineEventBus timeline,
    ILogger<EmailTrackingService> logger) : IEmailTrackingService
{
    // Standard 1x1 transparent GIF image bytes (43 bytes)
    private static readonly byte[] TransparentGifBytes =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00,
        0x00, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x21, 0xf9, 0x04, 0x01, 0x00,
        0x00, 0x00, 0x00, 0x2c, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00,
        0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3b
    ];

    [GeneratedRegex("""<a\s+(?:[^>]*?\s+)?href=["'](?<url>https?://[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorHrefRegex();

    [GeneratedRegex(@"</body>", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingBodyRegex();

    public async Task<RegisterTrackedEmailResult> RegisterAndTransformEmailAsync(
        RegisterTrackedEmailRequest request,
        string baseUrl,
        CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var trackingToken = Guid.NewGuid().ToString("N");
        var baseCleanUrl = baseUrl.TrimEnd('/');

        var trackedEmail = new TrackedEmail
        {
            TrackingToken = trackingToken,
            RecipientEmail = request.RecipientEmail.Trim().ToLowerInvariant(),
            Subject = request.Subject.Trim(),
            SenderEmail = request.SenderEmail?.Trim().ToLowerInvariant(),
            SentByUserId = tenantContext.UserId,
            LeadId = request.LeadId,
            ContactId = request.ContactId,
            AccountId = request.AccountId,
            DealId = request.DealId,
            MessageId = request.MessageId,
            SentAtUtc = DateTime.UtcNow
        };

        db.TrackedEmails.Add(trackedEmail);

        var html = request.HtmlBody;
        var rewrittenCount = 0;

        // 1. Rewrite Links for Click Tracking
        if (settings.TrackClicks)
        {
            var matches = AnchorHrefRegex().Matches(html);
            foreach (Match match in matches)
            {
                var originalUrl = match.Groups["url"].Value;
                if (originalUrl.Contains("/api/v1/email-tracking/click/"))
                {
                    continue; // Avoid rewriting already tracked links
                }

                var linkToken = Guid.NewGuid().ToString("N")[..16];
                var linkRecord = new TrackedEmailLink
                {
                    TrackedEmail = trackedEmail,
                    LinkToken = linkToken,
                    OriginalUrl = originalUrl
                };
                db.TrackedEmailLinks.Add(linkRecord);

                var trackingUrl = $"{baseCleanUrl}/api/v1/email-tracking/click/{linkToken}";
                html = html.Replace(match.Value, match.Value.Replace(originalUrl, trackingUrl));
                rewrittenCount++;
            }
        }

        // 2. Append Invisible Pixel for Open Tracking
        if (settings.TrackOpens)
        {
            var pixelUrl = $"{baseCleanUrl}/api/v1/email-tracking/pixel/{trackingToken}";
            var pixelTag = $"<img src=\"{pixelUrl}\" width=\"1\" height=\"1\" style=\"display:none!important;width:1px;height:1px;border:0;\" alt=\"\" />";

            if (ClosingBodyRegex().IsMatch(html))
            {
                html = ClosingBodyRegex().Replace(html, $"{pixelTag}</body>", 1);
            }
            else
            {
                html += pixelTag;
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Registered tracked email {TrackedEmailId} for {RecipientEmail} with {LinksCount} tracked links",
            trackedEmail.Id, trackedEmail.RecipientEmail, rewrittenCount);

        return new RegisterTrackedEmailResult
        {
            TrackedEmailId = trackedEmail.Id,
            TrackingToken = trackingToken,
            ProcessedHtmlBody = html,
            LinksRewrittenCount = rewrittenCount
        };
    }

    public async Task<byte[]?> RecordOpenAsync(
        string trackingToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Public endpoint - bypass query filters to find tenant owner
        var tracked = await db.TrackedEmails
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.TrackingToken == trackingToken && !e.IsDeleted, ct);

        if (tracked == null)
        {
            return TransparentGifBytes;
        }

        using var scope = tenantContext.Use(tracked.TenantId);

        var isFirstOpen = tracked.OpenCount == 0;
        tracked.OpenCount++;
        tracked.LastOpenedAtUtc = DateTime.UtcNow;

        if (isFirstOpen)
        {
            tracked.FirstOpenedAtUtc = tracked.LastOpenedAtUtc;
            tracked.UniqueOpenCount = 1;
        }

        var device = DetectDevice(userAgent);
        var openEvent = new TrackedEmailEvent
        {
            TrackedEmailId = tracked.Id,
            EventType = "Open",
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceType = device,
            OccurredAtUtc = DateTime.UtcNow
        };
        db.TrackedEmailEvents.Add(openEvent);

        await db.SaveChangesAsync(ct);

        // Timeline alert on first open
        var settings = await GetOrCreateSettingsAsync(ct);
        if (isFirstOpen && settings.NotifyOnFirstOpen)
        {
            if (tracked.LeadId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "lead",
                    tracked.LeadId.Value,
                    "email_opened",
                    $"Email opened by recipient: {tracked.Subject}",
                    "email_tracking",
                    JsonSerializer.Serialize(new { email = tracked.RecipientEmail, device, subject = tracked.Subject }),
                    tracked.SentByUserId,
                    DateTime.UtcNow), ct);
            }
            else if (tracked.ContactId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "contact",
                    tracked.ContactId.Value,
                    "email_opened",
                    $"Email opened by recipient: {tracked.Subject}",
                    "email_tracking",
                    JsonSerializer.Serialize(new { email = tracked.RecipientEmail, device, subject = tracked.Subject }),
                    tracked.SentByUserId,
                    DateTime.UtcNow), ct);
            }
        }

        return TransparentGifBytes;
    }

    public async Task<string?> RecordClickAsync(
        string linkToken,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        // Public endpoint - bypass query filter to locate link and tenant
        var link = await db.TrackedEmailLinks
            .IgnoreQueryFilters()
            .Include(l => l.TrackedEmail)
            .FirstOrDefaultAsync(l => l.LinkToken == linkToken && !l.IsDeleted, ct);

        if (link == null)
        {
            return null;
        }

        using var scope = tenantContext.Use(link.TenantId);

        var isFirstClick = link.ClickCount == 0;
        link.ClickCount++;
        link.LastClickedAtUtc = DateTime.UtcNow;

        if (isFirstClick)
        {
            link.FirstClickedAtUtc = link.LastClickedAtUtc;
        }

        link.TrackedEmail.ClickCount++;
        if (link.TrackedEmail.UniqueClickCount == 0)
        {
            link.TrackedEmail.UniqueClickCount = 1;
            link.TrackedEmail.FirstClickedAtUtc = link.LastClickedAtUtc;
        }
        link.TrackedEmail.LastClickedAtUtc = link.LastClickedAtUtc;

        var device = DetectDevice(userAgent);
        var clickEvent = new TrackedEmailEvent
        {
            TrackedEmailId = link.TrackedEmailId,
            EventType = "Click",
            LinkToken = linkToken,
            Url = link.OriginalUrl,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            DeviceType = device,
            OccurredAtUtc = DateTime.UtcNow
        };
        db.TrackedEmailEvents.Add(clickEvent);

        await db.SaveChangesAsync(ct);

        // Timeline alert
        var settings = await GetOrCreateSettingsAsync(ct);
        if (settings.NotifyOnLinkClick)
        {
            if (link.TrackedEmail.LeadId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "lead",
                    link.TrackedEmail.LeadId.Value,
                    "email_clicked",
                    $"Link clicked in email: {link.TrackedEmail.Subject}",
                    "email_tracking",
                    JsonSerializer.Serialize(new { url = link.OriginalUrl, email = link.TrackedEmail.RecipientEmail, device }),
                    link.TrackedEmail.SentByUserId,
                    DateTime.UtcNow), ct);
            }
            else if (link.TrackedEmail.ContactId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "contact",
                    link.TrackedEmail.ContactId.Value,
                    "email_clicked",
                    $"Link clicked in email: {link.TrackedEmail.Subject}",
                    "email_tracking",
                    JsonSerializer.Serialize(new { url = link.OriginalUrl, email = link.TrackedEmail.RecipientEmail, device }),
                    link.TrackedEmail.SentByUserId,
                    DateTime.UtcNow), ct);
            }
        }

        return link.OriginalUrl;
    }

    public async Task<TrackedEmailDto?> GetTrackedEmailAsync(Guid id, CancellationToken ct = default)
    {
        var email = await db.TrackedEmails
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        return email != null ? ToDto(email) : null;
    }

    public async Task<IReadOnlyList<TrackedEmailDto>> ListTrackedEmailsAsync(
        string? entityType,
        Guid? entityId,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var query = db.TrackedEmails.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType) && entityId.HasValue)
        {
            query = entityType.ToLowerInvariant() switch
            {
                "lead" => query.Where(e => e.LeadId == entityId.Value),
                "contact" => query.Where(e => e.ContactId == entityId.Value),
                "account" => query.Where(e => e.AccountId == entityId.Value),
                "deal" => query.Where(e => e.DealId == entityId.Value),
                _ => query
            };
        }

        var p = Math.Max(1, page);
        var ps = Math.Clamp(pageSize, 1, 100);

        var list = await query
            .OrderByDescending(e => e.SentAtUtc)
            .Skip((p - 1) * ps)
            .Take(ps)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<TrackedEmailEventDto>> ListEventsAsync(Guid trackedEmailId, CancellationToken ct = default)
    {
        var events = await db.TrackedEmailEvents
            .AsNoTracking()
            .Where(e => e.TrackedEmailId == trackedEmailId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(ct);

        return events.Select(e => new TrackedEmailEventDto
        {
            Id = e.Id,
            TrackedEmailId = e.TrackedEmailId,
            EventType = e.EventType,
            Url = e.Url,
            IpAddress = e.IpAddress,
            UserAgent = e.UserAgent,
            DeviceType = e.DeviceType,
            OccurredAtUtc = e.OccurredAtUtc
        }).ToList();
    }

    public async Task<EmailTrackingSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        return ToSettingsDto(settings);
    }

    public async Task<EmailTrackingSettingsDto> UpdateSettingsAsync(SaveEmailTrackingSettingsRequest request, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        settings.TrackOpens = request.TrackOpens;
        settings.TrackClicks = request.TrackClicks;
        settings.NotifyOnFirstOpen = request.NotifyOnFirstOpen;
        settings.NotifyOnLinkClick = request.NotifyOnLinkClick;
        settings.CustomTrackingDomain = request.CustomTrackingDomain?.Trim();

        await db.SaveChangesAsync(ct);
        return ToSettingsDto(settings);
    }

    public async Task<EmailTrackingStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var totalSent = await db.TrackedEmails.CountAsync(ct);
        var totalOpens = await db.TrackedEmails.SumAsync(e => (int?)e.OpenCount, ct) ?? 0;
        var uniqueOpens = await db.TrackedEmails.SumAsync(e => (int?)e.UniqueOpenCount, ct) ?? 0;
        var totalClicks = await db.TrackedEmails.SumAsync(e => (int?)e.ClickCount, ct) ?? 0;
        var uniqueClicks = await db.TrackedEmails.SumAsync(e => (int?)e.UniqueClickCount, ct) ?? 0;

        var openRate = totalSent > 0 ? Math.Round((double)uniqueOpens / totalSent * 100.0, 1) : 0.0;
        var clickRate = totalSent > 0 ? Math.Round((double)uniqueClicks / totalSent * 100.0, 1) : 0.0;

        return new EmailTrackingStatsDto
        {
            TotalSent = totalSent,
            TotalOpens = totalOpens,
            UniqueOpens = uniqueOpens,
            OpenRatePercent = openRate,
            TotalClicks = totalClicks,
            UniqueClicks = uniqueClicks,
            ClickRatePercent = clickRate
        };
    }

    private async Task<EmailTrackingSettings> GetOrCreateSettingsAsync(CancellationToken ct)
    {
        var settings = await db.EmailTrackingSettings.FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new EmailTrackingSettings
            {
                TrackOpens = true,
                TrackClicks = true,
                NotifyOnFirstOpen = true,
                NotifyOnLinkClick = true
            };
            db.EmailTrackingSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
        return settings;
    }

    private static TrackedEmailDto ToDto(TrackedEmail e) => new()
    {
        Id = e.Id,
        TrackingToken = e.TrackingToken,
        RecipientEmail = e.RecipientEmail,
        Subject = e.Subject,
        SenderEmail = e.SenderEmail,
        SentByUserId = e.SentByUserId,
        LeadId = e.LeadId,
        ContactId = e.ContactId,
        AccountId = e.AccountId,
        DealId = e.DealId,
        MessageId = e.MessageId,
        SentAtUtc = e.SentAtUtc,
        OpenCount = e.OpenCount,
        UniqueOpenCount = e.UniqueOpenCount,
        FirstOpenedAtUtc = e.FirstOpenedAtUtc,
        LastOpenedAtUtc = e.LastOpenedAtUtc,
        ClickCount = e.ClickCount,
        UniqueClickCount = e.UniqueClickCount,
        FirstClickedAtUtc = e.FirstClickedAtUtc,
        LastClickedAtUtc = e.LastClickedAtUtc,
        IsBounced = e.IsBounced,
        BounceReason = e.BounceReason
    };

    private static EmailTrackingSettingsDto ToSettingsDto(EmailTrackingSettings s) => new()
    {
        TrackOpens = s.TrackOpens,
        TrackClicks = s.TrackClicks,
        NotifyOnFirstOpen = s.NotifyOnFirstOpen,
        NotifyOnLinkClick = s.NotifyOnLinkClick,
        CustomTrackingDomain = s.CustomTrackingDomain
    };

    private static string DetectDevice(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Desktop";
        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("ipad") || ua.Contains("tablet")) return "Tablet";
        if (ua.Contains("mobile") || ua.Contains("iphone") || ua.Contains("android")) return "Mobile";
        return "Desktop";
    }
}
