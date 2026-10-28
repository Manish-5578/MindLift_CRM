using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class TelephonyService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline) : ITelephonyService
{
    private static string GenerateCallSid()
    {
        return "CA" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static string NormalizePhone(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var digitsAndPlus = new string(input.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return digitsAndPlus;
    }

    public async Task<TelephonySettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        return new TelephonySettingsDto(
            settings.Provider,
            settings.AccountSid,
            settings.CallerId,
            settings.IsActive,
            settings.AutoLogCalls,
            settings.RecordCallsDefault,
            settings.WebRtcEnabled);
    }

    public async Task<TelephonySettingsDto> UpdateSettingsAsync(UpdateTelephonySettingsRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await db.TelephonySettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (settings == null)
        {
            settings = new TelephonySettings { TenantId = tenantId };
            db.TelephonySettings.Add(settings);
        }

        if (!string.IsNullOrWhiteSpace(request.Provider))
            settings.Provider = request.Provider.Trim();

        settings.AccountSid = request.AccountSid?.Trim();
        if (!string.IsNullOrWhiteSpace(request.AuthToken))
            settings.EncryptedAuthToken = request.AuthToken.Trim(); // In real setup, encrypted

        if (!string.IsNullOrWhiteSpace(request.CallerId))
            settings.CallerId = NormalizePhone(request.CallerId);

        settings.IsActive = request.IsActive;
        settings.AutoLogCalls = request.AutoLogCalls;
        settings.RecordCallsDefault = request.RecordCallsDefault;
        settings.WebRtcEnabled = request.WebRtcEnabled;

        await db.SaveChangesAsync(ct);

        return new TelephonySettingsDto(
            settings.Provider,
            settings.AccountSid,
            settings.CallerId,
            settings.IsActive,
            settings.AutoLogCalls,
            settings.RecordCallsDefault,
            settings.WebRtcEnabled);
    }

    public async Task<InitiateCallResult> InitiateCallAsync(InitiateCallRequest request, Guid userId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var isEnabled = await featureFlags.IsEnabledAsync("click-to-call", ct);
        if (!isEnabled)
            throw new ForbiddenException();

        var normalizedTo = NormalizePhone(request.ToNumber);
        if (string.IsNullOrWhiteSpace(normalizedTo) || normalizedTo.Length < 3)
            return new InitiateCallResult(false, Guid.Empty, string.Empty, "Failed", string.Empty, request.ToNumber, null, "A valid destination phone number is required.");

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        if (!settings.IsActive)
            return new InitiateCallResult(false, Guid.Empty, string.Empty, "Failed", settings.CallerId, normalizedTo, null, "Telephony service is currently inactive for this tenant.");

        var fromNumber = !string.IsNullOrWhiteSpace(request.FromNumber)
            ? NormalizePhone(request.FromNumber)
            : settings.CallerId;

        var callSid = GenerateCallSid();
        var callId = Guid.CreateVersion7();

        // Resolve AccountId if not provided but ContactId or DealId is given
        var accountId = request.AccountId;
        if (accountId == null && request.ContactId is { } contactId)
        {
            var contact = await db.Set<Contact>().FirstOrDefaultAsync(c => c.Id == contactId, ct);
            accountId = contact?.AccountId;
        }
        else if (accountId == null && request.DealId is { } dealId)
        {
            var deal = await db.Set<Deal>().FirstOrDefaultAsync(d => d.Id == dealId, ct);
            accountId = deal?.AccountId;
        }

        var callSession = new CallSession
        {
            Id = callId,
            TenantId = tenantId,
            UserId = userId,
            CallSid = callSid,
            FromNumber = fromNumber,
            ToNumber = normalizedTo,
            Direction = "Outbound",
            Status = "Initiated",
            StartedAtUtc = DateTime.UtcNow,
            LeadId = request.LeadId,
            ContactId = request.ContactId,
            AccountId = accountId,
            DealId = request.DealId,
            Notes = request.Notes,
            RecordingUrl = null,
            TranscriptionStatus = (request.Record ?? settings.RecordCallsDefault) ? "Pending" : "None"
        };

        db.CallSessions.Add(callSession);
        await db.SaveChangesAsync(ct);

        string? webRtcToken = null;
        if (settings.WebRtcEnabled)
        {
            webRtcToken = $"rtc_token_{callSid}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
        }

        return new InitiateCallResult(
            true,
            callId,
            callSid,
            "Initiated",
            fromNumber,
            normalizedTo,
            webRtcToken,
            "Call initiated successfully.");
    }

    public async Task<CallSessionDto> GetCallAsync(Guid id, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        return MapDto(call);
    }

    public async Task<CallSessionDto> UpdateCallStatusAsync(UpdateCallStatusRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CallSid))
            throw new ValidationException("callSid", "CallSid is required.");

        var call = await db.CallSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CallSid == request.CallSid && !c.IsDeleted, ct);

        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        var targetTenantId = call.TenantId;
        using var scope = tenantContext.Use(targetTenantId);

        var isEnabled = await featureFlags.IsEnabledAsync("click-to-call", ct);
        if (!isEnabled)
            throw new ForbiddenException();

        var previousStatus = call.Status;
        call.Status = request.Status;

        if (string.Equals(request.Status, "InProgress", StringComparison.OrdinalIgnoreCase) && call.AnsweredAtUtc == null)
        {
            call.AnsweredAtUtc = DateTime.UtcNow;
        }

        var isTerminal = request.Status is "Completed" or "Busy" or "Failed" or "NoAnswer" or "Canceled";
        if (isTerminal)
        {
            if (call.EndedAtUtc == null)
                call.EndedAtUtc = DateTime.UtcNow;

            if (request.DurationSeconds.HasValue)
            {
                call.DurationSeconds = request.DurationSeconds.Value;
            }
            else if (call.AnsweredAtUtc.HasValue)
            {
                call.DurationSeconds = (int)Math.Max(0, (call.EndedAtUtc.Value - call.AnsweredAtUtc.Value).TotalSeconds);
            }

            if (!string.IsNullOrWhiteSpace(request.RecordingUrl))
            {
                call.RecordingUrl = request.RecordingUrl;
            }

            // Publish completed call to timeline
            if (string.Equals(request.Status, "Completed", StringComparison.OrdinalIgnoreCase) && !string.Equals(previousStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                await PublishCallTimelineEventAsync(call, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return MapDto(call);
    }

    public async Task<CallSessionDto> SetDispositionAsync(Guid callId, UpdateCallDispositionRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        call.Disposition = request.Disposition;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            call.Notes = string.IsNullOrWhiteSpace(call.Notes)
                ? request.Notes
                : $"{call.Notes}\n{request.Notes}";
        }

        await db.SaveChangesAsync(ct);
        return MapDto(call);
    }

    public async Task<CallSessionDto> EndCallAsync(Guid callId, int? durationSeconds = null, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var call = await db.CallSessions.FirstOrDefaultAsync(c => c.Id == callId, ct);
        if (call == null)
            throw new NotFoundException("call_not_found", "Call session was not found.");

        var prevStatus = call.Status;
        call.Status = "Completed";
        call.EndedAtUtc = DateTime.UtcNow;
        if (durationSeconds.HasValue)
        {
            call.DurationSeconds = durationSeconds.Value;
        }
        else if (call.AnsweredAtUtc.HasValue)
        {
            call.DurationSeconds = (int)Math.Max(0, (call.EndedAtUtc.Value - call.AnsweredAtUtc.Value).TotalSeconds);
        }

        if (!string.Equals(prevStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            await PublishCallTimelineEventAsync(call, ct);
        }

        await db.SaveChangesAsync(ct);
        return MapDto(call);
    }

    public async Task<IReadOnlyList<CallSessionDto>> GetCallHistoryAsync(CallHistoryQuery query, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var q = db.CallSessions.AsNoTracking().Where(c => c.TenantId == tenantId);

        if (query.LeadId.HasValue) q = q.Where(c => c.LeadId == query.LeadId.Value);
        if (query.ContactId.HasValue) q = q.Where(c => c.ContactId == query.ContactId.Value);
        if (query.AccountId.HasValue) q = q.Where(c => c.AccountId == query.AccountId.Value);
        if (query.DealId.HasValue) q = q.Where(c => c.DealId == query.DealId.Value);
        if (query.UserId.HasValue) q = q.Where(c => c.UserId == query.UserId.Value);
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(c => c.Status == query.Status);

        var list = await q.OrderByDescending(c => c.StartedAtUtc)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToListAsync(ct);

        return list.Select(MapDto).ToList();
    }

    private async Task PublishCallTimelineEventAsync(CallSession call, CancellationToken ct)
    {
        try
        {
            var summary = $"Phone call completed ({call.DurationSeconds}s)";
            if (call.LeadId is { } leadId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "lead",
                    leadId,
                    "call_completed",
                    summary,
                    "telephony"), ct);
            }
            else if (call.ContactId is { } contactId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "contact",
                    contactId,
                    "call_completed",
                    summary,
                    "telephony"), ct);
            }

            if (call.DealId is { } dealId)
            {
                await timeline.PublishAsync(new TimelineEventInput(
                    "deal",
                    dealId,
                    "call_completed",
                    summary,
                    "telephony"), ct);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private async Task<TelephonySettings> GetSettingsEntityAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.TelephonySettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

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
                WebRtcEnabled = true
            };
            db.TelephonySettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }

    private static CallSessionDto MapDto(CallSession c) => new(
        c.Id,
        c.UserId,
        c.CallSid,
        c.FromNumber,
        c.ToNumber,
        c.Direction,
        c.Status,
        c.StartedAtUtc,
        c.AnsweredAtUtc,
        c.EndedAtUtc,
        c.DurationSeconds,
        c.LeadId,
        c.ContactId,
        c.AccountId,
        c.DealId,
        c.Notes,
        c.Disposition,
        c.RecordingUrl,
        c.TranscriptionText);
}
