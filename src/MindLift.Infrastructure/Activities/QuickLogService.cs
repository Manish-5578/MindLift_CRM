using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Activities;

/// <summary>
/// Service for the 1-Click Quick-Log Dock (quick-log-dock).
/// Allows logging calls, notes, tasks, and meetings against active records (Lead, Contact, Account, Deal)
/// with automated linking, completion, follow-up scheduling, and timeline event generation.
/// </summary>
public sealed class QuickLogService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    ActivityService activities,
    ITimelineEventBus timeline)
{
    public async Task<QuickLogResult> LogAsync(QuickLogRequest request, CancellationToken cancellationToken = default)
    {
        var targetType = request.TargetType?.Trim().ToLowerInvariant();
        if (targetType is not ("lead" or "contact" or "account" or "deal"))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["targetType"] = ["Target type must be lead, contact, account, or deal."]
            });
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["subject"] = ["Subject is required."]
            });
        }

        Guid? leadId = null;
        Guid? contactId = null;
        Guid? accountId = null;
        Guid? dealId = null;

        switch (targetType)
        {
            case "lead":
                if (!await db.Leads.AnyAsync(l => l.Id == request.TargetId, cancellationToken))
                    throw new NotFoundException("Lead", request.TargetId);
                leadId = request.TargetId;
                break;

            case "contact":
                var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == request.TargetId, cancellationToken)
                    ?? throw new NotFoundException("Contact", request.TargetId);
                contactId = contact.Id;
                accountId = contact.AccountId;
                break;

            case "account":
                if (!await db.Accounts.AnyAsync(a => a.Id == request.TargetId, cancellationToken))
                    throw new NotFoundException("Account", request.TargetId);
                accountId = request.TargetId;
                break;

            case "deal":
                var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == request.TargetId, cancellationToken)
                    ?? throw new NotFoundException("Deal", request.TargetId);
                dealId = deal.Id;
                accountId = deal.AccountId;
                contactId = deal.PrimaryContactId;
                break;
        }

        var activityType = request.ActivityType?.Trim() ?? ActivityTypes.Note;
        var isCall = string.Equals(activityType, ActivityTypes.Call, StringComparison.OrdinalIgnoreCase);
        var isNote = string.Equals(activityType, ActivityTypes.Note, StringComparison.OrdinalIgnoreCase);
        var isTask = string.Equals(activityType, ActivityTypes.Task, StringComparison.OrdinalIgnoreCase);
        var isMeeting = string.Equals(activityType, ActivityTypes.Meeting, StringComparison.OrdinalIgnoreCase);

        var saveReq = new SaveActivityRequest(
            Type: activityType,
            Subject: request.Subject.Trim(),
            Description: request.Notes?.Trim(),
            Priority: request.Priority ?? "Normal",
            DueAt: request.DueAt ?? (isMeeting ? clock.UtcNow : isTask ? clock.UtcNow.AddDays(1) : null),
            EndAt: request.EndAt,
            DurationMinutes: request.DurationMinutes,
            Direction: isCall ? (request.Direction ?? "Outbound") : null,
            CallOutcome: isCall ? request.CallOutcome : null,
            Location: request.Location,
            ReminderMinutes: null,
            LeadId: leadId,
            ContactId: contactId,
            AccountId: accountId,
            DealId: dealId,
            OwnerId: tenant.UserId,
            AttendeeEmails: request.AttendeeEmails,
            SendInvitations: false,
            MentionUserIds: null
        );

        var created = await activities.CreateAsync(saveReq, cancellationToken);

        if ((isCall || isNote || request.MarkCompleted) && created.Status != "Completed")
        {
            created = await activities.CompleteAsync(created.Id, new CompleteActivityRequest(isCall ? request.CallOutcome : null, null, null), cancellationToken);
        }

        ActivityDto? followUp = null;
        if (!string.IsNullOrWhiteSpace(request.FollowUpSubject))
        {
            var followUpReq = new SaveActivityRequest(
                Type: ActivityTypes.Task,
                Subject: request.FollowUpSubject.Trim(),
                Description: null,
                Priority: "Normal",
                DueAt: request.FollowUpDueAt ?? clock.UtcNow.AddDays(1),
                EndAt: null,
                DurationMinutes: null,
                Direction: null,
                CallOutcome: null,
                Location: null,
                ReminderMinutes: null,
                LeadId: leadId,
                ContactId: contactId,
                AccountId: accountId,
                DealId: dealId,
                OwnerId: tenant.UserId,
                AttendeeEmails: null,
                SendInvitations: false,
                MentionUserIds: null
            );
            followUp = await activities.CreateAsync(followUpReq, cancellationToken);
        }

        var timelineKind = isCall ? "call_logged" : isNote ? "note_added" : isTask ? "task_created" : "meeting_scheduled";
        await timeline.PublishAsync(new TimelineEventInput(
            EntityType: targetType,
            EntityId: request.TargetId,
            Kind: timelineKind,
            Summary: $"Quick logged {activityType.ToLowerInvariant()}: {request.Subject.Trim()}",
            Source: "crm",
            DetailJson: JsonSerializer.Serialize(new
            {
                ActivityId = created.Id,
                ActivityType = activityType,
                Subject = created.Subject,
                DurationMinutes = request.DurationMinutes,
                CallOutcome = request.CallOutcome,
                FollowUpId = followUp?.Id
            }),
            ActorUserId: tenant.UserId,
            OccurredAt: clock.UtcNow
        ), cancellationToken);

        return new QuickLogResult(created, followUp);
    }

    public Task<QuickLogResult> LogCallAsync(QuickLogCallRequest request, CancellationToken cancellationToken = default)
        => LogAsync(new QuickLogRequest(
            TargetType: request.TargetType,
            TargetId: request.TargetId,
            ActivityType: ActivityTypes.Call,
            Subject: request.Subject,
            Notes: request.Notes,
            Direction: request.Direction,
            CallOutcome: request.CallOutcome,
            DurationMinutes: request.DurationMinutes,
            MarkCompleted: true,
            FollowUpSubject: request.FollowUpSubject,
            FollowUpDueAt: request.FollowUpDueAt
        ), cancellationToken);

    public Task<QuickLogResult> LogNoteAsync(QuickLogNoteRequest request, CancellationToken cancellationToken = default)
        => LogAsync(new QuickLogRequest(
            TargetType: request.TargetType,
            TargetId: request.TargetId,
            ActivityType: ActivityTypes.Note,
            Subject: request.Subject,
            Notes: request.Notes,
            MarkCompleted: true
        ), cancellationToken);

    public Task<QuickLogResult> LogTaskAsync(QuickLogTaskRequest request, CancellationToken cancellationToken = default)
        => LogAsync(new QuickLogRequest(
            TargetType: request.TargetType,
            TargetId: request.TargetId,
            ActivityType: ActivityTypes.Task,
            Subject: request.Subject,
            Notes: request.Notes,
            DueAt: request.DueAt,
            Priority: request.Priority
        ), cancellationToken);

    public Task<QuickLogResult> LogMeetingAsync(QuickLogMeetingRequest request, CancellationToken cancellationToken = default)
        => LogAsync(new QuickLogRequest(
            TargetType: request.TargetType,
            TargetId: request.TargetId,
            ActivityType: ActivityTypes.Meeting,
            Subject: request.Subject,
            Notes: request.Notes,
            DueAt: request.StartAt,
            EndAt: request.EndAt,
            Location: request.Location,
            AttendeeEmails: request.AttendeeEmails
        ), cancellationToken);
}
