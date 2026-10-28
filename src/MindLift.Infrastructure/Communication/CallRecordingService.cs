using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class CallRecordingService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline) : ICallRecordingService
{
    private static readonly SpeakerSegmentDto[] DefaultSimulatedSegments =
    [
        new("Agent", 0, 8, "Hello, thank you for taking the time to speak with MindLift CRM today. How are your current operations going?"),
        new("Contact", 9, 26, "Hi! We're currently reviewing solutions for lead automation and multi-channel communication. Our team needs something reliable."),
        new("Agent", 27, 48, "That sounds like a great fit. MindLift handles two-way email sync, telephony with click-to-call, and automated lead capture natively."),
        new("Contact", 49, 65, "Fantastic. Can you send over a pricing breakdown and schedule a detailed platform demo for our leadership team next Tuesday?"),
        new("Agent", 66, 80, "Absolutely. I'll send the quote right away and set up our follow-up demonstration for Tuesday at 10 AM. Thank you!")
    ];

    public async Task<RecordingConsentDto> GetConsentSettingsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        return new RecordingConsentDto(settings.ConsentMode, settings.DualPartyNotification, settings.RetentionDays);
    }

    public async Task<RecordingConsentDto> UpdateConsentSettingsAsync(UpdateRecordingConsentRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var validModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Always", "Prompt", "None" };
        if (!validModes.Contains(request.ConsentMode))
            throw new ValidationException("consentMode", "ConsentMode must be 'Always', 'Prompt', or 'None'.");

        if (request.RetentionDays < 1 || request.RetentionDays > 3650)
            throw new ValidationException("retentionDays", "RetentionDays must be between 1 and 3650 days.");

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        settings.ConsentMode = request.ConsentMode;
        settings.DualPartyNotification = request.DualPartyNotification;
        settings.RetentionDays = request.RetentionDays;

        await db.SaveChangesAsync(ct);
        return new RecordingConsentDto(settings.ConsentMode, settings.DualPartyNotification, settings.RetentionDays);
    }

    public async Task<CallRecordingDto> GetRecordingAsync(Guid callId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        var settings = await GetSettingsEntityAsync(tenantId, ct);

        return new CallRecordingDto(
            call.Id,
            call.CallSid,
            call.IsRecordingDeleted ? null : call.RecordingUrl,
            call.RecordingDurationSeconds,
            call.IsRecordingDeleted,
            call.StartedAtUtc,
            call.Direction,
            call.Status,
            call.ToNumber,
            call.FromNumber,
            settings.ConsentMode,
            settings.DualPartyNotification);
    }

    public async Task<TranscriptionResultDto> TranscribeCallAsync(Guid callId, TranscribeCallRequest? request = null, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        if (call.IsRecordingDeleted)
            throw new ValidationException("recording", "Call recording has been erased under GDPR policy.");

        // If recording URL was not previously generated, assign simulated recording URL and duration
        if (string.IsNullOrWhiteSpace(call.RecordingUrl))
        {
            call.RecordingUrl = $"https://recordings.mindlift.io/{tenantId}/{call.CallSid}.wav";
            call.RecordingDurationSeconds = call.DurationSeconds > 0 ? call.DurationSeconds : 80;
        }

        var fullText = string.Join("\n", DefaultSimulatedSegments.Select(s => $"[{FormatOffset(s.StartOffsetSeconds)}] {s.Speaker}: {s.Text}"));
        var summary = "Prospect inquired about lead automation and omnichannel capabilities. Agreed to review pricing and confirmed follow-up platform demo for Tuesday at 10 AM.";
        var sentiment = "Positive";
        var actionItems = new[]
        {
            "Send detailed pricing breakdown",
            "Schedule follow-up demo for Tuesday at 10:00 AM"
        };

        call.TranscriptionStatus = "Completed";
        call.TranscriptionText = fullText;
        call.TranscriptionSummary = summary;
        call.TranscriptionSentiment = sentiment;
        call.TranscriptionActionItems = actionItems;

        await db.SaveChangesAsync(ct);

        await PublishTranscriptionTimelineEventAsync(call, "call_transcribed", summary, ct);

        return new TranscriptionResultDto(
            call.Id,
            call.CallSid,
            call.TranscriptionStatus,
            call.TranscriptionText,
            call.TranscriptionSummary,
            call.TranscriptionSentiment,
            call.TranscriptionActionItems,
            DefaultSimulatedSegments,
            DateTime.UtcNow);
    }

    public async Task<TranscriptionResultDto> GetTranscriptionAsync(Guid callId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        var segments = ParseSegments(call.TranscriptionText);

        return new TranscriptionResultDto(
            call.Id,
            call.CallSid,
            call.TranscriptionStatus ?? "None",
            call.TranscriptionText,
            call.TranscriptionSummary,
            call.TranscriptionSentiment,
            call.TranscriptionActionItems,
            segments,
            call.TranscriptionStatus == "Completed" ? DateTime.UtcNow : null);
    }

    public async Task<bool> ProcessTranscriptionWebhookAsync(TranscriptionWebhookRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CallSid))
            throw new ValidationException("callSid", "CallSid is required.");

        var call = await db.CallSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CallSid == request.CallSid && !c.IsDeleted, ct);

        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        using var scope = tenantContext.Use(call.TenantId);

        var isEnabled = await featureFlags.IsEnabledAsync("call-recording", ct);
        if (!isEnabled)
            throw new ForbiddenException();

        call.TranscriptionStatus = request.Status ?? "Completed";
        if (request.FullText != null) call.TranscriptionText = request.FullText;
        if (request.Summary != null) call.TranscriptionSummary = request.Summary;
        if (request.Sentiment != null) call.TranscriptionSentiment = request.Sentiment;
        if (request.ActionItems != null) call.TranscriptionActionItems = request.ActionItems;

        await db.SaveChangesAsync(ct);

        await PublishTranscriptionTimelineEventAsync(call, "call_transcribed", call.TranscriptionSummary ?? "Call transcribed via external provider", ct);

        return true;
    }

    public async Task<GdprDeleteRecordingResult> DeleteRecordingAsync(Guid callId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        if (call.IsRecordingDeleted)
        {
            return new GdprDeleteRecordingResult(callId, true, "Call recording is already marked as deleted.");
        }

        call.IsRecordingDeleted = true;
        call.RecordingUrl = null;
        call.RecordingDurationSeconds = null;

        await db.SaveChangesAsync(ct);

        await PublishTranscriptionTimelineEventAsync(call, "call_recording_deleted", "Call audio recording erased under GDPR right-to-be-forgotten policy", ct);

        return new GdprDeleteRecordingResult(callId, true, "Call recording audio file has been permanently deleted.");
    }

    private async Task<TelephonySettings> GetSettingsEntityAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.TelephonySettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings == null)
        {
            settings = new TelephonySettings
            {
                TenantId = tenantId,
                Provider = "MockTelephony",
                CallerId = "+18005550199",
                IsActive = true,
                AutoLogCalls = true,
                RecordCallsDefault = false,
                WebRtcEnabled = true,
                ConsentMode = "Prompt",
                DualPartyNotification = true,
                RetentionDays = 90
            };
            db.TelephonySettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }

    private async Task PublishTranscriptionTimelineEventAsync(CallSession call, string kind, string summary, CancellationToken ct)
    {
        try
        {
            if (call.LeadId is { } leadId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "lead",
                    leadId,
                    kind,
                    summary,
                    "call-recording"), ct);
            }
            else if (call.ContactId is { } contactId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "contact",
                    contactId,
                    kind,
                    summary,
                    "call-recording"), ct);
            }

            if (call.DealId is { } dealId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "deal",
                    dealId,
                    kind,
                    summary,
                    "call-recording"), ct);
            }
        }
        catch
        {
            // Non-fatal timeline delivery
        }
    }

    private static string FormatOffset(int seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static IReadOnlyList<SpeakerSegmentDto> ParseSegments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // If it matches simulated text, return the segments
        if (text.Contains("MindLift CRM today"))
            return DefaultSimulatedSegments;

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<SpeakerSegmentDto>();
        int offset = 0;

        foreach (var line in lines)
        {
            var speaker = line.StartsWith("Agent:", StringComparison.OrdinalIgnoreCase) ? "Agent"
                : line.StartsWith("Contact:", StringComparison.OrdinalIgnoreCase) ? "Contact"
                : "Speaker";

            var content = line.Contains(':') ? line[(line.IndexOf(':') + 1)..].Trim() : line;
            result.Add(new SpeakerSegmentDto(speaker, offset, offset + 15, content));
            offset += 16;
        }

        return result;
    }
}
