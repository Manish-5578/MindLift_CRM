using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Rules;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Evaluates and manages stage gating rules for deals.
/// Uses the F2 rules engine, field validation, line item checks, and role restrictions.
/// Cannot be bypassed via API or Kanban drag.
/// </summary>
public sealed class DealStageGateService(
    AppDbContext db,
    IFeatureFlags features,
    ITenantContext tenant)
{
    private const string FeatureKey = "deal-stage-gating";

    public async Task<IReadOnlyList<DealStageGateDto>> GetGatesAsync(Guid pipelineId, Guid stageId, CancellationToken ct = default)
    {
        var gates = await db.DealStageGates
            .AsNoTracking()
            .Where(g => g.PipelineId == pipelineId && g.StageId == stageId)
            .OrderBy(g => g.Name)
            .ToListAsync(ct);

        return gates.Select(MapToDto).ToList();
    }

    public async Task<DealStageGateDto> CreateGateAsync(Guid pipelineId, Guid stageId, SaveDealStageGateRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        var gate = new DealStageGate
        {
            PipelineId = pipelineId,
            StageId = stageId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            RequiredFields = request.RequiredFields?.ToArray() ?? [],
            MinAmount = request.MinAmount,
            RequireLineItems = request.RequireLineItems,
            ConditionJson = request.Condition != null ? JsonSerializer.Serialize(request.Condition) : null,
            AllowedRoles = request.AllowedRoles?.ToArray() ?? [],
            FailureMessage = request.FailureMessage?.Trim()
        };

        db.DealStageGates.Add(gate);
        await db.SaveChangesAsync(ct);

        return MapToDto(gate);
    }

    public async Task<DealStageGateDto> UpdateGateAsync(Guid pipelineId, Guid stageId, Guid gateId, SaveDealStageGateRequest request, CancellationToken ct = default)
    {
        ValidateRequest(request);

        var gate = await db.DealStageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.PipelineId == pipelineId && g.StageId == stageId, ct)
            ?? throw new NotFoundException("StageGate", gateId);

        gate.Name = request.Name.Trim();
        gate.Description = request.Description?.Trim();
        gate.IsActive = request.IsActive;
        gate.RequiredFields = request.RequiredFields?.ToArray() ?? [];
        gate.MinAmount = request.MinAmount;
        gate.RequireLineItems = request.RequireLineItems;
        gate.ConditionJson = request.Condition != null ? JsonSerializer.Serialize(request.Condition) : null;
        gate.AllowedRoles = request.AllowedRoles?.ToArray() ?? [];
        gate.FailureMessage = request.FailureMessage?.Trim();

        await db.SaveChangesAsync(ct);

        return MapToDto(gate);
    }

    public async Task DeleteGateAsync(Guid pipelineId, Guid stageId, Guid gateId, CancellationToken ct = default)
    {
        var gate = await db.DealStageGates
            .FirstOrDefaultAsync(g => g.Id == gateId && g.PipelineId == pipelineId && g.StageId == stageId, ct)
            ?? throw new NotFoundException("StageGate", gateId);

        db.DealStageGates.Remove(gate);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Validates whether a deal satisfies all gating criteria to move into targetStage.
    /// Throws BusinessRuleException if any gating check fails.
    /// </summary>
    public async Task ValidateTransitionAsync(Deal deal, PipelineStage targetStage, CancellationToken ct = default)
    {
        if (!await features.IsEnabledAsync(FeatureKey, ct))
        {
            return;
        }

        var gates = await db.DealStageGates
            .AsNoTracking()
            .Where(g => g.PipelineId == deal.PipelineId && g.StageId == targetStage.Id && g.IsActive)
            .ToListAsync(ct);

        if (gates.Count == 0)
        {
            return;
        }

        foreach (var gate in gates)
        {
            // 1. Role restriction check
            if (gate.AllowedRoles.Length > 0 && tenant.UserId is { } userId)
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                if (user != null && !user.IsTenantOwner && !user.IsPlatformAdmin)
                {
                    string? userRoleName = null;
                    if (user.RoleId is { } rId)
                    {
                        userRoleName = await db.Roles.AsNoTracking().Where(r => r.Id == rId).Select(r => r.Name).FirstOrDefaultAsync(ct);
                    }

                    if (userRoleName == null || !gate.AllowedRoles.Contains(userRoleName, StringComparer.OrdinalIgnoreCase))
                    {
                        throw new BusinessRuleException("stage_gate_role",
                            gate.FailureMessage ?? $"Only users with roles ({string.Join(", ", gate.AllowedRoles)}) can move deals to {targetStage.Name}.");
                    }
                }
            }

            // 2. Minimum amount check
            if (gate.MinAmount.HasValue && deal.Amount < gate.MinAmount.Value)
            {
                throw new BusinessRuleException("stage_gate_min_amount",
                    gate.FailureMessage ?? $"Deal amount must be at least {gate.MinAmount.Value:N2} to move to {targetStage.Name}.");
            }

            // 3. Line items required check
            if (gate.RequireLineItems)
            {
                var hasLineItems = await db.DealLineItems.AnyAsync(li => li.DealId == deal.Id, ct);
                if (!hasLineItems)
                {
                    throw new BusinessRuleException("stage_gate_line_items",
                        gate.FailureMessage ?? $"Deal must have at least one product line item before moving to {targetStage.Name}.");
                }
            }

            // 4. Required fields check
            if (gate.RequiredFields.Length > 0)
            {
                var facts = BuildDealFacts(deal);
                foreach (var field in gate.RequiredFields)
                {
                    var cleanField = field.Trim().ToLowerInvariant();
                    if (!facts.TryGetValue(cleanField, out var val) || val == null || string.IsNullOrWhiteSpace(val.ToString()))
                    {
                        throw new BusinessRuleException("stage_gate_required_field",
                            gate.FailureMessage ?? $"Required field '{field}' must be provided before moving to {targetStage.Name}.");
                    }
                }
            }

            // 5. Custom RuleNode evaluation (F2 rules engine)
            if (!string.IsNullOrWhiteSpace(gate.ConditionJson))
            {
                RuleNode? rule = null;
                try
                {
                    rule = JsonSerializer.Deserialize<RuleNode>(gate.ConditionJson);
                }
                catch
                {
                    // Fail closed if serialized rule is corrupt
                    throw new BusinessRuleException("stage_gate_condition",
                        gate.FailureMessage ?? $"Deal gating condition could not be evaluated for {targetStage.Name}.");
                }

                if (rule != null)
                {
                    var facts = BuildDealFacts(deal);
                    var matched = RuleEvaluator.Evaluate(rule, facts);
                    if (!matched)
                    {
                        throw new BusinessRuleException("stage_gate_condition",
                            gate.FailureMessage ?? $"Deal does not satisfy gating condition for {targetStage.Name}.");
                    }
                }
            }
        }
    }

    private static Dictionary<string, object?> BuildDealFacts(Deal deal)
    {
        var facts = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = deal.Name,
            ["amount"] = deal.Amount,
            ["currency"] = deal.Currency,
            ["probability"] = deal.Probability,
            ["status"] = deal.Status,
            ["description"] = deal.Description,
            ["account_id"] = deal.AccountId?.ToString(),
            ["account"] = deal.AccountId?.ToString(),
            ["primary_contact_id"] = deal.PrimaryContactId?.ToString(),
            ["contact"] = deal.PrimaryContactId?.ToString(),
            ["expected_close_date"] = deal.ExpectedCloseDate?.ToString("yyyy-MM-dd"),
            ["has_account"] = deal.AccountId != null,
            ["has_contact"] = deal.PrimaryContactId != null
        };

        if (deal.CustomFields != null)
        {
            foreach (var prop in deal.CustomFields.RootElement.EnumerateObject())
            {
                facts[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.ToString()
                };
            }
        }

        return facts;
    }

    private static void ValidateRequest(SaveDealStageGateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "Gate rule name is required.");
        }

        if (request.Condition != null)
        {
            var errors = RuleEvaluator.Validate(request.Condition);
            if (errors.Count > 0)
            {
                throw new ValidationException("condition", string.Join(" ", errors));
            }
        }
    }

    private static DealStageGateDto MapToDto(DealStageGate g)
    {
        RuleNode? rule = null;
        if (!string.IsNullOrWhiteSpace(g.ConditionJson))
        {
            try
            {
                rule = JsonSerializer.Deserialize<RuleNode>(g.ConditionJson);
            }
            catch { }
        }

        return new DealStageGateDto(
            g.Id,
            g.PipelineId,
            g.StageId,
            g.Name,
            g.Description,
            g.IsActive,
            g.RequiredFields,
            g.MinAmount,
            g.RequireLineItems,
            rule,
            g.AllowedRoles,
            g.FailureMessage
        );
    }
}
