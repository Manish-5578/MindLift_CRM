using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #25: Website visitor tracking (visitor-tracking).
/// Real-time website visitor intelligence, tracking token ingestion, session aggregation,
/// lead identification, and browsing history timeline synchronization.
/// </summary>
public sealed class VisitorTrackingService(
    AppDbContext db,
    ITenantContext tenant,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline,
    ILogger<VisitorTrackingService> logger) : IVisitorTrackingService
{
    // ================= Tracker Management =================

    public async Task<WebsiteTrackerDto> CreateTrackerAsync(SaveWebsiteTrackerRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Tracker name is required.");

        var domain = req.Domain?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
            throw new ValidationException("Domain", "Domain is required.");

        var token = $"trk_{Guid.NewGuid():N}";

        var tracker = new WebsiteTracker
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Name = name,
            Domain = domain,
            TrackingToken = token,
            IsActive = true,
            AutoCreateLeads = req.AutoCreateLeads,
            AllowedOrigins = CleanArray(req.AllowedOrigins),
            CreatedAt = DateTime.UtcNow
        };

        db.WebsiteTrackers.Add(tracker);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created website tracker {TrackerId} for {Domain} with token {Token}",
            tracker.Id, tracker.Domain, tracker.TrackingToken);

        return MapTrackerDto(tracker);
    }

    public async Task<WebsiteTrackerDto> GetTrackerByIdAsync(Guid id, CancellationToken ct = default)
    {
        var tracker = await db.WebsiteTrackers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tracker == null)
            throw new NotFoundException("WebsiteTracker", id);

        return MapTrackerDto(tracker);
    }

    public async Task<IReadOnlyList<WebsiteTrackerDto>> ListTrackersAsync(CancellationToken ct = default)
    {
        var list = await db.WebsiteTrackers
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return list.Select(MapTrackerDto).ToList();
    }

    public async Task<WebsiteTrackerDto> UpdateTrackerAsync(Guid id, SaveWebsiteTrackerRequest req, CancellationToken ct = default)
    {
        var tracker = await db.WebsiteTrackers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tracker == null)
            throw new NotFoundException("WebsiteTracker", id);

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Tracker name is required.");

        var domain = req.Domain?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
            throw new ValidationException("Domain", "Domain is required.");

        tracker.Name = name;
        tracker.Domain = domain;
        tracker.AutoCreateLeads = req.AutoCreateLeads;
        tracker.AllowedOrigins = CleanArray(req.AllowedOrigins);
        tracker.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapTrackerDto(tracker);
    }

    public async Task<bool> DeleteTrackerAsync(Guid id, CancellationToken ct = default)
    {
        var tracker = await db.WebsiteTrackers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tracker == null) return false;

        tracker.IsDeleted = true;
        tracker.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= Ingestion & Visitor Journey =================

    public async Task<TrackHitResultDto> TrackHitAsync(TrackHitRequest req, CancellationToken ct = default)
    {
        var token = req.TrackingToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return new TrackHitResultDto { Success = false, Message = "TrackingToken is required." };

        var visitorId = req.VisitorId?.Trim();
        if (string.IsNullOrWhiteSpace(visitorId))
            return new TrackHitResultDto { Success = false, Message = "VisitorId is required." };

        var sessionId = req.SessionId?.Trim();
        if (string.IsNullOrWhiteSpace(sessionId))
            return new TrackHitResultDto { Success = false, Message = "SessionId is required." };

        // Anonymous public token lookup across tenants
        var tracker = await db.WebsiteTrackers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TrackingToken == token && !t.IsDeleted, ct);

        if (tracker == null || !tracker.IsActive)
            return new TrackHitResultDto { Success = false, Message = "Invalid or inactive tracking token." };

        // Scope execution to the tracker's tenant and verify feature flag
        using (tenant.Use(tracker.TenantId))
        {
            var isEnabled = await featureFlags.IsEnabledAsync("visitor-tracking", ct);
            if (!isEnabled)
                return new TrackHitResultDto { Success = false, Message = "Visitor tracking is disabled for this organization." };

            var now = DateTime.UtcNow;

            // Find or create session
            var session = await db.VisitorSessions
                .FirstOrDefaultAsync(s => s.TrackerId == tracker.Id && s.VisitorId == visitorId && s.SessionId == sessionId, ct);

            if (session == null)
            {
                session = new VisitorSession
                {
                    TenantId = tracker.TenantId,
                    TrackerId = tracker.Id,
                    VisitorId = visitorId,
                    SessionId = sessionId,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    PageViewsCount = 0,
                    TotalDurationSeconds = 0,
                    InitialReferrer = req.Referrer,
                    Country = req.Country,
                    City = req.City,
                    UtmSource = req.UtmSource,
                    UtmMedium = req.UtmMedium,
                    UtmCampaign = req.UtmCampaign,
                    CreatedAt = now
                };
                db.VisitorSessions.Add(session);
                tracker.TotalVisits++;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                session.LastSeenAt = now;
                session.TotalDurationSeconds += Math.Max(0, req.DurationSeconds);
                session.UpdatedAt = now;
            }

            Guid? associatedLeadId = session.LeadId;
            Guid? associatedContactId = session.ContactId;
            bool identified = !string.IsNullOrWhiteSpace(session.IdentifiedEmail);

            // Handle email identification if provided
            if (!string.IsNullOrWhiteSpace(req.IdentifiedEmail))
            {
                var email = req.IdentifiedEmail.Trim().ToLowerInvariant();
                session.IdentifiedEmail = email;
                identified = true;

                var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.NormalizedEmail == email || l.Email == email, ct);
                if (lead != null)
                {
                    associatedLeadId = lead.Id;
                    session.LeadId = lead.Id;
                }
                else
                {
                    var contact = await db.Set<Contact>().FirstOrDefaultAsync(c => c.NormalizedEmail == email || c.Email == email, ct);
                    if (contact != null)
                    {
                        associatedContactId = contact.Id;
                        session.ContactId = contact.Id;
                    }
                    else if (tracker.AutoCreateLeads)
                    {
                        var newLead = new Lead
                        {
                            TenantId = tracker.TenantId,
                            FirstName = "Website",
                            LastName = "Visitor",
                            Email = email,
                            NormalizedEmail = email,
                            Source = "website",
                            Status = "new",
                            CreatedAt = now
                        };
                        db.Set<Lead>().Add(newLead);
                        await db.SaveChangesAsync(ct);
                        associatedLeadId = newLead.Id;
                        session.LeadId = newLead.Id;
                    }
                }

                // Backfill past sessions for this visitor
                if (associatedLeadId.HasValue || associatedContactId.HasValue)
                {
                    var priorSessions = await db.VisitorSessions
                        .Where(s => s.VisitorId == visitorId && s.Id != session.Id && s.LeadId == null && s.ContactId == null)
                        .ToListAsync(ct);

                    foreach (var ps in priorSessions)
                    {
                        ps.LeadId = associatedLeadId;
                        ps.ContactId = associatedContactId;
                        ps.IdentifiedEmail = email;
                    }
                }
            }

            // Create PageView
            var pageView = new VisitorPageView
            {
                TenantId = tracker.TenantId,
                SessionId = session.Id,
                VisitorId = visitorId,
                LeadId = associatedLeadId,
                Url = req.Url,
                Title = req.Title,
                Referrer = req.Referrer,
                DurationSeconds = req.DurationSeconds,
                ViewedAt = now,
                CreatedAt = now
            };
            db.VisitorPageViews.Add(pageView);

            session.PageViewsCount++;
            tracker.TotalPageViews++;

            // Publish timeline event if associated with lead
            if (associatedLeadId.HasValue)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    EntityType: "leads",
                    EntityId: associatedLeadId.Value,
                    Kind: "website.page_view",
                    Summary: $"Visited {req.Url} ({(string.IsNullOrWhiteSpace(req.Title) ? "Page" : req.Title)})",
                    Source: "visitor-tracking",
                    OccurredAt: now), ct);
            }

            await db.SaveChangesAsync(ct);

            return new TrackHitResultDto
            {
                Success = true,
                VisitorId = visitorId,
                SessionId = sessionId,
                Identified = identified,
                AssociatedLeadId = associatedLeadId,
                AssociatedContactId = associatedContactId,
                Message = "Hit recorded successfully."
            };
        }
    }

    public async Task<TrackHitResultDto> IdentifyVisitorAsync(IdentifyVisitorRequest req, CancellationToken ct = default)
    {
        var token = req.TrackingToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return new TrackHitResultDto { Success = false, Message = "TrackingToken is required." };

        var visitorId = req.VisitorId?.Trim();
        if (string.IsNullOrWhiteSpace(visitorId))
            return new TrackHitResultDto { Success = false, Message = "VisitorId is required." };

        var email = req.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return new TrackHitResultDto { Success = false, Message = "Email is required." };

        var tracker = await db.WebsiteTrackers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TrackingToken == token && !t.IsDeleted, ct);

        if (tracker == null || !tracker.IsActive)
            return new TrackHitResultDto { Success = false, Message = "Invalid or inactive tracking token." };

        using (tenant.Use(tracker.TenantId))
        {
            var isEnabled = await featureFlags.IsEnabledAsync("visitor-tracking", ct);
            if (!isEnabled)
                return new TrackHitResultDto { Success = false, Message = "Visitor tracking is disabled for this organization." };

            var now = DateTime.UtcNow;

            var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.NormalizedEmail == email || l.Email == email, ct);
            if (lead == null)
            {
                lead = new Lead
                {
                    TenantId = tracker.TenantId,
                    FirstName = string.IsNullOrWhiteSpace(req.FirstName) ? "Identified" : req.FirstName.Trim(),
                    LastName = string.IsNullOrWhiteSpace(req.LastName) ? "Visitor" : req.LastName.Trim(),
                    Email = email,
                    NormalizedEmail = email,
                    Phone = req.Phone?.Trim(),
                    CompanyName = req.CompanyName?.Trim(),
                    Source = "website",
                    Status = "new",
                    CreatedAt = now
                };
                db.Set<Lead>().Add(lead);
                await db.SaveChangesAsync(ct);
            }

            // Associate all sessions for this visitorId
            var sessions = await db.VisitorSessions
                .Where(s => s.VisitorId == visitorId)
                .ToListAsync(ct);

            foreach (var s in sessions)
            {
                s.LeadId = lead.Id;
                s.IdentifiedEmail = email;
            }

            // Update page views
            var pageViews = await db.VisitorPageViews
                .Where(pv => pv.VisitorId == visitorId)
                .ToListAsync(ct);

            foreach (var pv in pageViews)
            {
                pv.LeadId = lead.Id;
            }

            await timeline.PublishAsync(new TimelineEventInput(
                EntityType: "leads",
                EntityId: lead.Id,
                Kind: "website.identified",
                Summary: $"Visitor identified on website with email {email}",
                Source: "visitor-tracking",
                OccurredAt: now), ct);

            await db.SaveChangesAsync(ct);

            return new TrackHitResultDto
            {
                Success = true,
                VisitorId = visitorId,
                SessionId = sessions.FirstOrDefault()?.SessionId ?? string.Empty,
                Identified = true,
                AssociatedLeadId = lead.Id,
                Message = "Visitor successfully identified and linked to lead."
            };
        }
    }

    // ================= Insights & Sessions =================

    public async Task<IReadOnlyList<VisitorSessionDto>> GetVisitorSessionsAsync(Guid? trackerId = null, Guid? leadId = null, int limit = 50, CancellationToken ct = default)
    {
        var query = db.VisitorSessions
            .Include(s => s.PageViews)
            .AsQueryable();

        if (trackerId.HasValue)
            query = query.Where(s => s.TrackerId == trackerId.Value);

        if (leadId.HasValue)
            query = query.Where(s => s.LeadId == leadId.Value);

        var list = await query
            .OrderByDescending(s => s.LastSeenAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return list.Select(s => new VisitorSessionDto
        {
            Id = s.Id,
            TrackerId = s.TrackerId,
            VisitorId = s.VisitorId,
            SessionId = s.SessionId,
            LeadId = s.LeadId,
            ContactId = s.ContactId,
            IdentifiedEmail = s.IdentifiedEmail,
            FirstSeenAt = s.FirstSeenAt,
            LastSeenAt = s.LastSeenAt,
            PageViewsCount = s.PageViewsCount,
            TotalDurationSeconds = s.TotalDurationSeconds,
            Country = s.Country,
            City = s.City,
            UtmSource = s.UtmSource,
            UtmMedium = s.UtmMedium,
            UtmCampaign = s.UtmCampaign,
            PageViews = s.PageViews.OrderBy(pv => pv.ViewedAt).Select(pv => new VisitorPageViewDto
            {
                Id = pv.Id,
                Url = pv.Url,
                Title = pv.Title,
                Referrer = pv.Referrer,
                DurationSeconds = pv.DurationSeconds,
                ViewedAt = pv.ViewedAt
            }).ToList()
        }).ToList();
    }

    public async Task<VisitorAnalyticsDto> GetAnalyticsSummaryAsync(Guid? trackerId = null, CancellationToken ct = default)
    {
        var sessionQuery = db.VisitorSessions.AsQueryable();
        var pvQuery = db.VisitorPageViews.AsQueryable();

        if (trackerId.HasValue)
        {
            sessionQuery = sessionQuery.Where(s => s.TrackerId == trackerId.Value);
            var sessionIds = await sessionQuery.Select(s => s.Id).ToListAsync(ct);
            pvQuery = pvQuery.Where(pv => sessionIds.Contains(pv.SessionId));
        }

        var totalSessions = await sessionQuery.CountAsync(ct);
        var activeThreshold = DateTime.UtcNow.AddMinutes(-15);
        var activeNow = await sessionQuery.CountAsync(s => s.LastSeenAt >= activeThreshold, ct);
        var totalViews = await pvQuery.CountAsync(ct);

        var avgDuration = totalSessions > 0
            ? await sessionQuery.AverageAsync(s => (double)s.TotalDurationSeconds, ct)
            : 0;

        var topPages = await pvQuery
            .GroupBy(pv => pv.Url)
            .Select(g => new TopPageDto { Url = g.Key, ViewsCount = g.Count() })
            .OrderByDescending(tp => tp.ViewsCount)
            .Take(10)
            .ToListAsync(ct);

        return new VisitorAnalyticsDto
        {
            TotalVisitors = totalSessions,
            ActiveNowCount = activeNow,
            TotalPageViews = totalViews,
            AverageDurationSeconds = Math.Round(avgDuration, 1),
            TopPages = topPages
        };
    }

    // ================= Helper Methods =================

    private static string[] CleanArray(string[]? input)
    {
        if (input == null || input.Length == 0) return [];
        return input
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WebsiteTrackerDto MapTrackerDto(WebsiteTracker t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Domain = t.Domain,
        TrackingToken = t.TrackingToken,
        IsActive = t.IsActive,
        AutoCreateLeads = t.AutoCreateLeads,
        AllowedOrigins = t.AllowedOrigins,
        TotalVisits = t.TotalVisits,
        TotalPageViews = t.TotalPageViews,
        EmbedSnippet = $"<script src=\"/api/v1/visitor-tracking/tracker.js\" data-tracker-token=\"{t.TrackingToken}\" async></script>",
        CreatedAt = t.CreatedAt
    };
}
