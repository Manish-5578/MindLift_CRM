using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Rules;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Service for managing and evaluating conditional field visibility, mandatory requirements, and read-only attributes.
/// </summary>
public sealed class LayoutRuleService(
    AppDbContext db,
    ITenantContext tenant,
    SettingsVersion version) : ILayoutRuleService
{
    public async Task<IReadOnlyList<LayoutRuleDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default)
    {
        var query = db.LayoutRules.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var norm = entityType.Trim().ToLowerInvariant();
            query = query.Where(x => x.EntityType == norm || x.EntityType == "");
        }

        var rows = await query.OrderBy(x => x.EntityType).ThenBy(x => x.Priority).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<LayoutRuleDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.LayoutRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Layout rule", id);
        return Map(row);
    }

    public async Task<LayoutRuleDto> CreateAsync(SaveLayoutRuleRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        string? conditionJson = null;
        if (request.Condition != null)
        {
            var errors = RuleEvaluator.Validate(request.Condition);
            if (errors.Count > 0)
            {
                throw new ValidationException(errors[0]);
            }
            conditionJson = JsonSerializer.Serialize(request.Condition);
        }

        var row = new LayoutRule
        {
            EntityType = request.EntityType.Trim().ToLowerInvariant(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            Priority = request.Priority,
            ConditionJson = conditionJson,
            ShowFields = request.ShowFields?.ToArray() ?? [],
            HideFields = request.HideFields?.ToArray() ?? [],
            MandatoryFields = request.MandatoryFields?.ToArray() ?? [],
            ReadOnlyFields = request.ReadOnlyFields?.ToArray() ?? [],
        };

        db.LayoutRules.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return Map(row);
    }

    public async Task<LayoutRuleDto> UpdateAsync(Guid id, SaveLayoutRuleRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var row = await db.LayoutRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Layout rule", id);

        string? conditionJson = null;
        if (request.Condition != null)
        {
            var errors = RuleEvaluator.Validate(request.Condition);
            if (errors.Count > 0)
            {
                throw new ValidationException(errors[0]);
            }
            conditionJson = JsonSerializer.Serialize(request.Condition);
        }

        row.EntityType = request.EntityType.Trim().ToLowerInvariant();
        row.Name = request.Name.Trim();
        row.Description = request.Description?.Trim();
        row.IsActive = request.IsActive;
        row.Priority = request.Priority;
        row.ConditionJson = conditionJson;
        row.ShowFields = request.ShowFields?.ToArray() ?? [];
        row.HideFields = request.HideFields?.ToArray() ?? [];
        row.MandatoryFields = request.MandatoryFields?.ToArray() ?? [];
        row.ReadOnlyFields = request.ReadOnlyFields?.ToArray() ?? [];

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return Map(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.LayoutRules.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Layout rule", id);

        db.LayoutRules.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<LayoutRuleEvaluationResult> EvaluateAsync(
        string entityType,
        IReadOnlyDictionary<string, object?> fieldValues,
        CancellationToken cancellationToken = default)
    {
        var normEntity = entityType.Trim().ToLowerInvariant();
        var facts = fieldValues != null
            ? new Dictionary<string, object?>(fieldValues, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var rules = await db.LayoutRules.AsNoTracking()
            .Where(x => x.IsActive && (x.EntityType == normEntity || x.EntityType == ""))
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);

        var appliedRules = new List<string>();
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mandatory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            RuleNode? ruleNode = null;
            if (!string.IsNullOrWhiteSpace(rule.ConditionJson))
            {
                try
                {
                    ruleNode = JsonSerializer.Deserialize<RuleNode>(rule.ConditionJson);
                }
                catch
                {
                    ruleNode = null;
                }
            }

            var isMatch = ruleNode == null || RuleEvaluator.Evaluate(ruleNode, facts);
            if (isMatch)
            {
                appliedRules.Add(rule.Name);

                foreach (var f in rule.ShowFields)
                {
                    visible.Add(f);
                    hidden.Remove(f);
                }

                foreach (var f in rule.HideFields)
                {
                    hidden.Add(f);
                    visible.Remove(f);
                }

                foreach (var f in rule.MandatoryFields)
                {
                    mandatory.Add(f);
                }

                foreach (var f in rule.ReadOnlyFields)
                {
                    readOnly.Add(f);
                }
            }
        }

        return new LayoutRuleEvaluationResult(
            normEntity,
            appliedRules,
            visible.ToList(),
            hidden.ToList(),
            mandatory.ToList(),
            readOnly.ToList());
    }

    public async Task<LayoutRuleValidationResult> ValidateAsync(
        string entityType,
        IReadOnlyDictionary<string, object?> fieldValues,
        CancellationToken cancellationToken = default)
    {
        var evalResult = await EvaluateAsync(entityType, fieldValues, cancellationToken);
        var errors = new List<string>();

        var facts = fieldValues != null
            ? new Dictionary<string, object?>(fieldValues, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredField in evalResult.MandatoryFields)
        {
            if (!facts.TryGetValue(requiredField, out var val) || val == null || (val is string s && string.IsNullOrWhiteSpace(s)))
            {
                errors.Add($"Field '{requiredField}' is required by layout rules.");
            }
        }

        return new LayoutRuleValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateRequest(SaveLayoutRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.EntityType))
            throw new ValidationException("EntityType is required.");
    }

    private static LayoutRuleDto Map(LayoutRule r)
    {
        RuleNode? condition = null;
        if (!string.IsNullOrWhiteSpace(r.ConditionJson))
        {
            try
            {
                condition = JsonSerializer.Deserialize<RuleNode>(r.ConditionJson);
            }
            catch
            {
                condition = null;
            }
        }

        return new LayoutRuleDto(
            r.Id,
            r.EntityType,
            r.Name,
            r.Description,
            r.IsActive,
            r.Priority,
            condition,
            r.ShowFields,
            r.HideFields,
            r.MandatoryFields,
            r.ReadOnlyFields);
    }
}
