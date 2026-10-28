using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class MeetingLinkService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline) : IMeetingLinkService
{
    private static readonly Regex SlugCleaner = new("[^a-z0-9-]", RegexOptions.Compiled);

    private static string NormalizeSlug(string? input, string fallbackTitle)
    {
        var raw = string.IsNullOrWhiteSpace(input) ? fallbackTitle : input;
        var slug = SlugCleaner.Replace(raw.Trim().ToLowerInvariant().Replace(' ', '-'), "");
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "meet-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        }
        return slug.Length > 60 ? slug[..60] : slug;
    }

    public async Task<MeetingLinkDto> CreateMeetingLinkAsync(CreateMeetingLinkRequest request, Guid userId, string userName, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("title", "Meeting link title is required.");

        var baseSlug = NormalizeSlug(request.Slug, request.Title);
        var finalSlug = baseSlug;

        // Ensure unique slug within tenant
        var conflict = await db.MeetingLinks.AnyAsync(l => l.Slug == finalSlug, ct);
        if (conflict)
        {
            finalSlug = $"{baseSlug}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
        }

        // Resolve or default booking page
        Guid? bookingPageId = request.BookingPageId;
        if (bookingPageId == null)
        {
            var defaultPage = await db.BookingPages.FirstOrDefaultAsync(p => p.IsActive, ct);
            if (defaultPage != null)
            {
                bookingPageId = defaultPage.Id;
            }
        }

        var link = new MeetingLink
        {
            TenantId = tenantId,
            UserId = userId,
            BookingPageId = bookingPageId,
            Slug = finalSlug,
            Title = request.Title.Trim(),
            Description = request.Description,
            DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : 30,
            LocationType = string.IsNullOrWhiteSpace(request.LocationType) ? "Video" : request.LocationType,
            LocationValue = request.LocationValue,
            LeadId = request.LeadId,
            ContactId = request.ContactId,
            DealId = request.DealId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail?.Trim().ToLowerInvariant(),
            CustomerPhone = request.CustomerPhone,
            IsSingleUse = request.IsSingleUse,
            IsUsed = false,
            IsActive = true,
            ClickCount = 0,
            BookingCount = 0,
            ExpiresAtUtc = request.ExpiresAtUtc,
            ShareableUrl = $"/book/m/{finalSlug}"
        };

        db.MeetingLinks.Add(link);
        await db.SaveChangesAsync(ct);

        return MapDto(link, userName);
    }

    public async Task<MeetingLinkDto> UpdateMeetingLinkAsync(Guid id, UpdateMeetingLinkRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var link = await db.MeetingLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link == null)
            throw new NotFoundException("meeting_link_not_found", "Meeting link was not found.");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ValidationException("title", "Title is required.");

        link.Title = request.Title.Trim();
        link.Description = request.Description;
        link.DurationMinutes = request.DurationMinutes > 0 ? request.DurationMinutes : link.DurationMinutes;
        link.LocationType = request.LocationType;
        link.LocationValue = request.LocationValue;
        link.IsActive = request.IsActive;
        link.ExpiresAtUtc = request.ExpiresAtUtc;

        await db.SaveChangesAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == link.UserId, ct);
        return MapDto(link, user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Host");
    }

    public async Task<IReadOnlyList<MeetingLinkDto>> GetMeetingLinksAsync(MeetingLinkQuery query, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var q = db.MeetingLinks.AsNoTracking().Where(l => l.TenantId == tenantId);

        if (query.UserId.HasValue) q = q.Where(l => l.UserId == query.UserId.Value);
        if (query.DealId.HasValue) q = q.Where(l => l.DealId == query.DealId.Value);
        if (query.ContactId.HasValue) q = q.Where(l => l.ContactId == query.ContactId.Value);
        if (query.LeadId.HasValue) q = q.Where(l => l.LeadId == query.LeadId.Value);
        if (query.IsActive.HasValue) q = q.Where(l => l.IsActive == query.IsActive.Value);

        var list = await q.OrderByDescending(l => l.CreatedAt)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToListAsync(ct);

        var userIds = list.Select(l => l.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        return list.Select(l => MapDto(l, users.GetValueOrDefault(l.UserId, "Host"))).ToList();
    }

    public async Task<MeetingLinkDto> GetMeetingLinkAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var link = await db.MeetingLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link == null)
            throw new NotFoundException("meeting_link_not_found", "Meeting link was not found.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == link.UserId, ct);
        return MapDto(link, user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Host");
    }

    public async Task<bool> DeleteMeetingLinkAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var link = await db.MeetingLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link == null)
            throw new NotFoundException("meeting_link_not_found", "Meeting link was not found.");

        link.IsActive = false;
        link.IsDeleted = true;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> TrackClickAsync(string slug, CancellationToken ct = default)
    {
        var link = await db.MeetingLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Slug == slug && !l.IsDeleted, ct);

        if (link == null) return false;

        using var scope = tenantContext.Use(link.TenantId);
        link.ClickCount++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<PublicMeetingLinkDto> GetPublicMeetingLinkAsync(string slug, CancellationToken ct = default)
    {
        var link = await db.MeetingLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Slug == slug && !l.IsDeleted, ct);

        if (link == null || !link.IsActive)
            throw new NotFoundException("meeting_link_not_found", "Meeting booking link is invalid or inactive.");

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == link.UserId, ct);
        var hostName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Representative";

        var workspace = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == link.TenantId, ct);
        var workspaceSlug = workspace?.Slug ?? "default";

        var bookingPage = link.BookingPageId.HasValue
            ? await db.BookingPages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == link.BookingPageId.Value, ct)
            : await db.BookingPages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == link.TenantId && p.IsActive, ct);

        var bookingPageSlug = bookingPage?.Slug ?? "default";

        var isExpired = link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow;
        var isAvailable = !isExpired && (!link.IsSingleUse || !link.IsUsed);

        return new PublicMeetingLinkDto(
            link.Slug,
            link.Title,
            link.Description,
            link.DurationMinutes,
            link.LocationType,
            hostName,
            link.CustomerName,
            link.CustomerEmail,
            link.CustomerPhone,
            isExpired,
            isAvailable,
            bookingPageSlug,
            workspaceSlug);
    }

    public async Task<BookingConfirmationDto> BookThroughLinkAsync(string slug, BookThroughMeetingLinkRequest request, CancellationToken ct = default)
    {
        var link = await db.MeetingLinks
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Slug == slug && !l.IsDeleted, ct);

        if (link == null || !link.IsActive)
            throw new NotFoundException("meeting_link_not_found", "Meeting booking link is invalid or inactive.");

        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow)
            throw new ValidationException("link_expired", "This meeting link has expired.");

        if (link.IsSingleUse && link.IsUsed)
            throw new ValidationException("link_used", "This single-use meeting link has already been used to schedule a meeting.");

        using var scope = tenantContext.Use(link.TenantId);

        var isEnabled = await featureFlags.IsEnabledAsync("meeting-links", ct);
        if (!isEnabled)
            throw new ForbiddenException();

        var customerEmail = (link.CustomerEmail ?? request.CustomerEmail).Trim().ToLowerInvariant();
        var customerName = (link.CustomerName ?? request.CustomerName).Trim();
        var customerPhone = link.CustomerPhone ?? request.CustomerPhone;

        // Resolve or create Contact / Lead
        Guid? contactId = link.ContactId;
        Guid? leadId = link.LeadId;
        Guid? dealId = link.DealId;
        Guid? accountId = null;

        if (contactId == null && leadId == null)
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.NormalizedEmail == customerEmail || c.Email == customerEmail, ct);
            if (contact != null)
            {
                contactId = contact.Id;
                accountId = contact.AccountId;
            }
            else
            {
                var lead = await db.Leads.FirstOrDefaultAsync(l => l.NormalizedEmail == customerEmail || l.Email == customerEmail, ct);
                if (lead != null)
                {
                    leadId = lead.Id;
                }
                else
                {
                    var newLead = new Lead
                    {
                        TenantId = link.TenantId,
                        FirstName = customerName.Split(' ').FirstOrDefault() ?? customerName,
                        LastName = customerName.Contains(' ') ? customerName[(customerName.IndexOf(' ') + 1)..] : "Lead",
                        Email = customerEmail,
                        NormalizedEmail = customerEmail,
                        Phone = customerPhone,
                        NormalizedPhone = customerPhone,
                        Source = "meeting-link",
                        CompanyName = "Meeting Inbound",
                        Status = "new"
                    };
                    db.Leads.Add(newLead);
                    await db.SaveChangesAsync(ct);
                    leadId = newLead.Id;
                }
            }
        }

        var startAt = request.StartAtUtc;
        var endAt = startAt.AddMinutes(link.DurationMinutes);

        // Create Calendar Activity
        var activity = new Activity
        {
            TenantId = link.TenantId,
            Type = "Meeting",
            Subject = link.Title,
            Status = "Open",
            DueAt = startAt,
            EndAt = endAt,
            DurationMinutes = link.DurationMinutes,
            Location = link.LocationValue ?? link.LocationType,
            OwnerId = link.UserId,
            LeadId = leadId,
            ContactId = contactId,
            AccountId = accountId,
            DealId = dealId,
            Attendees = JsonSerializer.Serialize(new[] { customerEmail })
        };
        db.Activities.Add(activity);
        await db.SaveChangesAsync(ct);

        // Create Booking record
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var booking = new Booking
        {
            TenantId = link.TenantId,
            BookingPageId = link.BookingPageId ?? Guid.Empty,
            ActivityId = activity.Id,
            ContactId = contactId,
            LeadId = leadId,
            UserId = link.UserId,
            Name = customerName,
            Email = customerEmail,
            Phone = customerPhone,
            Answers = request.Notes != null ? JsonSerializer.Serialize(new { notes = request.Notes }) : "{}",
            StartAt = startAt,
            EndAt = endAt,
            Status = "Confirmed",
            ManageTokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant(),
            TimeZone = "UTC"
        };
        db.Bookings.Add(booking);

        // Update link statistics
        link.BookingCount++;
        if (link.IsSingleUse)
        {
            link.IsUsed = true;
        }

        await db.SaveChangesAsync(ct);

        // Publish timeline event
        var eventSummary = $"Meeting '{link.Title}' booked for {startAt:g} UTC";
        await PublishTimelineAsync(leadId, contactId, dealId, "meeting_booked", eventSummary, ct);

        return new BookingConfirmationDto(link.Title, startAt, endAt, token, $"/booking/manage/{token}");
    }

    private async Task PublishTimelineAsync(Guid? leadId, Guid? contactId, Guid? dealId, string kind, string summary, CancellationToken ct)
    {
        try
        {
            if (leadId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("lead", leadId.Value, kind, summary, "meeting-links"), ct);
            }
            else if (contactId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("contact", contactId.Value, kind, summary, "meeting-links"), ct);
            }

            if (dealId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("deal", dealId.Value, kind, summary, "meeting-links"), ct);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private static MeetingLinkDto MapDto(MeetingLink l, string userName)
    {
        var rate = l.ClickCount > 0 ? Math.Round((decimal)l.BookingCount / l.ClickCount * 100m, 1) : 0m;
        return new MeetingLinkDto(
            l.Id,
            l.UserId,
            userName,
            l.BookingPageId,
            l.Slug,
            l.Title,
            l.Description,
            l.DurationMinutes,
            l.LocationType,
            l.LocationValue,
            l.LeadId,
            l.ContactId,
            l.DealId,
            l.CustomerName,
            l.CustomerEmail,
            l.CustomerPhone,
            l.IsSingleUse,
            l.IsUsed,
            l.IsActive,
            l.ClickCount,
            l.BookingCount,
            rate,
            l.ExpiresAtUtc,
            l.ShareableUrl);
    }
}
