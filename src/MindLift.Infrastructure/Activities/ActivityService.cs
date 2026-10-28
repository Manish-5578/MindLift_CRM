using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Activities;

/// <summary>Tasks, calls, meetings and notes, linked to leads, contacts, accounts and deals (plan 7.8).</summary>
public sealed class ActivityService(AppDbContext db, ITenantContext tenant, IClock clock, IDataScopeService scope, IEmailSender email, INotificationService notifications, IRecordSavePipeline savePipeline)
{
    private static readonly string[] Priorities = ["Low", "Normal", "High"];
    private static readonly string[] Directions = ["Inbound", "Outbound"];

    // ---------- reading ----------

    public async Task<PagedResult<ActivityDto>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = await ScopedAsync(cancellationToken);
        var (todayStart, todayEnd) = await TodayAsync(cancellationToken);

        source = (query.Tab?.ToLowerInvariant()) switch
        {
            "today" => source.Where(a => a.Status == "Open" && a.DueAt != null && a.DueAt >= todayStart && a.DueAt < todayEnd),
            "overdue" => source.Where(a => a.Status == "Open" && a.DueAt != null && a.DueAt < todayStart),
            "upcoming" => source.Where(a => a.Status == "Open" && (a.DueAt == null || a.DueAt >= todayEnd)),
            "completed" => source.Where(a => a.Status == "Completed"),
            _ => source,
        };

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            source = source.Where(a => a.Type == query.Type);
        }

        if (query.OwnerId is { } owner)
        {
            source = source.Where(a => a.OwnerId == owner);
        }
        else if (query.MineOnly && tenant.UserId is { } me)
        {
            source = source.Where(a => a.OwnerId == me);
        }

        if (query.LeadId is { } lead) source = source.Where(a => a.LeadId == lead);
        if (query.ContactId is { } contact) source = source.Where(a => a.ContactId == contact);
        if (query.AccountId is { } account) source = source.Where(a => a.AccountId == account);
        if (query.DealId is { } deal) source = source.Where(a => a.DealId == deal);

        var total = await source.CountAsync(cancellationToken);
        var completedTab = string.Equals(query.Tab, "completed", StringComparison.OrdinalIgnoreCase);
        var ordered = completedTab || string.IsNullOrWhiteSpace(query.Tab) || query.Tab == "all"
            ? source.OrderByDescending(a => a.CompletedAt ?? a.DueAt ?? a.CreatedAt).ThenBy(a => a.Id)
            : source.OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.Id);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<ActivityDto>(await MapAsync(rows, cancellationToken), total, page, pageSize);
    }

    public async Task<ActivityDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => (await MapAsync([await LoadScopedAsync(id, cancellationToken)], cancellationToken))[0];

    /// <summary>The signed-in person's open work: counts for the dashboard and the overdue and today items.</summary>
    public async Task<MyDayDto> MyDayAsync(CancellationToken cancellationToken = default)
    {
        var me = tenant.UserId ?? throw new ForbiddenException("Sign in first.", "no_user");
        var (todayStart, todayEnd) = await TodayAsync(cancellationToken);
        var open = (await ScopedAsync(cancellationToken)).Where(a => a.OwnerId == me && a.Status == "Open" && a.DueAt != null);
        var overdue = await open.CountAsync(a => a.DueAt < todayStart, cancellationToken);
        var today = await open.CountAsync(a => a.DueAt >= todayStart && a.DueAt < todayEnd, cancellationToken);
        var upcoming = await open.CountAsync(a => a.DueAt >= todayEnd, cancellationToken);
        var items = await open.Where(a => a.DueAt < todayEnd).OrderBy(a => a.DueAt).Take(50).ToListAsync(cancellationToken);
        return new MyDayDto(overdue, today, upcoming, await MapAsync(items, cancellationToken));
    }

    /// <summary>Progress of the onboarding checklists of a won deal: the tasks the templates created and how many are done.</summary>
    public async Task<IReadOnlyList<OnboardingProgressDto>> OnboardingAsync(Guid dealId, CancellationToken cancellationToken = default)
    {
        var tasks = await db.Activities.AsNoTracking().Where(a => a.DealId == dealId && a.ChecklistTemplateId != null).OrderBy(a => a.DueAt).ToListAsync(cancellationToken);
        var ids = tasks.Select(t => t.ChecklistTemplateId!.Value).Distinct().ToList();
        var names = await db.ChecklistTemplates.AsNoTracking().IgnoreQueryFilters().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var dtos = (await MapAsync(tasks, cancellationToken)).ToDictionary(d => d.Id);
        return ids.Select(id =>
        {
            var group = tasks.Where(t => t.ChecklistTemplateId == id).ToList();
            return new OnboardingProgressDto(id, names.GetValueOrDefault(id) ?? "Checklist", group.Count, group.Count(t => t.Status == "Completed"), group.Select(t => dtos[t.Id]).ToList());
        }).ToList();
    }

    // ---------- writing ----------

    public async Task<ActivityDto> CreateAsync(SaveActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = new Activity { OwnerId = tenant.UserId ?? throw new ForbiddenException("Sign in to create records.", "no_user") };
        await ApplyAsync(activity, request, isNew: true, cancellationToken);
        activity = await savePipeline.ExecuteCreateAsync("activities", activity, async () =>
        {
            db.Activities.Add(activity);
            await db.SaveChangesAsync(cancellationToken);
            return activity;
        }, cancellationToken);
        await SaveMentionsAsync(activity, request, cancellationToken);
        await NotifyTaskAssignedAsync(activity, cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
        await SendInvitationsAsync(activity, request.SendInvitations, cancelled: false, cancellationToken);
        return await GetAsync(activity.Id, cancellationToken);
    }

    public async Task<ActivityDto> UpdateAsync(Guid id, SaveActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await LoadScopedAsync(id, cancellationToken);
        var previousDeal = activity.DealId;
        var previousOwner = activity.OwnerId;
        var previousDue = activity.DueAt;
        var previousReminder = activity.ReminderMinutes;
        var previousSnapshot = new Activity
        {
            Id = activity.Id,
            Type = activity.Type,
            Subject = activity.Subject,
            Status = activity.Status,
            Priority = activity.Priority,
            DueAt = activity.DueAt,
            OwnerId = activity.OwnerId,
            CustomFields = activity.CustomFields
        };

        await ApplyAsync(activity, request, isNew: false, cancellationToken);
        if (activity.DueAt != previousDue || activity.ReminderMinutes != previousReminder)
        {
            // A new time means a new reminder and a new overdue warning.
            activity.ReminderSentAt = null;
            activity.OverdueNotifiedAt = null;
        }

        activity = await savePipeline.ExecuteUpdateAsync("activities", activity, previousSnapshot, async () =>
        {
            await db.SaveChangesAsync(cancellationToken);
            return activity;
        }, cancellationToken);

        await SaveMentionsAsync(activity, request, cancellationToken);
        if (activity.OwnerId != previousOwner)
        {
            await NotifyTaskAssignedAsync(activity, cancellationToken);
        }

        await SendInvitationsAsync(activity, request.SendInvitations, cancelled: false, cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
        if (previousDeal != activity.DealId)
        {
            await RefreshDealTimingAsync(previousDeal, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Someone else's task: they are told when it is created for them or handed to them.</summary>
    private Task NotifyTaskAssignedAsync(Activity activity, CancellationToken cancellationToken)
        => activity.Type != ActivityTypes.Task
            ? Task.CompletedTask
            : notifications.NotifyAsync(new NotificationRequest(
                [activity.OwnerId], NotificationTypes.TaskAssigned, $"Task assigned to you: {activity.Subject}",
                activity.DueAt is { } due ? $"Due {due:d MMM yyyy HH:mm} UTC." : null, "activities", activity.Id, ActivityLink(activity)), cancellationToken);

    private static string ActivityLink(Activity a)
        => a.DealId is { } d ? $"/deals/{d}" : a.ContactId is { } c ? $"/contacts/{c}" : a.AccountId is { } ac ? $"/accounts/{ac}" : a.LeadId is { } l ? $"/leads/{l}" : "/activities";

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await LoadScopedAsync(id, cancellationToken);
        await SendInvitationsAsync(activity, false, cancelled: true, cancellationToken);
        db.Activities.Remove(activity);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
    }

    /// <summary>Completes a task, call or meeting. A call can record its outcome, and a follow-up task can be created from it.</summary>
    public async Task<ActivityDto> CompleteAsync(Guid id, CompleteActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await LoadScopedAsync(id, cancellationToken);
        if (activity.Status == "Completed")
        {
            throw new BusinessRuleException("already_completed", "This activity is already completed.");
        }

        if (!string.IsNullOrWhiteSpace(request.Outcome))
        {
            if (activity.Type != ActivityTypes.Call)
            {
                throw RecordHelpers.Invalid("outcome", "Only calls have an outcome.");
            }

            await ValidateOutcomeAsync(request.Outcome, cancellationToken);
            activity.CallOutcome = request.Outcome;
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            activity.Description = string.IsNullOrWhiteSpace(activity.Description) ? request.Notes.Trim() : activity.Description + "\n\n" + request.Notes.Trim();
        }

        activity.Status = "Completed";
        activity.CompletedAt = clock.UtcNow;
        activity.CompletedById = tenant.UserId;

        Activity? followUp = null;
        if (request.FollowUp is { } follow)
        {
            // The follow-up inherits the links of the task it comes from unless the request names others.
            var merged = follow with
            {
                Type = ActivityTypes.Task,
                LeadId = follow.LeadId ?? activity.LeadId,
                ContactId = follow.ContactId ?? activity.ContactId,
                AccountId = follow.AccountId ?? activity.AccountId,
                DealId = follow.DealId ?? activity.DealId,
            };
            followUp = new Activity { OwnerId = tenant.UserId!.Value, ParentActivityId = activity.Id };
            await ApplyAsync(followUp, merged, isNew: true, cancellationToken);
            db.Activities.Add(followUp);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Moves a task or meeting to a new time (dragging it on the calendar). A booking's customer is told of the new time.</summary>
    public async Task<ActivityDto> ScheduleAsync(Guid id, ScheduleActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await LoadScopedAsync(id, cancellationToken);
        if (activity.Type is not (ActivityTypes.Task or ActivityTypes.Meeting))
        {
            throw RecordHelpers.Invalid("type", "Only tasks and meetings have a time you can move.");
        }

        if (activity.Status != "Open")
        {
            throw new BusinessRuleException("not_open", "Only open items can be moved.");
        }

        var start = DateTime.SpecifyKind(request.StartAt, DateTimeKind.Utc);
        var length = activity.EndAt is { } oldEnd && activity.DueAt is { } oldStart ? oldEnd - oldStart : TimeSpan.FromHours(1);
        if (request.EndAt is { } requestedEnd && requestedEnd <= request.StartAt)
        {
            throw RecordHelpers.Invalid("endAt", "The end must be after the start.");
        }

        activity.DueAt = start;
        if (activity.Type == ActivityTypes.Meeting)
        {
            activity.EndAt = request.EndAt is { } end ? DateTime.SpecifyKind(end, DateTimeKind.Utc) : start + length;
        }

        activity.ReminderSentAt = null;
        activity.OverdueNotifiedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.ActivityId == id && b.Status == "Confirmed", cancellationToken);
        if (booking is not null && activity.EndAt is { } bookingEnd)
        {
            booking.StartAt = start;
            booking.EndAt = bookingEnd;
            await db.SaveChangesAsync(cancellationToken);
            await TellBookerAsync(booking, activity, cancellationToken);
        }

        await SendInvitationsAsync(activity, false, cancelled: false, cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private async Task TellBookerAsync(Booking booking, Activity activity, CancellationToken cancellationToken)
    {
        try
        {
            var host = await db.Users.AsNoTracking().Where(u => u.Id == activity.OwnerId).Select(u => new { Name = (u.FirstName + " " + u.LastName).Trim(), u.Email }).FirstAsync(cancellationToken);
            var zone = MindLift.Application.Common.Time.TimeZoneRules.Exists(booking.TimeZone) ? TimeZoneInfo.FindSystemTimeZoneById(booking.TimeZone) : TimeZoneInfo.Utc;
            var local = TimeZoneInfo.ConvertTimeFromUtc(booking.StartAt, zone).ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var meeting = new IcsBuilder.Meeting(activity.Id, activity.Subject, null, activity.Location, booking.StartAt, booking.EndAt, host.Name, host.Email ?? "no-reply@mindlift.local", [booking.Email], activity.Version);
            await email.SendAsync(booking.Email, $"Booking moved: {activity.Subject}",
                $"Hello {booking.Name},\n\n{host.Name} moved your booking to a new time.\n\n{activity.Subject}\n{local} ({zone.Id})\n\nThe updated calendar file is attached.",
                [new EmailAttachment("booking.ics", "text/calendar; method=REQUEST; charset=UTF-8", Encoding.UTF8.GetBytes(IcsBuilder.Build(meeting)))], cancellationToken);
        }
        catch (Exception)
        {
            // The meeting has moved either way; a mail problem must not undo it.
        }
    }

    public async Task<ActivityDto> ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await LoadScopedAsync(id, cancellationToken);
        if (activity.Type == ActivityTypes.Note)
        {
            throw new BusinessRuleException("note_cannot_reopen", "A note is not something to reopen.");
        }

        activity.Status = "Open";
        activity.CompletedAt = null;
        activity.CompletedById = null;
        await db.SaveChangesAsync(cancellationToken);
        await RefreshDealTimingAsync(activity.DealId, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    // ---------- rules ----------

    private async Task ApplyAsync(Activity activity, SaveActivityRequest request, bool isNew, CancellationToken cancellationToken)
    {
        if (!ActivityTypes.All.Contains(request.Type))
        {
            throw RecordHelpers.Invalid("type", "Choose task, call, meeting or note.");
        }

        if (!isNew && request.Type != activity.Type)
        {
            throw RecordHelpers.Invalid("type", "The type of an activity cannot be changed.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw RecordHelpers.Invalid("subject", "Subject is required.");
        }

        var priority = request.Priority ?? (isNew ? "Normal" : activity.Priority);
        if (!Priorities.Contains(priority))
        {
            throw RecordHelpers.Invalid("priority", "Choose low, normal or high.");
        }

        if (request.Direction is not null && !Directions.Contains(request.Direction))
        {
            throw RecordHelpers.Invalid("direction", "Choose inbound or outbound.");
        }

        if (request.Direction is not null && request.Type != ActivityTypes.Call)
        {
            throw RecordHelpers.Invalid("direction", "Only calls have a direction.");
        }

        if (request.CallOutcome is not null)
        {
            if (request.Type != ActivityTypes.Call)
            {
                throw RecordHelpers.Invalid("callOutcome", "Only calls have an outcome.");
            }

            await ValidateOutcomeAsync(request.CallOutcome, cancellationToken);
        }

        if (request.DurationMinutes is < 0 or > 24 * 60)
        {
            throw RecordHelpers.Invalid("durationMinutes", "Enter a duration between 0 and 1,440 minutes.");
        }

        if (request.ReminderMinutes is < 0 or > 60 * 24 * 30)
        {
            throw RecordHelpers.Invalid("reminderMinutes", "Enter a reminder of up to 30 days.");
        }

        if (request.EndAt is { } end && request.DueAt is { } start && end < start)
        {
            throw RecordHelpers.Invalid("endAt", "The end cannot be before the start.");
        }

        // Linking: a deal brings its company and main contact, a contact brings its company.
        var leadId = request.LeadId;
        var contactId = request.ContactId;
        var accountId = request.AccountId;
        var dealId = request.DealId;
        await EnsureLinkInScopeAsync(request.LeadId, db.Leads.AsNoTracking(), AppModule.Leads, x => x.OwnerId, x => x.Id, "leadId", cancellationToken);
        await EnsureLinkInScopeAsync(request.ContactId, db.Contacts.AsNoTracking(), AppModule.Contacts, x => x.OwnerId, x => x.Id, "contactId", cancellationToken);
        await EnsureLinkInScopeAsync(request.AccountId, db.Accounts.AsNoTracking(), AppModule.Accounts, x => x.OwnerId, x => x.Id, "accountId", cancellationToken);
        await EnsureLinkInScopeAsync(request.DealId, db.Deals.AsNoTracking(), AppModule.Deals, x => x.OwnerId, x => x.Id, "dealId", cancellationToken);
        if (leadId is { } l && !await db.Leads.AnyAsync(x => x.Id == l, cancellationToken)) throw RecordHelpers.Invalid("leadId", "That lead no longer exists.");
        if (dealId is { } d)
        {
            var deal = await db.Deals.AsNoTracking().Where(x => x.Id == d).Select(x => new { x.AccountId, x.PrimaryContactId }).FirstOrDefaultAsync(cancellationToken)
                ?? throw RecordHelpers.Invalid("dealId", "That deal no longer exists.");
            if (accountId is null && deal.AccountId is { } derivedAcc)
            {
                var scopedAccounts = await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, x => x.OwnerId, cancellationToken);
                if (await scopedAccounts.AnyAsync(x => x.Id == derivedAcc, cancellationToken))
                {
                    accountId = derivedAcc;
                }
            }
            if (contactId is null && deal.PrimaryContactId is { } derivedContact)
            {
                var scopedContacts = await RecordHelpers.InScopeAsync(db.Contacts.AsNoTracking(), scope, AppModule.Contacts, x => x.OwnerId, cancellationToken);
                if (await scopedContacts.AnyAsync(x => x.Id == derivedContact, cancellationToken))
                {
                    contactId = derivedContact;
                }
            }
        }

        if (contactId is { } c)
        {
            var contact = await db.Contacts.AsNoTracking().Where(x => x.Id == c).Select(x => new { x.AccountId }).FirstOrDefaultAsync(cancellationToken)
                ?? throw RecordHelpers.Invalid("contactId", "That contact no longer exists.");
            if (accountId is null && contact.AccountId is { } derivedAcc)
            {
                var scopedAccounts = await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, x => x.OwnerId, cancellationToken);
                if (await scopedAccounts.AnyAsync(x => x.Id == derivedAcc, cancellationToken))
                {
                    accountId = derivedAcc;
                }
            }
        }

        if (accountId is { } a && !await db.Accounts.AnyAsync(x => x.Id == a, cancellationToken)) throw RecordHelpers.Invalid("accountId", "That account no longer exists.");

        if (request.AttendeeEmails is { Count: > 0 } && request.Type != ActivityTypes.Meeting)
        {
            throw RecordHelpers.Invalid("attendeeEmails", "Only meetings have attendees.");
        }

        if (request.MentionUserIds is { Count: > 0 } && request.Type != ActivityTypes.Note)
        {
            throw RecordHelpers.Invalid("mentionUserIds", "Only notes can mention people.");
        }

        if (request.SendInvitations && request.Type == ActivityTypes.Meeting && request.DueAt is null)
        {
            throw RecordHelpers.Invalid("dueAt", "Choose when the meeting starts before sending invitations.");
        }

        var attendees = request.AttendeeEmails is null ? null : NormalizeEmails(request.AttendeeEmails);
        if (request.SendInvitations && request.Type == ActivityTypes.Meeting && (attendees ?? ParseAttendees(activity.Attendees)).Count == 0)
        {
            throw RecordHelpers.Invalid("attendeeEmails", "Add at least one email address to send invitations to.");
        }

        if (request.MentionUserIds is { Count: > 25 })
        {
            throw RecordHelpers.Invalid("mentionUserIds", "Mention at most 25 people in one note.");
        }

        if (request.MentionUserIds is { Count: > 0 } mentioned)
        {
            var ids = mentioned.Distinct().ToList();
            if (await db.Users.CountAsync(u => ids.Contains(u.Id) && u.IsActive, cancellationToken) != ids.Count)
            {
                throw RecordHelpers.Invalid("mentionUserIds", "One of the people you mentioned is not an active user.");
            }
        }

        if (attendees is not null)
        {
            activity.Attendees = JsonSerializer.Serialize(attendees);
        }

        activity.Type = request.Type;
        activity.Subject = request.Subject.Trim();
        activity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        activity.Priority = priority;
        activity.Direction = request.Direction ?? (request.Type == ActivityTypes.Call ? activity.Direction ?? "Outbound" : null);
        activity.CallOutcome = request.CallOutcome ?? activity.CallOutcome;
        activity.Location = request.Type == ActivityTypes.Meeting ? RecordHelpers.Clean(request.Location) : null;
        activity.DurationMinutes = request.DurationMinutes;
        activity.EndAt = request.EndAt is { } endAt ? DateTime.SpecifyKind(endAt, DateTimeKind.Utc) : null;
        activity.ReminderMinutes = request.ReminderMinutes;
        activity.LeadId = leadId;
        activity.ContactId = contactId;
        activity.AccountId = accountId;
        activity.DealId = dealId;
        if (request.OwnerId is { } owner)
        {
            await RecordHelpers.EnsureOwnerAsync(db, scope, AppModule.Activities, owner, cancellationToken);
            activity.OwnerId = owner;
        }

        var due = request.DueAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : (DateTime?)null;
        if (isNew)
        {
            // Notes and calls logged for now or the past already happened; tasks, meetings and calls planned for later are still to do.
            var done = request.Type == ActivityTypes.Note || (request.Type == ActivityTypes.Call && (due is null || due <= clock.UtcNow));
            activity.DueAt = done ? due ?? clock.UtcNow : due;
            if (done)
            {
                activity.Status = "Completed";
                activity.CompletedAt = clock.UtcNow;
                activity.CompletedById = tenant.UserId;
            }
            else
            {
                activity.Status = "Open";
            }
        }
        else
        {
            activity.DueAt = due;
        }
    }

    private static List<string> NormalizeEmails(IReadOnlyList<string> input)
    {
        var result = new List<string>();
        foreach (var raw in input)
        {
            var text = raw.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(text, out var address) || address.Address != text)
            {
                throw RecordHelpers.Invalid("attendeeEmails", $"{text} is not a valid email address.");
            }

            if (!result.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(text.ToLowerInvariant());
            }
        }

        if (result.Count > 50)
        {
            throw RecordHelpers.Invalid("attendeeEmails", "A meeting can have at most 50 attendees.");
        }

        return result;
    }

    private static List<string> ParseAttendees(string json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    /// <summary>Replaces the people mentioned in a note. Null leaves them as they are.</summary>
    private async Task SaveMentionsAsync(Activity activity, SaveActivityRequest request, CancellationToken cancellationToken)
    {
        if (request.MentionUserIds is null || activity.Type != ActivityTypes.Note)
        {
            return;
        }

        var wanted = request.MentionUserIds.Distinct().ToList();
        var existing = await db.ActivityMentions.Where(m => m.ActivityId == activity.Id).ToListAsync(cancellationToken);
        db.ActivityMentions.RemoveRange(existing.Where(m => !wanted.Contains(m.UserId)));
        var added = wanted.Where(w => existing.All(m => m.UserId != w)).ToList();
        foreach (var userId in added)
        {
            db.ActivityMentions.Add(new ActivityMention { ActivityId = activity.Id, UserId = userId });
        }

        await db.SaveChangesAsync(cancellationToken);

        // Only people newly mentioned are told, so editing a note does not repeat the message.
        if (added.Count > 0)
        {
            var author = tenant.UserId is { } me ? await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefaultAsync(cancellationToken) : null;
            await notifications.NotifyAsync(new NotificationRequest(
                added, NotificationTypes.Mentioned, $"{author ?? "Someone"} mentioned you in a note: {activity.Subject}", activity.Description is { Length: > 0 } text ? (text.Length > 200 ? text[..200] + "…" : text) : null,
                "activities", activity.Id, ActivityLink(activity)), cancellationToken);
        }
    }

    /// <summary>
    /// Emails a calendar invitation (.ics) to every external attendee. An edit of a meeting whose invitations went out is sent as an update,
    /// and deleting it sends a cancellation. Each attendee gets their own message.
    /// </summary>
    private async Task SendInvitationsAsync(Activity activity, bool requested, bool cancelled, CancellationToken cancellationToken)
    {
        if (activity.Type != ActivityTypes.Meeting || activity.DueAt is not { } start)
        {
            return;
        }

        var attendees = ParseAttendees(activity.Attendees);
        var alreadySent = activity.InvitationsSentAt is not null;
        if (attendees.Count == 0 || !(requested || alreadySent) || (cancelled && !alreadySent))
        {
            return;
        }

        var organizer = await db.Users.AsNoTracking().Where(u => u.Id == (tenant.UserId ?? activity.OwnerId))
            .Select(u => new { Name = (u.FirstName + " " + u.LastName).Trim(), u.Email }).FirstAsync(cancellationToken);
        var meeting = new IcsBuilder.Meeting(activity.Id, activity.Subject, activity.Description, activity.Location, start,
            activity.EndAt ?? start.AddHours(1), organizer.Name, organizer.Email ?? "no-reply@mindlift.local", attendees, activity.Version);
        var ics = Encoding.UTF8.GetBytes(IcsBuilder.Build(meeting, cancelled));

        var verb = cancelled ? "Cancelled" : alreadySent ? "Updated" : "Invitation";
        var when = start.ToString("dddd d MMMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC";
        foreach (var to in attendees)
        {
            await email.SendAsync(
                to,
                $"{verb}: {activity.Subject}",
                $"{organizer.Name} {(cancelled ? "cancelled" : alreadySent ? "changed" : "invited you to")} a meeting.\n\n{activity.Subject}\n{when}{(string.IsNullOrWhiteSpace(activity.Location) ? "" : "\nWhere: " + activity.Location)}\n\nThe calendar file is attached.",
                [new EmailAttachment("invite.ics", $"text/calendar; method={(cancelled ? "CANCEL" : "REQUEST")}; charset=UTF-8", ics)],
                cancellationToken);
        }

        if (!cancelled)
        {
            activity.InvitationsSentAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ValidateOutcomeAsync(string outcome, CancellationToken cancellationToken)
    {
        if (!await db.PicklistValues.AnyAsync(p => p.PicklistKey == "call_outcome" && p.Value == outcome && p.IsActive, cancellationToken))
        {
            throw RecordHelpers.Invalid("callOutcome", "Choose a valid outcome.");
        }
    }

    /// <summary>Keeps the last and next activity dates of a deal in step, so rotting and "next step" stay right.</summary>
    private async Task RefreshDealTimingAsync(Guid? dealId, CancellationToken cancellationToken)
    {
        if (dealId is not { } id)
        {
            return;
        }

        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return;
        }

        var now = clock.UtcNow;
        deal.LastActivityAt = await db.Activities.Where(a => a.DealId == id && a.Status == "Completed" && a.ChecklistTemplateId == null)
            .Select(a => a.CompletedAt).MaxAsync(cancellationToken);
        deal.NextActivityAt = await db.Activities.Where(a => a.DealId == id && a.Status == "Open" && a.DueAt != null && a.DueAt >= now)
            .Select(a => a.DueAt).MinAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---------- helpers ----------

    private async Task<(DateTime Start, DateTime End)> TodayAsync(CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow, zone).Date;
        return (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone),
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.AddDays(1), DateTimeKind.Unspecified), zone));
    }

    private async Task<IReadOnlyList<ActivityDto>> MapAsync(IReadOnlyList<Activity> rows, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (todayStart, _) = await TodayAsync(cancellationToken);
        var owners = await RecordHelpers.OwnerNamesAsync(db, rows.Select(r => r.OwnerId).Distinct(), cancellationToken);

        var dealIds = rows.Where(r => r.DealId != null).Select(r => r.DealId!.Value).Distinct().ToList();
        var contactIds = rows.Where(r => r.ContactId != null).Select(r => r.ContactId!.Value).Distinct().ToList();
        var accountIds = rows.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var leadIds = rows.Where(r => r.LeadId != null).Select(r => r.LeadId!.Value).Distinct().ToList();
        var visibleDeals = await RecordHelpers.InScopeAsync(db.Deals.AsNoTracking(), scope, AppModule.Deals, x => x.OwnerId, cancellationToken);
        var visibleContacts = await RecordHelpers.InScopeAsync(db.Contacts.AsNoTracking(), scope, AppModule.Contacts, x => x.OwnerId, cancellationToken);
        var visibleAccounts = await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, x => x.OwnerId, cancellationToken);
        var visibleLeads = await RecordHelpers.InScopeAsync(db.Leads.AsNoTracking(), scope, AppModule.Leads, x => x.OwnerId, cancellationToken);

        var deals = await visibleDeals.Where(x => dealIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var contacts = await visibleContacts.Where(x => contactIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => (x.FirstName + " " + x.LastName).Trim(), cancellationToken);
        var accounts = await visibleAccounts.Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var leads = await visibleLeads.Where(x => leadIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => (x.FirstName + " " + x.LastName).Trim(), cancellationToken);
        var activityIds = rows.Select(r => r.Id).ToList();
        var mentionRows = await db.ActivityMentions.AsNoTracking().Where(m => activityIds.Contains(m.ActivityId)).ToListAsync(cancellationToken);
        var mentionNames = await RecordHelpers.OwnerNamesAsync(db, mentionRows.Select(m => m.UserId).Distinct(), cancellationToken);

        return rows.Select(a =>
        {
            string? name = null, kind = null;
            if (a.DealId is { } d && deals.TryGetValue(d, out var dn)) { name = dn; kind = "deal"; }
            else if (a.ContactId is { } c && contacts.TryGetValue(c, out var cn)) { name = cn; kind = "contact"; }
            else if (a.AccountId is { } ac && accounts.TryGetValue(ac, out var an)) { name = an; kind = "account"; }
            else if (a.LeadId is { } l && leads.TryGetValue(l, out var ln)) { name = ln; kind = "lead"; }

            return new ActivityDto(a.Id, a.Type, a.Subject, a.Description, a.Status, a.Priority, a.DueAt, a.EndAt, a.DurationMinutes, a.Direction, a.CallOutcome, a.Location,
                a.ReminderMinutes, a.CompletedAt, a.LeadId, a.ContactId, a.AccountId, a.DealId, name, kind, a.OwnerId, owners.GetValueOrDefault(a.OwnerId),
                a.ParentActivityId, a.ChecklistTemplateId, a.Status == "Open" && a.DueAt != null && a.DueAt < todayStart, a.CreatedAt,
                ParseAttendees(a.Attendees),
                mentionRows.Where(m => m.ActivityId == a.Id).Select(m => new MentionDto(m.UserId, mentionNames.GetValueOrDefault(m.UserId) ?? "—")).ToList(),
                a.InvitationsSentAt);
        }).ToList();
    }

    private async Task<IQueryable<Activity>> ScopedAsync(CancellationToken cancellationToken)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Activities, cancellationToken);
        var query = db.Activities.AsQueryable();
        return ids is null ? query : query.Where(a => ids.Contains(a.OwnerId));
    }

    private async Task<Activity> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedAsync(cancellationToken)).FirstOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new NotFoundException("Activity", id);

    /// <summary>A record linked by the caller must be one they can see.</summary>
    private async Task EnsureLinkInScopeAsync<T>(Guid? requested, IQueryable<T> source, AppModule module,
        System.Linq.Expressions.Expression<Func<T, Guid>> owner, System.Linq.Expressions.Expression<Func<T, Guid>> key, string field, CancellationToken cancellationToken)
    {
        if (requested is not { } wanted)
        {
            return;
        }

        var visible = await RecordHelpers.InScopeAsync(source, scope, module, owner, cancellationToken);
        var match = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(
            System.Linq.Expressions.Expression.Equal(key.Body, System.Linq.Expressions.Expression.Constant(wanted)), key.Parameters);
        if (!await visible.AnyAsync(match, cancellationToken))
        {
            throw RecordHelpers.Invalid(field, "That record no longer exists.");
        }
    }
}
