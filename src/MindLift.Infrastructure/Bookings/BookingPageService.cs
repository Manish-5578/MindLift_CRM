using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Bookings;

/// <summary>Shared reading and writing of the JSON stored on a booking page.</summary>
public static class BookingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly string[] DayKeys = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    public static AvailabilityDto Availability(string json)
    {
        var parsed = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AvailabilityDto>(json, Options);
        return parsed ?? new AvailabilityDto("UTC", new Dictionary<string, IReadOnlyList<TimeRangeDto>>(), []);
    }

    public static List<Guid> Users(string json) => JsonSerializer.Deserialize<List<Guid>>(json, Options) ?? [];

    public static List<BookingQuestionDto> Questions(string json) => JsonSerializer.Deserialize<List<BookingQuestionDto>>(json, Options) ?? [];

    public static List<int> Offsets(string json) => JsonSerializer.Deserialize<List<int>>(json, Options) ?? [];

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Minutes since midnight for an "HH:mm" text, or null when it is not a time of day.</summary>
    public static int? Minutes(string? text)
        => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.Hour * 60 + time.Minute : null;
}

/// <summary>Booking page settings: create, edit, delete and list the bookings that came through a page (plan 7.9).</summary>
public sealed class BookingPageService(AppDbContext db, ITenantContext tenant)
{
    private static readonly Regex SlugPattern = new("^[a-z0-9](?:[a-z0-9-]{0,58}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly string[] LocationTypes = ["InPerson", "Phone", "Video"];
    private static readonly string[] AssignmentModes = ["SingleUser", "RoundRobin"];
    private static readonly string[] QuestionTypes = ["text", "textarea"];

    public async Task<IReadOnlyList<BookingPageDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantSlug = await TenantSlugAsync(cancellationToken);
        return (await db.BookingPages.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken)).Select(p => Map(p, tenantSlug)).ToList();
    }

    public async Task<BookingPageDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Map(await LoadAsync(id, cancellationToken), await TenantSlugAsync(cancellationToken));

    public async Task<BookingPageDto> SaveAsync(Guid? id, SaveBookingPageRequest request, CancellationToken cancellationToken = default)
    {
        var page = id is { } existing ? await LoadAsync(existing, cancellationToken) : new BookingPage();
        await ValidateAsync(request, id, cancellationToken);

        page.Slug = request.Slug.Trim().ToLowerInvariant();
        page.Name = request.Name.Trim();
        page.Description = RecordHelpers.Clean(request.Description);
        page.DurationMinutes = request.DurationMinutes;
        page.BufferBeforeMinutes = request.BufferBeforeMinutes;
        page.BufferAfterMinutes = request.BufferAfterMinutes;
        page.SlotStepMinutes = request.SlotStepMinutes;
        page.MinNoticeHours = request.MinNoticeHours;
        page.MaxDaysAhead = request.MaxDaysAhead;
        page.AssignmentMode = request.AssignmentMode;
        page.UserIds = BookingJson.Write(request.UserIds.Distinct().ToList());
        page.Availability = BookingJson.Write(Normalize(request.Availability));
        page.Questions = BookingJson.Write(request.Questions.Select(q => q with { Key = q.Key.Trim(), Label = q.Label.Trim() }).ToList());
        page.LocationType = request.LocationType;
        page.LocationValue = RecordHelpers.Clean(request.LocationValue);
        page.ReminderOffsetsMinutes = BookingJson.Write(request.ReminderOffsetsMinutes.Distinct().OrderByDescending(m => m).ToList());
        page.IsActive = request.IsActive;

        if (id is null)
        {
            db.BookingPages.Add(page);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(page, await TenantSlugAsync(cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var page = await LoadAsync(id, cancellationToken);
        if (await db.Bookings.AnyAsync(b => b.BookingPageId == id && b.Status == BookingStatus.Confirmed && b.StartAt > DateTime.UtcNow, cancellationToken))
        {
            throw new BusinessRuleException("page_has_bookings", "This page has upcoming bookings. Cancel them first, or switch the page off instead.");
        }

        db.BookingPages.Remove(page);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BookingListItemDto>> BookingsAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        var page = await LoadAsync(pageId, cancellationToken);
        var rows = await db.Bookings.AsNoTracking().Where(b => b.BookingPageId == pageId).OrderByDescending(b => b.StartAt).Take(200).ToListAsync(cancellationToken);
        var names = await RecordHelpers.OwnerNamesAsync(db, rows.Select(r => r.UserId).Distinct(), cancellationToken);
        return rows.Select(b => new BookingListItemDto(b.Id, page.Name, b.Name, b.Email, b.StartAt, b.EndAt, b.Status, b.UserId, names.GetValueOrDefault(b.UserId))).ToList();
    }

    private async Task ValidateAsync(SaveBookingPageRequest r, Guid? id, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        void Fail(string key, string message) => errors[key] = [message];

        if (string.IsNullOrWhiteSpace(r.Name)) Fail("name", "Name is required.");
        else if (r.Name.Trim().Length > 150) Fail("name", "Use at most 150 characters.");

        var slug = (r.Slug ?? string.Empty).Trim().ToLowerInvariant();
        if (!SlugPattern.IsMatch(slug)) Fail("slug", "Use 1 to 60 lowercase letters, numbers and hyphens, starting and ending with a letter or number.");
        else if (await db.BookingPages.AnyAsync(p => p.Slug == slug && p.Id != id, cancellationToken)) Fail("slug", "Another booking page already uses this address.");

        if (r.DurationMinutes is < 5 or > 480) Fail("durationMinutes", "A meeting lasts between 5 minutes and 8 hours.");
        if (r.BufferBeforeMinutes is < 0 or > 240) Fail("bufferBeforeMinutes", "Use 0 to 240 minutes.");
        if (r.BufferAfterMinutes is < 0 or > 240) Fail("bufferAfterMinutes", "Use 0 to 240 minutes.");
        if (r.SlotStepMinutes is < 5 or > 240) Fail("slotStepMinutes", "Offer start times every 5 to 240 minutes.");
        if (r.MinNoticeHours is < 0 or > 720) Fail("minNoticeHours", "Use 0 to 720 hours.");
        if (r.MaxDaysAhead is < 1 or > 365) Fail("maxDaysAhead", "Allow bookings 1 to 365 days ahead.");
        if (!AssignmentModes.Contains(r.AssignmentMode)) Fail("assignmentMode", "Choose single person or round robin.");
        if (!LocationTypes.Contains(r.LocationType)) Fail("locationType", "Choose in person, phone or video.");
        if (r.LocationValue is { Length: > 300 }) Fail("locationValue", "Use at most 300 characters.");
        if (r.Description is { Length: > 2000 }) Fail("description", "Use at most 2000 characters.");

        var userIds = (r.UserIds ?? []).Distinct().ToList();
        if (userIds.Count == 0) Fail("userIds", "Choose who takes the bookings.");
        else if (r.AssignmentMode == "SingleUser" && userIds.Count > 1) Fail("userIds", "Single person pages have one person. Use round robin for a team.");
        else if (await db.Users.CountAsync(u => userIds.Contains(u.Id) && u.IsActive, cancellationToken) != userIds.Count) Fail("userIds", "Choose active people of this workspace.");

        if (r.ReminderOffsetsMinutes is null || r.ReminderOffsetsMinutes.Count > 5 || r.ReminderOffsetsMinutes.Any(m => m is < 5 or > 10080)) Fail("reminderOffsetsMinutes", "Add up to 5 reminders, each 5 minutes to 7 days before the meeting.");

        var questions = r.Questions ?? [];
        if (questions.Count > 10 || questions.Any(q => string.IsNullOrWhiteSpace(q.Label) || q.Label.Length > 200 || string.IsNullOrWhiteSpace(q.Key) || q.Key.Length > 40 || !Regex.IsMatch(q.Key, "^[a-z0-9_]+$") || !QuestionTypes.Contains(q.Type))
            || questions.Select(q => q.Key).Distinct().Count() != questions.Count)
        {
            Fail("questions", "Use up to 10 questions, each with a label, a unique key of lowercase letters, numbers and underscores, and type text or textarea.");
        }

        var availability = r.Availability;
        if (availability is null || !TimeZoneRules.Exists(availability.TimeZone)) Fail("availability", "Choose a valid time zone for the opening hours.");
        else
        {
            var problem = AvailabilityProblem(availability);
            if (problem is not null) Fail("availability", problem);
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>The first thing wrong with the opening hours, or null when they are fine.</summary>
    private static string? AvailabilityProblem(AvailabilityDto a)
    {
        foreach (var (day, ranges) in a.Weekly)
        {
            if (!BookingJson.DayKeys.Contains(day))
            {
                return $"{day} is not a day of the week.";
            }

            var problem = RangesProblem(ranges, day);
            if (problem is not null)
            {
                return problem;
            }
        }

        foreach (var o in a.DateOverrides)
        {
            if (!DateOnly.TryParseExact(o.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return $"{o.Date} is not a date (use yyyy-MM-dd).";
            }

            var problem = RangesProblem(o.Ranges, o.Date);
            if (problem is not null)
            {
                return problem;
            }
        }

        return a.DateOverrides.Select(o => o.Date).Distinct().Count() != a.DateOverrides.Count ? "A date appears twice in the exceptions." : null;
    }

    private static string? RangesProblem(IReadOnlyList<TimeRangeDto> ranges, string label)
    {
        if (ranges.Count > 6)
        {
            return $"Use at most 6 time ranges for {label}.";
        }

        var parsed = new List<(int From, int To)>();
        foreach (var range in ranges)
        {
            var from = BookingJson.Minutes(range.From);
            var to = BookingJson.Minutes(range.To);
            if (from is null || to is null)
            {
                return $"Times for {label} must look like 09:00.";
            }

            if (to <= from)
            {
                return $"For {label}, each range must end after it starts.";
            }

            parsed.Add((from.Value, to.Value));
        }

        var ordered = parsed.OrderBy(p => p.From).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].From < ordered[i - 1].To)
            {
                return $"The ranges for {label} overlap.";
            }
        }

        return null;
    }

    /// <summary>Stores the opening hours in one tidy shape: every day present, ranges in order.</summary>
    private static AvailabilityDto Normalize(AvailabilityDto a)
    {
        var weekly = BookingJson.DayKeys.ToDictionary(
            day => day,
            day => (IReadOnlyList<TimeRangeDto>)(a.Weekly.TryGetValue(day, out var ranges) ? ranges.OrderBy(r => r.From, StringComparer.Ordinal).ToList() : []));
        return new AvailabilityDto(a.TimeZone, weekly, a.DateOverrides.OrderBy(o => o.Date, StringComparer.Ordinal)
            .Select(o => new DateOverrideDto(o.Date, o.Ranges.OrderBy(r => r.From, StringComparer.Ordinal).ToList())).ToList());
    }

    private async Task<BookingPage> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await db.BookingPages.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Booking page", id);

    private async Task<string> TenantSlugAsync(CancellationToken cancellationToken)
    {
        var id = tenant.TenantId ?? throw new ForbiddenException("No tenant is set for this operation.");
        return await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.Slug).FirstAsync(cancellationToken);
    }

    private BookingPageDto Map(BookingPage p, string tenantSlug) => new(
        p.Id, p.Slug, p.Name, p.Description, p.DurationMinutes, p.BufferBeforeMinutes, p.BufferAfterMinutes, p.SlotStepMinutes, p.MinNoticeHours, p.MaxDaysAhead,
        p.AssignmentMode, BookingJson.Users(p.UserIds), BookingJson.Availability(p.Availability), BookingJson.Questions(p.Questions), p.LocationType, p.LocationValue,
        BookingJson.Offsets(p.ReminderOffsetsMinutes), p.IsActive, $"/book/{tenantSlug}/{p.Slug}");
}
