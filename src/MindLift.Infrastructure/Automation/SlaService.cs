using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #24: First-response SLA and escalation (response-sla).
/// Enforces response-time commitments for sales leads, provides early warnings,
/// and executes automated reassignment and manager escalations on breach.
/// </summary>
public sealed class SlaService(
    AppDbContext db,
    ITenantContext tenant,
    INotificationService notifications,
    ITimelineEventBus timeline,
    ILogger<SlaService> logger) : ISlaService
{
    // ================= Policy Management =================

    public async Task<SlaPolicyDto> CreatePolicyAsync(SaveSlaPolicyRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Policy name is required.");

        if (req.MaxResponseMinutes <= 0)
            throw new ValidationException("MaxResponseMinutes", "Max response minutes must be greater than zero.");

        var policy = new SlaPolicy
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Name = name,
            Description = req.Description?.Trim(),
            IsActive = req.IsActive,
            Priority = req.Priority <= 0 ? 1 : req.Priority,
            MaxResponseMinutes = req.MaxResponseMinutes,
            WarningThresholdPercentage = Math.Clamp(req.WarningThresholdPercentage, 1, 99),
            Sources = CleanArray(req.Sources),
            Ratings = CleanArray(req.Ratings),
            EscalationUserId = req.EscalationUserId,
            EscalationAction = NormalizeEscalationAction(req.EscalationAction),
            CreatedAt = DateTime.UtcNow
        };

        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created SLA policy {PolicyId} ({PolicyName}) with target {Minutes}m",
            policy.Id, policy.Name, policy.MaxResponseMinutes);

        return await MapPolicyDtoAsync(policy, ct);
    }

    public async Task<SlaPolicyDto> GetPolicyByIdAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy == null)
            throw new NotFoundException("SlaPolicy", id);

        return await MapPolicyDtoAsync(policy, ct);
    }

    public async Task<IReadOnlyList<SlaPolicyDto>> ListPoliciesAsync(bool? activeOnly = null, CancellationToken ct = default)
    {
        var query = db.SlaPolicies.AsQueryable();

        if (activeOnly.HasValue)
            query = query.Where(p => p.IsActive == activeOnly.Value);

        var list = await query
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

        var userIds = list.Where(p => p.EscalationUserId.HasValue).Select(p => p.EscalationUserId!.Value).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        return list.Select(p => new SlaPolicyDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            IsActive = p.IsActive,
            Priority = p.Priority,
            MaxResponseMinutes = p.MaxResponseMinutes,
            WarningThresholdPercentage = p.WarningThresholdPercentage,
            Sources = p.Sources,
            Ratings = p.Ratings,
            EscalationUserId = p.EscalationUserId,
            EscalationUserName = p.EscalationUserId.HasValue ? users.GetValueOrDefault(p.EscalationUserId.Value) : null,
            EscalationAction = p.EscalationAction,
            CreatedAt = p.CreatedAt
        }).ToList();
    }

    public async Task<SlaPolicyDto> UpdatePolicyAsync(Guid id, SaveSlaPolicyRequest req, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy == null)
            throw new NotFoundException("SlaPolicy", id);

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Policy name is required.");

        if (req.MaxResponseMinutes <= 0)
            throw new ValidationException("MaxResponseMinutes", "Max response minutes must be greater than zero.");

        policy.Name = name;
        policy.Description = req.Description?.Trim();
        policy.IsActive = req.IsActive;
        policy.Priority = req.Priority <= 0 ? 1 : req.Priority;
        policy.MaxResponseMinutes = req.MaxResponseMinutes;
        policy.WarningThresholdPercentage = Math.Clamp(req.WarningThresholdPercentage, 1, 99);
        policy.Sources = CleanArray(req.Sources);
        policy.Ratings = CleanArray(req.Ratings);
        policy.EscalationUserId = req.EscalationUserId;
        policy.EscalationAction = NormalizeEscalationAction(req.EscalationAction);
        policy.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return await MapPolicyDtoAsync(policy, ct);
    }

    public async Task<bool> DeletePolicyAsync(Guid id, CancellationToken ct = default)
    {
        var policy = await db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy == null) return false;

        policy.IsDeleted = true;
        policy.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= SLA Tracking & Enforcement =================

    public async Task<LeadSlaTrackerDto?> AttachSlaToLeadAsync(Guid leadId, CancellationToken ct = default)
    {
        var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead == null)
            throw new NotFoundException("Lead", leadId);

        var existing = await db.LeadSlaTrackers.FirstOrDefaultAsync(t => t.LeadId == leadId, ct);
        if (existing != null)
            return await MapTrackerDtoAsync(existing, ct);

        var policies = await db.SlaPolicies
            .Where(p => p.IsActive)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.CreatedAt)
            .ToListAsync(ct);

        SlaPolicy? matchedPolicy = null;
        foreach (var p in policies)
        {
            if (MatchesPolicy(p, lead))
            {
                matchedPolicy = p;
                break;
            }
        }

        if (matchedPolicy == null) return null;

        var baseTime = lead.AssignedAt ?? lead.CreatedAt;
        var dueAt = baseTime.AddMinutes(matchedPolicy.MaxResponseMinutes);

        var tracker = new LeadSlaTracker
        {
            TenantId = lead.TenantId,
            LeadId = lead.Id,
            PolicyId = matchedPolicy.Id,
            PolicyName = matchedPolicy.Name,
            DueAt = dueAt,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        if (lead.FirstContactedAt.HasValue)
        {
            tracker.RespondedAt = lead.FirstContactedAt.Value;
            tracker.Status = lead.FirstContactedAt.Value <= dueAt ? "Met" : "Breached";
            if (tracker.Status == "Breached")
                tracker.BreachedAt = dueAt;
        }

        db.LeadSlaTrackers.Add(tracker);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Attached SLA policy {PolicyName} to lead {LeadId} with due date {DueAt}",
            tracker.PolicyName, tracker.LeadId, tracker.DueAt);

        return await MapTrackerDtoAsync(tracker, ct);
    }

    public async Task<LeadSlaTrackerDto> RecordFirstResponseAsync(Guid leadId, RecordResponseRequest? req = null, CancellationToken ct = default)
    {
        var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead == null)
            throw new NotFoundException("Lead", leadId);

        var responseTime = req?.RespondedAt ?? DateTime.UtcNow;
        lead.FirstContactedAt = responseTime;

        var tracker = await db.LeadSlaTrackers.FirstOrDefaultAsync(t => t.LeadId == leadId, ct);
        if (tracker == null)
        {
            await AttachSlaToLeadAsync(leadId, ct);
            tracker = await db.LeadSlaTrackers.FirstOrDefaultAsync(t => t.LeadId == leadId, ct);
        }

        if (tracker != null)
        {
            tracker.RespondedAt = responseTime;
            tracker.Notes = req?.Notes?.Trim() ?? tracker.Notes;
            tracker.Status = responseTime <= tracker.DueAt ? "Met" : "Breached";
            if (tracker.Status == "Breached" && !tracker.BreachedAt.HasValue)
                tracker.BreachedAt = tracker.DueAt;
            tracker.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        if (tracker != null)
        {
            return await MapTrackerDtoAsync(tracker, ct);
        }

        return new LeadSlaTrackerDto
        {
            LeadId = lead.Id,
            PolicyName = "NoPolicy",
            DueAt = responseTime,
            RespondedAt = responseTime,
            Status = "Met",
            IsBreached = false,
            MinutesRemaining = 0
        };
    }

    public async Task<LeadSlaTrackerDto?> GetSlaForLeadAsync(Guid leadId, CancellationToken ct = default)
    {
        var tracker = await db.LeadSlaTrackers.FirstOrDefaultAsync(t => t.LeadId == leadId, ct);
        if (tracker == null) return null;

        return await MapTrackerDtoAsync(tracker, ct);
    }

    public async Task<SlaCheckResultDto> ProcessSlaEscalationsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var pending = await db.LeadSlaTrackers
            .Where(t => t.Status == "Pending")
            .ToListAsync(ct);

        var policyIds = pending.Select(t => t.PolicyId).Distinct().ToList();
        var policies = await db.SlaPolicies
            .Where(p => policyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var leadIds = pending.Select(t => t.LeadId).Distinct().ToList();
        var leads = await db.Set<Lead>()
            .Where(l => leadIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        int checkedCount = 0;
        int warningCount = 0;
        int breachedCount = 0;
        int escalatedCount = 0;

        foreach (var t in pending)
        {
            checkedCount++;
            if (!policies.TryGetValue(t.PolicyId, out var policy)) continue;
            if (!leads.TryGetValue(t.LeadId, out var lead)) continue;

            if (now > t.DueAt)
            {
                // Breached!
                t.Status = "Breached";
                t.BreachedAt = now;
                breachedCount++;

                if (policy.EscalationUserId.HasValue && !t.Escalated)
                {
                    t.Escalated = true;
                    t.EscalatedAt = now;
                    t.EscalatedToUserId = policy.EscalationUserId.Value;
                    escalatedCount++;

                    var previousOwner = lead.OwnerId;

                    if (policy.EscalationAction is "Reassign" or "ReassignAndNotify")
                    {
                        lead.OwnerId = policy.EscalationUserId.Value;
                        lead.AssignedAt = now;
                        lead.UpdatedAt = now;
                    }

                    // Send notification to escalation manager
                    await notifications.NotifyAsync(new NotificationRequest(
                        [policy.EscalationUserId.Value],
                        NotificationTypes.LeadAssigned,
                        $"SLA BREACH ESCALATION: Lead {lead.FirstName} {lead.LastName}",
                        $"First response SLA breached for policy '{policy.Name}'. Lead reassigned or escalated.",
                        "leads",
                        lead.Id,
                        $"/leads/{lead.Id}"), ct);

                    // Publish timeline event
                    await timeline.PublishAsync(new TimelineEventInput(
                        EntityType: "leads",
                        EntityId: lead.Id,
                        Kind: "lead.sla_breached",
                        Summary: $"First-response SLA breached ({policy.Name}). Escalated to manager.",
                        Source: "response-sla",
                        OccurredAt: now), ct);
                }
            }
            else
            {
                // Check warning threshold
                var totalDurationMinutes = policy.MaxResponseMinutes;
                var baseTime = lead.AssignedAt ?? lead.CreatedAt;
                var elapsedMinutes = (now - baseTime).TotalMinutes;
                var pct = (elapsedMinutes / totalDurationMinutes) * 100.0;

                if (pct >= policy.WarningThresholdPercentage && !t.WarningNotifiedAt.HasValue)
                {
                    t.WarningNotifiedAt = now;
                    warningCount++;

                    // Send early warning to assigned rep
                    await notifications.NotifyAsync(new NotificationRequest(
                        [lead.OwnerId],
                        NotificationTypes.LeadAssigned,
                        $"SLA WARNING: Response Due Soon for {lead.FirstName} {lead.LastName}",
                        $"First response SLA due at {t.DueAt:HH:mm} UTC. Only {Math.Max(0, (t.DueAt - now).TotalMinutes):F0}m remaining.",
                        "leads",
                        lead.Id,
                        $"/leads/{lead.Id}"), ct);
                }
            }
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Processed SLA escalations: Checked={Checked}, Breached={Breached}, Escalated={Escalated}, Warnings={Warnings}",
            checkedCount, breachedCount, escalatedCount, warningCount);

        return new SlaCheckResultDto
        {
            CheckedCount = checkedCount,
            WarningCount = warningCount,
            BreachedCount = breachedCount,
            EscalatedCount = escalatedCount,
            ProcessedAt = now
        };
    }

    public async Task<IReadOnlyList<LeadSlaTrackerDto>> ListTrackersAsync(string? status = null, bool? escalated = null, int limit = 50, CancellationToken ct = default)
    {
        var query = db.LeadSlaTrackers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(t => t.Status == status);

        if (escalated.HasValue)
            query = query.Where(t => t.Escalated == escalated.Value);

        var list = await query
            .OrderBy(t => t.DueAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        var userIds = list.Where(t => t.EscalatedToUserId.HasValue).Select(t => t.EscalatedToUserId!.Value).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var now = DateTime.UtcNow;

        return list.Select(t =>
        {
            var isBreached = t.Status == "Breached" || (t.Status == "Pending" && now > t.DueAt);
            var rem = Math.Max(0, (t.DueAt - now).TotalMinutes);

            return new LeadSlaTrackerDto
            {
                Id = t.Id,
                LeadId = t.LeadId,
                PolicyId = t.PolicyId,
                PolicyName = t.PolicyName,
                DueAt = t.DueAt,
                RespondedAt = t.RespondedAt,
                BreachedAt = t.BreachedAt,
                Status = t.Status,
                Escalated = t.Escalated,
                EscalatedAt = t.EscalatedAt,
                EscalatedToUserId = t.EscalatedToUserId,
                EscalatedToUserName = t.EscalatedToUserId.HasValue ? users.GetValueOrDefault(t.EscalatedToUserId.Value) : null,
                Notes = t.Notes,
                IsBreached = isBreached,
                MinutesRemaining = rem
            };
        }).ToList();
    }

    // ================= Helper Methods =================

    private static bool MatchesPolicy(SlaPolicy policy, Lead lead)
    {
        // 1. Sources
        if (policy.Sources.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(lead.Source) || !policy.Sources.Any(s => string.Equals(s, lead.Source, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        // 2. Ratings
        if (policy.Ratings.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(lead.Rating) || !policy.Ratings.Any(r => string.Equals(r, lead.Rating, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static string NormalizeEscalationAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "ReassignAndNotify";
        return action.Trim().ToLowerInvariant() switch
        {
            "notifyonly" => "NotifyOnly",
            "reassign" => "Reassign",
            _ => "ReassignAndNotify"
        };
    }

    private static string[] CleanArray(string[]? input)
    {
        if (input == null || input.Length == 0) return [];
        return input
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<SlaPolicyDto> MapPolicyDtoAsync(SlaPolicy p, CancellationToken ct)
    {
        string? escalationName = null;
        if (p.EscalationUserId.HasValue)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == p.EscalationUserId.Value, ct);
            if (user != null)
                escalationName = $"{user.FirstName} {user.LastName}".Trim();
        }

        return new SlaPolicyDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            IsActive = p.IsActive,
            Priority = p.Priority,
            MaxResponseMinutes = p.MaxResponseMinutes,
            WarningThresholdPercentage = p.WarningThresholdPercentage,
            Sources = p.Sources,
            Ratings = p.Ratings,
            EscalationUserId = p.EscalationUserId,
            EscalationUserName = escalationName,
            EscalationAction = p.EscalationAction,
            CreatedAt = p.CreatedAt
        };
    }

    private async Task<LeadSlaTrackerDto> MapTrackerDtoAsync(LeadSlaTracker t, CancellationToken ct)
    {
        string? escalationName = null;
        if (t.EscalatedToUserId.HasValue)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == t.EscalatedToUserId.Value, ct);
            if (user != null)
                escalationName = $"{user.FirstName} {user.LastName}".Trim();
        }

        var now = DateTime.UtcNow;
        var isBreached = t.Status == "Breached" || (t.Status == "Pending" && now > t.DueAt);
        var rem = Math.Max(0, (t.DueAt - now).TotalMinutes);

        return new LeadSlaTrackerDto
        {
            Id = t.Id,
            LeadId = t.LeadId,
            PolicyId = t.PolicyId,
            PolicyName = t.PolicyName,
            DueAt = t.DueAt,
            RespondedAt = t.RespondedAt,
            BreachedAt = t.BreachedAt,
            Status = t.Status,
            Escalated = t.Escalated,
            EscalatedAt = t.EscalatedAt,
            EscalatedToUserId = t.EscalatedToUserId,
            EscalatedToUserName = escalationName,
            Notes = t.Notes,
            IsBreached = isBreached,
            MinutesRemaining = rem
        };
    }
}
