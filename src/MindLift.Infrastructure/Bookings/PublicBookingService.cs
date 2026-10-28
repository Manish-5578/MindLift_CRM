using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Activities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Leads;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Bookings;

/// <summary>
/// The booking pages customers use without signing in (plan 7.9). The workspace comes from the address; everything after that runs inside it.
/// Booking takes a lock per person so two customers choosing the same time cannot both get it.
/// </summary>
public sealed class PublicBookingService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    AvailabilityService availability,
    LeadService leads,
    INotificationService notifications,
    IEmailSender email,
    IOptions<AppOptions> app,
    ILogger<PublicBookingService> logger)
{
    private const int MaxAnswerLength = 2000;

    public async Task<PublicBookingPageDto> PageAsync(string tenantSlug, string pageSlug, CancellationToken cancellationToken = default)
    {
        var (workspace, page) = await FindAsync(tenantSlug, pageSlug, cancellationToken);
        using var scope = tenant.Use(workspace.Id);
        return new PublicBookingPageDto(workspace.Name, page.Name, page.Description, page.DurationMinutes, BookingJson.Questions(page.Questions),
            page.LocationType, page.LocationValue, page.MaxDaysAhead, BookingJson.Availability(page.Availability).TimeZone);
    }

    /// <summary>The free times between two dates, which are days in the visitor's own time zone.</summary>
    public async Task<IReadOnlyList<SlotDto>> SlotsAsync(string tenantSlug, string pageSlug, DateOnly from, DateOnly to, string? timeZone, CancellationToken cancellationToken = default)
    {
        var (workspace, page) = await FindAsync(tenantSlug, pageSlug, cancellationToken);
        var zone = ZoneOrPage(timeZone, page);
        if (to < from || to.DayNumber - from.DayNumber > 31)
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["to"] = ["Ask for at most 31 days at a time, and end on or after the start."] });
        }

        using var scope = tenant.Use(workspace.Id);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
        return await availability.SlotsAsync(page, fromUtc, toUtc, null, cancellationToken);
    }

    public async Task<BookingConfirmationDto> BookAsync(string tenantSlug, string pageSlug, BookRequest request, CancellationToken cancellationToken = default)
    {
        var (workspace, page) = await FindAsync(tenantSlug, pageSlug, cancellationToken);

        // Bots fill the hidden field. They get what looks like a confirmation and nothing is stored.
        if (!string.IsNullOrEmpty(request.Hp))
        {
            return new BookingConfirmationDto(page.Name, request.Start, request.Start.AddMinutes(page.DurationMinutes), Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), "/booking/manage/unknown");
        }

        using var scope = tenant.Use(workspace.Id);
        var questions = BookingJson.Questions(page.Questions);
        Validate(request, questions);
        var zoneName = ZoneOrPage(request.TimeZone, page).Id;
        var start = DateTime.SpecifyKind(request.Start, DateTimeKind.Utc);
        var end = start.AddMinutes(page.DurationMinutes);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock every candidate in a fixed order, then look again: the answer from before the lock may be out of date.
        var candidates = await availability.FreePeopleAsync(page, start, null, cancellationToken);
        await LockAsync(candidates.Order().ToList(), cancellationToken);
        var free = await availability.FreePeopleAsync(page, start, null, cancellationToken);
        var offered = (await availability.SlotsAsync(page, start, start.AddMinutes(1), null, cancellationToken)).Any(s => s.StartUtc == start);
        if (free.Count == 0 || !offered)
        {
            throw new ConflictException("slot_taken", "Sorry, that time was just taken. Please choose another.");
        }

        var assigned = await ChooseAsync(page, free, start, cancellationToken);
        var (contactId, leadId, accountId) = await FindOrCreatePersonAsync(request, assigned, cancellationToken);

        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        var answers = request.Answers ?? new Dictionary<string, string>();
        var activity = new Activity
        {
            Type = ActivityTypes.Meeting,
            Subject = $"{page.Name}: {request.Name.Trim()}",
            Description = DescribeAnswers(questions, answers),
            Status = "Open",
            DueAt = start,
            EndAt = end,
            Location = page.LocationValue,
            OwnerId = assigned,
            ContactId = contactId,
            LeadId = leadId,
            AccountId = accountId,
            Attendees = BookingJson.Write(new[] { request.Email.Trim().ToLowerInvariant() }),
        };
        db.Activities.Add(activity);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var booking = new Domain.Entities.Booking
        {
            BookingPageId = page.Id,
            ActivityId = activity.Id,
            ContactId = contactId,
            LeadId = leadId,
            UserId = assigned,
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = RecordHelpers.Clean(request.Phone),
            Answers = BookingJson.Write(answers),
            StartAt = start,
            EndAt = end,
            Status = BookingStatus.Confirmed,
            ManageTokenHash = Hash(token),
            TimeZone = zoneName,
            RemindersSent = BookingJson.Write(OffsetsAlreadyPassed(page, start)),
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _ = settings;
        await SendConfirmationAsync(workspace, page, booking, assigned, token, "Booking confirmed", false, cancellationToken);
        await TellPersonAsync(page, booking, NotificationTypes.BookingCreated, $"New booking: {booking.Name}", cancellationToken);

        return new BookingConfirmationDto(page.Name, start, end, token, $"/booking/manage/{token}");
    }

    public async Task<ManageBookingDto> ManageAsync(string token, CancellationToken cancellationToken = default)
    {
        var (workspace, page, booking) = await ResolveAsync(token, cancellationToken);
        using var scope = tenant.Use(workspace.Id);
        return new ManageBookingDto(workspace.Name, page.Name, workspace.Slug, page.Slug, booking.Name, booking.StartAt, booking.EndAt, booking.Status,
            page.LocationType, page.LocationValue, page.DurationMinutes, booking.Status == BookingStatus.Confirmed && booking.StartAt > clock.UtcNow, booking.TimeZone);
    }

    public async Task CancelAsync(string token, CancelBookingRequest request, CancellationToken cancellationToken = default)
    {
        var (workspace, page, found) = await ResolveAsync(token, cancellationToken);
        using var scope = tenant.Use(workspace.Id);
        var booking = await db.Bookings.FirstAsync(b => b.Id == found.Id, cancellationToken);
        EnsureChangeable(booking);

        booking.Status = BookingStatus.Cancelled;
        booking.CancelledAt = clock.UtcNow;
        booking.CancelReason = RecordHelpers.Clean(request.Reason) is { } reason ? (reason.Length > 500 ? reason[..500] : reason) : null;
        var cancelledToken = Guid.NewGuid().ToString("N");
        booking.ManageTokenHash = Hash(cancelledToken);
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == booking.ActivityId, cancellationToken);
        if (activity is not null)
        {
            activity.Status = "Cancelled";
        }

        await db.SaveChangesAsync(cancellationToken);
        await SendConfirmationAsync(workspace, page, booking, booking.UserId, cancelledToken, "Booking cancelled", true, cancellationToken);
        await TellPersonAsync(page, booking, NotificationTypes.BookingCancelled, $"Booking cancelled: {booking.Name}", cancellationToken);
    }

    public async Task<BookingConfirmationDto> RescheduleAsync(string token, RescheduleBookingRequest request, CancellationToken cancellationToken = default)
    {
        var (workspace, page, found) = await ResolveAsync(token, cancellationToken);
        if (MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow))
        {
            // A lapsed workspace takes no new bookings, and moving one is a new booking. The customer can still cancel.
            throw new NotFoundException("Booking page", page.Slug);
        }

        using var scope = tenant.Use(workspace.Id);
        var start = DateTime.SpecifyKind(request.Start, DateTimeKind.Utc);
        var end = start.AddMinutes(page.DurationMinutes);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var booking = await db.Bookings.FirstAsync(b => b.Id == found.Id, cancellationToken);
        EnsureChangeable(booking);

        var candidates = await availability.FreePeopleAsync(page, start, booking.ActivityId, cancellationToken);
        await LockAsync(candidates.Order().ToList(), cancellationToken);
        var free = await availability.FreePeopleAsync(page, start, booking.ActivityId, cancellationToken);
        var offered = (await availability.SlotsAsync(page, start, start.AddMinutes(1), booking.ActivityId, cancellationToken)).Any(s => s.StartUtc == start);
        if (free.Count == 0 || !offered)
        {
            throw new ConflictException("slot_taken", "Sorry, that time is not available. Please choose another.");
        }

        // The same person keeps the booking when they are free; otherwise a free colleague on a round robin page takes it.
        var assigned = free.Contains(booking.UserId) ? booking.UserId : await ChooseAsync(page, free, start, cancellationToken);
        booking.StartAt = start;
        booking.EndAt = end;
        booking.UserId = assigned;
        booking.TimeZone = ZoneOrPage(request.TimeZone, page).Id;
        booking.RemindersSent = BookingJson.Write(OffsetsAlreadyPassed(page, start));

        var newToken = Guid.NewGuid().ToString("N");
        booking.ManageTokenHash = Hash(newToken);

        var activity = await db.Activities.FirstAsync(a => a.Id == booking.ActivityId, cancellationToken);
        activity.DueAt = start;
        activity.EndAt = end;
        activity.OwnerId = assigned;
        activity.ReminderSentAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await SendConfirmationAsync(workspace, page, booking, assigned, newToken, "Booking moved", false, cancellationToken);
        await TellPersonAsync(page, booking, NotificationTypes.BookingCreated, $"Booking moved: {booking.Name}", cancellationToken);
        return new BookingConfirmationDto(page.Name, start, end, newToken, $"/booking/manage/{newToken}");
    }

    // ---------- helpers ----------

    private async Task<(Tenant Workspace, BookingPage Page)> FindAsync(string tenantSlug, string pageSlug, CancellationToken cancellationToken)
    {
        var workspace = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug.ToLower(), cancellationToken);
        if (workspace is null || MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, clock.UtcNow))
        {
            throw new NotFoundException("Booking page", pageSlug);
        }

        var page = await db.BookingPages.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == workspace.Id && p.Slug == pageSlug.ToLower() && p.IsActive && !p.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Booking page", pageSlug);
        return (workspace, page);
    }

    private async Task<(Tenant Workspace, BookingPage Page, Booking Booking)> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token ?? string.Empty);
        var booking = await db.Bookings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(b => b.ManageTokenHash == hash && !b.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Booking", "link");
        var workspace = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == booking.TenantId, cancellationToken);
        var page = await db.BookingPages.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == booking.BookingPageId, cancellationToken);
        return (workspace, page, booking);
    }

    private void EnsureChangeable(Booking booking)
    {
        if (booking.Status != BookingStatus.Confirmed)
        {
            throw new ConflictException("booking_not_active", "This booking was already cancelled.");
        }

        if (booking.StartAt <= clock.UtcNow)
        {
            throw new BusinessRuleException("booking_in_past", "This meeting has already started, so it can no longer be changed here.");
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static TimeZoneInfo ZoneOrPage(string? timeZone, BookingPage page)
        => !string.IsNullOrWhiteSpace(timeZone) && TimeZoneRules.Exists(timeZone)
            ? TimeZoneInfo.FindSystemTimeZoneById(timeZone)
            : TimeZoneInfo.FindSystemTimeZoneById(BookingJson.Availability(page.Availability).TimeZone);

    private static void Validate(BookRequest request, IReadOnlyList<BookingQuestionDto> questions)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["Enter your name."];
        else if (request.Name.Trim().Length > 100) errors["name"] = ["Use at most 100 characters."];

        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 254 || !MailAddress.TryCreate(request.Email.Trim(), out var address) || address.Address != request.Email.Trim())
        {
            errors["email"] = ["Enter a valid email address, so we can send your confirmation."];
        }

        if (request.Phone is { Length: > 40 }) errors["phone"] = ["Use at most 40 characters."];

        var answers = request.Answers ?? new Dictionary<string, string>();
        foreach (var question in questions)
        {
            answers.TryGetValue(question.Key, out var value);
            if (question.Required && string.IsNullOrWhiteSpace(value)) errors[$"answers.{question.Key}"] = ["Please answer this question."];
            else if (value is { Length: > MaxAnswerLength }) errors[$"answers.{question.Key}"] = [$"Use at most {MaxAnswerLength} characters."];
        }

        if (answers.Keys.Any(k => questions.All(q => q.Key != k))) errors["answers"] = ["One of the answers is not for a question on this page."];

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private async Task LockAsync(IReadOnlyList<Guid> people, CancellationToken cancellationToken)
    {
        foreach (var person in people)
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({person.ToString()}))", cancellationToken);
        }
    }

    /// <summary>Round robin goes to whoever has the fewest bookings that week; single person pages have only one choice.</summary>
    private async Task<Guid> ChooseAsync(BookingPage page, IReadOnlyList<Guid> free, DateTime start, CancellationToken cancellationToken)
    {
        if (free.Count == 1 || page.AssignmentMode != "RoundRobin")
        {
            return free[0];
        }

        var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        var counts = await db.Bookings.AsNoTracking()
            .Where(b => free.Contains(b.UserId) && b.Status == BookingStatus.Confirmed && b.StartAt >= weekStart && b.StartAt < weekStart.AddDays(7))
            .GroupBy(b => b.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return free.OrderBy(u => counts.GetValueOrDefault(u)).First();
    }

    /// <summary>A known contact is reused, then an open lead, and only then is a new lead made (source Booking).</summary>
    private async Task<(Guid? ContactId, Guid? LeadId, Guid? AccountId)> FindOrCreatePersonAsync(BookRequest request, Guid owner, CancellationToken cancellationToken)
    {
        var normalizedEmail = LeadNormalizer.Email(request.Email);
        var normalizedPhone = LeadNormalizer.Phone(request.Phone);

        var contact = await db.Contacts.AsNoTracking()
            .Where(c => (normalizedEmail != null && c.NormalizedEmail == normalizedEmail) || (normalizedPhone != null && c.NormalizedPhone == normalizedPhone))
            .OrderBy(c => c.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (contact is not null)
        {
            return (contact.Id, null, contact.AccountId);
        }

        var lead = await db.Leads.AsNoTracking()
            .Where(l => l.ConvertedAt == null && ((normalizedEmail != null && l.NormalizedEmail == normalizedEmail) || (normalizedPhone != null && l.NormalizedPhone == normalizedPhone)))
            .OrderBy(l => l.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (lead is not null)
        {
            return (null, lead.Id, null);
        }

        var parts = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var created = await leads.CreateAsync(new SaveLeadRequest(parts[0], parts.Length > 1 ? parts[1] : string.Empty, request.Email.Trim(), RecordHelpers.Clean(request.Phone),
            null, "new", "booking", null, null, null, owner, null), cancellationToken);
        return (null, created.Id, null);
    }

    private static string? DescribeAnswers(IReadOnlyList<BookingQuestionDto> questions, IReadOnlyDictionary<string, string> answers)
    {
        var lines = questions.Where(q => answers.TryGetValue(q.Key, out var v) && !string.IsNullOrWhiteSpace(v)).Select(q => $"{q.Label}\n{answers[q.Key].Trim()}").ToList();
        return lines.Count == 0 ? null : string.Join("\n\n", lines);
    }

    /// <summary>Reminders whose time had already gone when the booking was made are marked sent, so a late booking is not flooded.</summary>
    private List<int> OffsetsAlreadyPassed(BookingPage page, DateTime start)
        => BookingJson.Offsets(page.ReminderOffsetsMinutes).Where(offset => start.AddMinutes(-offset) <= clock.UtcNow).ToList();

    private async Task SendConfirmationAsync(Tenant workspace, BookingPage page, Booking booking, Guid userId, string token, string headline, bool cancel, CancellationToken cancellationToken)
    {
        try
        {
            var host = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { Name = (u.FirstName + " " + u.LastName).Trim(), u.Email }).FirstAsync(cancellationToken);
            var zone = TimeZoneRules.Exists(booking.TimeZone) ? TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZone) : TimeZoneInfo.Utc;
            var local = TimeZoneInfo.ConvertTimeFromUtc(booking.StartAt, zone).ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var link = $"{app.Value.BaseUrl}/booking/manage/{token}";
            var where = string.IsNullOrWhiteSpace(page.LocationValue) ? page.LocationType : $"{page.LocationType}: {page.LocationValue}";
            var body = cancel
                ? $"Hello {booking.Name},\n\nYour booking with {workspace.Name} has been cancelled.\n\n{page.Name}\n{local} ({zone.Id})\n"
                : $"Hello {booking.Name},\n\n{headline}.\n\n{page.Name} with {host.Name} at {workspace.Name}\n{local} ({zone.Id})\nWhere: {where}\n\nNeed to change it? Cancel or choose another time here:\n{link}\n\nThe calendar file is attached.";

            var meeting = new IcsBuilder.Meeting(booking.ActivityId, page.Name, null, page.LocationValue, booking.StartAt, booking.EndAt, host.Name, host.Email ?? "no-reply@mindlift.local",
                [booking.Email], await db.Activities.AsNoTracking().Where(a => a.Id == booking.ActivityId).Select(a => a.Version).FirstOrDefaultAsync(cancellationToken));
            var ics = Encoding.UTF8.GetBytes(IcsBuilder.Build(meeting, cancel));
            await email.SendAsync(booking.Email, $"{headline}: {page.Name}", body,
                [new EmailAttachment("booking.ics", $"text/calendar; method={(cancel ? "CANCEL" : "REQUEST")}; charset=UTF-8", ics)], cancellationToken);
        }
        catch (Exception ex)
        {
            // The booking is made either way; a mail problem must not undo it.
            logger.LogWarning(ex, "Could not send the booking email for booking {BookingId}", booking.Id);
        }
    }

    private Task TellPersonAsync(BookingPage page, Booking booking, string type, string title, CancellationToken cancellationToken)
        => notifications.NotifyAsync(new NotificationRequest(
            [booking.UserId], type, title, $"{page.Name}, {booking.StartAt:ddd d MMM HH:mm} UTC", "activities", booking.ActivityId, $"/calendar?date={booking.StartAt:yyyy-MM-dd}"), cancellationToken);
}
