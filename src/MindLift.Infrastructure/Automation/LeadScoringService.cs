using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Automation;

/// <summary>
/// Phase 4 / #23: Lead scoring (lead-scoring).
/// Automated lead qualification scoring engine based on demographic, firmographic,
/// behavioral, engagement, and time decay rules.
/// </summary>
public sealed class LeadScoringService(
    AppDbContext db,
    ITenantContext tenant,
    ILogger<LeadScoringService> logger) : ILeadScoringService
{
    // ================= Rule Management =================

    public async Task<LeadScoringRuleDto> CreateRuleAsync(SaveLeadScoringRuleRequest req, CancellationToken ct = default)
    {
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Rule name is required.");

        var field = req.ConditionField?.Trim();
        if (string.IsNullOrWhiteSpace(field))
            throw new ValidationException("ConditionField", "Condition field is required.");

        var rule = new LeadScoringRule
        {
            TenantId = tenant.TenantId ?? Guid.Empty,
            Name = name,
            Description = req.Description?.Trim(),
            IsActive = req.IsActive,
            RuleType = string.IsNullOrWhiteSpace(req.RuleType) ? "Demographic" : req.RuleType.Trim(),
            Points = req.Points,
            ConditionField = field,
            Operator = string.IsNullOrWhiteSpace(req.Operator) ? "Equals" : req.Operator.Trim(),
            ConditionValue = req.ConditionValue?.Trim() ?? string.Empty,
            Order = req.Order <= 0 ? 1 : req.Order,
            CreatedAt = DateTime.UtcNow
        };

        db.LeadScoringRules.Add(rule);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created lead scoring rule {RuleId} ({RuleName}) with delta {Points} pts",
            rule.Id, rule.Name, rule.Points);

        return MapRuleDto(rule);
    }

    public async Task<LeadScoringRuleDto> GetRuleByIdAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.LeadScoringRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null)
            throw new NotFoundException("LeadScoringRule", id);

        return MapRuleDto(rule);
    }

    public async Task<IReadOnlyList<LeadScoringRuleDto>> ListRulesAsync(bool? activeOnly = null, CancellationToken ct = default)
    {
        var query = db.LeadScoringRules.AsQueryable();

        if (activeOnly.HasValue)
            query = query.Where(r => r.IsActive == activeOnly.Value);

        var list = await query
            .OrderBy(r => r.Order)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return list.Select(MapRuleDto).ToList();
    }

    public async Task<LeadScoringRuleDto> UpdateRuleAsync(Guid id, SaveLeadScoringRuleRequest req, CancellationToken ct = default)
    {
        var rule = await db.LeadScoringRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null)
            throw new NotFoundException("LeadScoringRule", id);

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Rule name is required.");

        var field = req.ConditionField?.Trim();
        if (string.IsNullOrWhiteSpace(field))
            throw new ValidationException("ConditionField", "Condition field is required.");

        rule.Name = name;
        rule.Description = req.Description?.Trim();
        rule.IsActive = req.IsActive;
        rule.RuleType = string.IsNullOrWhiteSpace(req.RuleType) ? "Demographic" : req.RuleType.Trim();
        rule.Points = req.Points;
        rule.ConditionField = field;
        rule.Operator = string.IsNullOrWhiteSpace(req.Operator) ? "Equals" : req.Operator.Trim();
        rule.ConditionValue = req.ConditionValue?.Trim() ?? string.Empty;
        rule.Order = req.Order <= 0 ? 1 : req.Order;
        rule.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapRuleDto(rule);
    }

    public async Task<bool> DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.LeadScoringRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule == null) return false;

        rule.IsDeleted = true;
        rule.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ================= Scoring Evaluation =================

    public async Task<LeadScoreResultDto> EvaluateLeadScoreAsync(Guid leadId, CancellationToken ct = default)
    {
        var lead = await db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == leadId, ct);
        if (lead == null)
            throw new NotFoundException("Lead", leadId);

        var rules = await db.LeadScoringRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync(ct);

        // Pre-query context data for behavioral rules
        bool hasCadenceTouch = false;
        if (rules.Any(r => r.ConditionField.Equals("CadenceTouch", StringComparison.OrdinalIgnoreCase)))
        {
            hasCadenceTouch = await db.CadenceEnrollments
                .AnyAsync(e => e.TargetType == "Lead" && e.TargetId == lead.Id, ct);
        }

        bool hasTimelineActivity = false;
        if (rules.Any(r => r.ConditionField.Equals("TimelineActivity", StringComparison.OrdinalIgnoreCase)))
        {
            hasTimelineActivity = await db.TimelineEvents
                .AnyAsync(e => (e.EntityType == "leads" || e.EntityType == "lead") && e.EntityId == lead.Id, ct);
        }

        int runningScore = 0;
        var appliedLogs = new List<LeadScoreLog>();

        foreach (var rule in rules)
        {
            if (MatchesRule(rule, lead, hasCadenceTouch, hasTimelineActivity))
            {
                runningScore += rule.Points;
                var log = new LeadScoreLog
                {
                    TenantId = lead.TenantId,
                    LeadId = lead.Id,
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    Points = rule.Points,
                    RunningScore = runningScore,
                    Reason = $"Applied rule '{rule.Name}' ({rule.RuleType}): {rule.Points:+0;-0;0} pts [{rule.ConditionField} {rule.Operator} {rule.ConditionValue}]",
                    ScoredAt = DateTime.UtcNow
                };
                appliedLogs.Add(log);
            }
        }

        int finalScore = Math.Max(0, runningScore);
        string grade = finalScore >= 70 ? "Hot" : (finalScore >= 30 ? "Warm" : "Cold");

        lead.Score = finalScore;
        lead.ScoreGrade = grade;
        lead.LastScoredAt = DateTime.UtcNow;

        if (appliedLogs.Count > 0)
        {
            db.LeadScoreLogs.AddRange(appliedLogs);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Lead {LeadId} scored: {FinalScore} ({Grade}) with {AppliedCount} rules matched",
            lead.Id, finalScore, grade, appliedLogs.Count);

        return new LeadScoreResultDto
        {
            LeadId = lead.Id,
            Score = finalScore,
            ScoreGrade = grade,
            LastScoredAt = lead.LastScoredAt,
            AppliedRulesCount = appliedLogs.Count,
            AppliedRules = appliedLogs.Select(l => new LeadScoreLogDto
            {
                Id = l.Id,
                LeadId = l.LeadId,
                RuleId = l.RuleId,
                RuleName = l.RuleName,
                Points = l.Points,
                RunningScore = l.RunningScore,
                Reason = l.Reason,
                ScoredAt = l.ScoredAt
            }).ToList()
        };
    }

    public async Task<BatchScoreResultDto> BatchEvaluateScoresAsync(BatchScoreRequest? req = null, CancellationToken ct = default)
    {
        var query = db.Set<Lead>()
            .Where(l => l.Status != "converted");

        if (req?.LeadIds != null && req.LeadIds.Count > 0)
        {
            query = query.Where(l => req.LeadIds.Contains(l.Id));
        }

        var leads = await query.ToListAsync(ct);

        int hot = 0;
        int warm = 0;
        int cold = 0;

        foreach (var lead in leads)
        {
            var res = await EvaluateLeadScoreAsync(lead.Id, ct);
            switch (res.ScoreGrade)
            {
                case "Hot": hot++; break;
                case "Warm": warm++; break;
                default: cold++; break;
            }
        }

        return new BatchScoreResultDto
        {
            TotalEvaluated = leads.Count,
            TotalHotLeads = hot,
            TotalWarmLeads = warm,
            TotalColdLeads = cold,
            ProcessedAt = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<LeadScoreLogDto>> GetScoreLogsAsync(Guid leadId, CancellationToken ct = default)
    {
        var logs = await db.LeadScoreLogs
            .Where(l => l.LeadId == leadId)
            .OrderByDescending(l => l.ScoredAt)
            .ToListAsync(ct);

        return logs.Select(l => new LeadScoreLogDto
        {
            Id = l.Id,
            LeadId = l.LeadId,
            RuleId = l.RuleId,
            RuleName = l.RuleName,
            Points = l.Points,
            RunningScore = l.RunningScore,
            Reason = l.Reason,
            ScoredAt = l.ScoredAt
        }).ToList();
    }

    // ================= Rule Evaluation Engine =================

    private static bool MatchesRule(LeadScoringRule rule, Lead lead, bool hasCadenceTouch, bool hasTimelineActivity)
    {
        var field = rule.ConditionField.Trim();

        // 1. Special behavioral/activity flags
        if (field.Equals("HasEmail", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(lead.Email);

        if (field.Equals("HasPhone", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(lead.Phone);

        if (field.Equals("HasCompany", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(lead.CompanyName);

        if (field.Equals("CadenceTouch", StringComparison.OrdinalIgnoreCase))
            return hasCadenceTouch;

        if (field.Equals("TimelineActivity", StringComparison.OrdinalIgnoreCase))
            return hasTimelineActivity;

        if (field.Equals("InactivityDays", StringComparison.OrdinalIgnoreCase))
        {
            var referenceDate = lead.UpdatedAt ?? lead.CreatedAt;
            var daysInactive = (DateTime.UtcNow - referenceDate).TotalDays;
            if (int.TryParse(rule.ConditionValue, out var thresholdDays))
                return daysInactive >= thresholdDays;
            return false;
        }

        // 2. Standard and custom string properties
        string? leadValue = field.ToLowerInvariant() switch
        {
            "industry" => ExtractCustomFieldString(lead.CustomFields, "industry", "Industry"),
            "source" or "leadsource" => lead.Source,
            "rating" or "leadrating" => lead.Rating,
            "status" or "leadstatus" => lead.Status,
            "company" or "companyname" => lead.CompanyName,
            "country" => ExtractCustomFieldString(lead.CustomFields, "country", "Country", "country_code"),
            "state" or "region" => ExtractCustomFieldString(lead.CustomFields, "state", "State", "region", "Region"),
            "jobtitle" or "title" => ExtractCustomFieldString(lead.CustomFields, "job_title", "title", "Title"),
            _ => ExtractCustomFieldString(lead.CustomFields, field)
        };

        // 3. Numeric comparisons (Revenue, CompanySize)
        if (field.Equals("AnnualRevenue", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("Revenue", StringComparison.OrdinalIgnoreCase))
        {
            var rev = ExtractCustomFieldDecimal(lead.CustomFields, "revenue", "annual_revenue", "AnnualRevenue");
            if (rev.HasValue && decimal.TryParse(rule.ConditionValue, out var targetRev))
            {
                return rule.Operator.ToLowerInvariant() switch
                {
                    "greaterthan" or ">" => rev.Value > targetRev,
                    "greaterthanorequal" or ">=" => rev.Value >= targetRev,
                    "lessthan" or "<" => rev.Value < targetRev,
                    "lessthanorequal" or "<=" => rev.Value <= targetRev,
                    _ => rev.Value == targetRev
                };
            }
            return false;
        }

        if (field.Equals("CompanySize", StringComparison.OrdinalIgnoreCase) ||
            field.Equals("Employees", StringComparison.OrdinalIgnoreCase))
        {
            var size = ExtractCustomFieldInt(lead.CustomFields, "company_size", "employees", "CompanySize", "Employees");
            if (size.HasValue && int.TryParse(rule.ConditionValue, out var targetSize))
            {
                return rule.Operator.ToLowerInvariant() switch
                {
                    "greaterthan" or ">" => size.Value > targetSize,
                    "greaterthanorequal" or ">=" => size.Value >= targetSize,
                    "lessthan" or "<" => size.Value < targetSize,
                    "lessthanorequal" or "<=" => size.Value <= targetSize,
                    _ => size.Value == targetSize
                };
            }
            return false;
        }

        // 4. String comparisons
        if (string.IsNullOrWhiteSpace(leadValue))
            return false;

        var target = rule.ConditionValue.Trim();
        var actual = leadValue.Trim();

        return rule.Operator.ToLowerInvariant() switch
        {
            "equals" => string.Equals(actual, target, StringComparison.OrdinalIgnoreCase),
            "notequals" => !string.Equals(actual, target, StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(target, StringComparison.OrdinalIgnoreCase),
            "inlist" => target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .Any(item => string.Equals(item, actual, StringComparison.OrdinalIgnoreCase)),
            _ => string.Equals(actual, target, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string? ExtractCustomFieldString(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString();
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.ToString();
            }
        }
        return null;
    }

    private static decimal? ExtractCustomFieldDecimal(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
                    return val;
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static int? ExtractCustomFieldInt(JsonDocument? doc, params string[] propertyNames)
    {
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                    return val;
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private static LeadScoringRuleDto MapRuleDto(LeadScoringRule r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        IsActive = r.IsActive,
        RuleType = r.RuleType,
        Points = r.Points,
        ConditionField = r.ConditionField,
        Operator = r.Operator,
        ConditionValue = r.ConditionValue,
        Order = r.Order,
        CreatedAt = r.CreatedAt
    };
}
