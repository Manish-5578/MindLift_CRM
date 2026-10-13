using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>Picklists (plan 5.3, 7.2) and pipelines with their stages (plan 7.7).</summary>
public sealed partial class PicklistPipelineService(AppDbContext db, ITenantContext tenant, SettingsVersion version)
{
    [GeneratedRegex("^[a-z0-9_]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValuePattern();

    // ---------- Picklists ----------

    public async Task<IReadOnlyList<PicklistDto>> ListPicklistsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.PicklistValues.AsNoTracking()
            .OrderBy(p => p.PicklistKey).ThenBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);

        return rows.GroupBy(p => p.PicklistKey)
            .Select(g => new PicklistDto(g.Key, g.Select(Map).ToList()))
            .ToList();
    }

    public async Task<PicklistValueDto> AddValueAsync(string picklistKey, SavePicklistValueRequest request, CancellationToken cancellationToken = default)
    {
        // A list exists when the workspace has values for it. Without this check any key would quietly create a new list.
        if (!await db.PicklistValues.AnyAsync(p => p.PicklistKey == picklistKey, cancellationToken))
        {
            throw new NotFoundException("Picklist", picklistKey);
        }

        if (!ValuePattern().IsMatch(request.Value))
        {
            throw Invalid("value", "Use lowercase letters, digits and underscores (for example new_lead).");
        }

        if (await db.PicklistValues.AnyAsync(p => p.PicklistKey == picklistKey && p.Value == request.Value, cancellationToken))
        {
            throw new ConflictException("picklist_value_taken", "This value already exists in the list.");
        }

        if (request.IsDefault)
        {
            await ClearDefaultAsync(picklistKey, cancellationToken);
        }

        var row = new PicklistValue
        {
            PicklistKey = picklistKey,
            Value = request.Value,
            Label = request.Label.Trim(),
            LabelAr = request.LabelAr,
            Category = request.Category,
            Color = request.Color,
            SortOrder = request.SortOrder,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
        };
        db.PicklistValues.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return Map(row);
    }

    /// <summary>The value code never changes. Label, color, order, default and active flag can.</summary>
    public async Task<PicklistValueDto> UpdateValueAsync(string picklistKey, Guid id, SavePicklistValueRequest request, CancellationToken cancellationToken = default)
    {
        var row = await LoadValueAsync(picklistKey, id, cancellationToken);
        if (request.Value != row.Value)
        {
            throw new ConflictException("picklist_value_fixed", "The code of a picklist value cannot be changed. Create a new value instead.");
        }

        if (request.IsDefault && !row.IsDefault)
        {
            await ClearDefaultAsync(picklistKey, cancellationToken);
        }

        row.Label = request.Label.Trim();
        row.LabelAr = request.LabelAr;
        row.Category = request.Category;
        row.Color = request.Color;
        row.SortOrder = request.SortOrder;
        row.IsDefault = request.IsDefault;
        row.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return Map(row);
    }

    /// <summary>
    /// Deletes a value. Values in use need a replacement, which is written to every record that held the old value.
    /// System values cannot be deleted; deactivate them instead.
    /// </summary>
    public async Task DeleteValueAsync(string picklistKey, Guid id, string? replaceWith, CancellationToken cancellationToken = default)
    {
        var row = await LoadValueAsync(picklistKey, id, cancellationToken);
        if (row.IsSystem)
        {
            throw new ConflictException("system_value", "System values cannot be deleted. Deactivate them instead.");
        }

        var usage = await UsageAsync(picklistKey, row.Value, cancellationToken);
        if (usage > 0)
        {
            if (string.IsNullOrWhiteSpace(replaceWith))
            {
                throw new ConflictException("picklist_value_in_use", $"{usage} records use this value. Choose a replacement or deactivate it.");
            }

            var replacement = await db.PicklistValues.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PicklistKey == picklistKey && p.Value == replaceWith, cancellationToken)
                ?? throw Invalid("replaceWith", "The replacement value does not exist.");
            if (replacement.Id == row.Id)
            {
                throw Invalid("replaceWith", "The replacement must be a different value.");
            }

            await ReplaceUsageAsync(picklistKey, row.Value, replaceWith, cancellationToken);
        }

        db.PicklistValues.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    // ---------- Pipelines ----------

    public async Task<IReadOnlyList<PipelineDto>> ListPipelinesAsync(CancellationToken cancellationToken = default)
    {
        var pipelines = await db.Pipelines.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var stages = await db.PipelineStages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        return pipelines.Select(p => MapPipeline(p, stages.Where(s => s.PipelineId == p.Id).ToList())).ToList();
    }

    /// <summary>Creates a pipeline with three stages: New (open), Won and Lost.</summary>
    public async Task<PipelineDto> CreatePipelineAsync(SavePipelineRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureNameFreeAsync(request.Name, null, cancellationToken);
        if (request.IsDefault)
        {
            await ClearDefaultPipelineAsync(cancellationToken);
        }

        var pipeline = new Pipeline
        {
            Name = request.Name.Trim(),
            NameAr = request.NameAr,
            IsDefault = request.IsDefault,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
        };
        db.Pipelines.Add(pipeline);
        db.PipelineStages.Add(NewStage(pipeline.Id, "New", 0, 10, "Open"));
        db.PipelineStages.Add(NewStage(pipeline.Id, "Won", 1, 100, "Won"));
        db.PipelineStages.Add(NewStage(pipeline.Id, "Lost", 2, 0, "Lost"));
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return await GetPipelineAsync(pipeline.Id, cancellationToken);
    }

    public async Task<PipelineDto> UpdatePipelineAsync(Guid id, SavePipelineRequest request, CancellationToken cancellationToken = default)
    {
        var pipeline = await LoadPipelineAsync(id, cancellationToken);
        await EnsureNameFreeAsync(request.Name, id, cancellationToken);
        if (request.IsDefault && !pipeline.IsDefault)
        {
            await ClearDefaultPipelineAsync(cancellationToken);
        }

        pipeline.Name = request.Name.Trim();
        pipeline.NameAr = request.NameAr;
        pipeline.IsDefault = request.IsDefault;
        pipeline.IsActive = request.IsActive;
        pipeline.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return await GetPipelineAsync(id, cancellationToken);
    }

    /// <summary>
    /// Deletes a pipeline. Deals in it must be moved to a stage of another pipeline, given in the request.
    /// The last pipeline cannot be deleted.
    /// </summary>
    public async Task DeletePipelineAsync(Guid id, Guid? moveDealsToStageId, CancellationToken cancellationToken = default)
    {
        var pipeline = await LoadPipelineAsync(id, cancellationToken);
        if (await db.Pipelines.CountAsync(cancellationToken) <= 1)
        {
            throw new BusinessRuleException("last_pipeline", "A workspace needs at least one pipeline.");
        }

        var deals = await db.Deals.Where(d => d.PipelineId == id).ToListAsync(cancellationToken);
        if (deals.Count > 0)
        {
            if (moveDealsToStageId is not { } target)
            {
                throw new ConflictException("pipeline_has_deals", $"{deals.Count} deals are in this pipeline. Choose a stage in another pipeline to move them to.");
            }

            var targetStage = await db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == target, cancellationToken)
                ?? throw Invalid("moveDealsToStageId", "The target stage does not exist.");
            if (targetStage.PipelineId == id)
            {
                throw Invalid("moveDealsToStageId", "Choose a stage in a different pipeline.");
            }

            foreach (var deal in deals)
            {
                deal.PipelineId = targetStage.PipelineId;
                deal.StageId = targetStage.Id;
            }
        }

        var stages = await db.PipelineStages.Where(s => s.PipelineId == id).ToListAsync(cancellationToken);
        db.PipelineStages.RemoveRange(stages);
        db.Pipelines.Remove(pipeline);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<PipelineStageDto> AddStageAsync(Guid pipelineId, SaveStageRequest request, CancellationToken cancellationToken = default)
    {
        await LoadPipelineAsync(pipelineId, cancellationToken);
        var stage = new PipelineStage
        {
            PipelineId = pipelineId,
            Name = request.Name.Trim(),
            NameAr = request.NameAr,
            Probability = request.Probability,
            StageType = request.StageType,
            Color = request.Color,
            RottingDays = request.RottingDays,
        };
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // The position is "insert before the stage that is there now". The stages after it move down by one.
        var ordered = await LoadOrderedStagesAsync(pipelineId, cancellationToken);
        ordered.Insert(Math.Clamp(request.SortOrder - 1, 0, ordered.Count), stage);
        Renumber(ordered);
        db.PipelineStages.Add(stage);
        await db.SaveChangesAsync(cancellationToken);
        await ValidateRulesAsync(pipelineId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return MapStage(stage);
    }

    public async Task<PipelineStageDto> UpdateStageAsync(Guid pipelineId, Guid stageId, SaveStageRequest request, CancellationToken cancellationToken = default)
    {
        var stage = await LoadStageAsync(pipelineId, stageId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        stage.Name = request.Name.Trim();
        stage.NameAr = request.NameAr;
        stage.Probability = request.Probability;
        stage.StageType = request.StageType;
        stage.Color = request.Color;
        stage.RottingDays = request.RottingDays;

        // Moving a stage works the same way: take it out, put it back at the new position, renumber the rest.
        var ordered = await LoadOrderedStagesAsync(pipelineId, cancellationToken);
        ordered.RemoveAll(s => s.Id == stageId);
        ordered.Insert(Math.Clamp(request.SortOrder - 1, 0, ordered.Count), stage);
        Renumber(ordered);
        await db.SaveChangesAsync(cancellationToken);
        await ValidateRulesAsync(pipelineId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return MapStage(stage);
    }

    /// <summary>Deletes a stage. Deals in it move to <paramref name="moveDealsToStageId"/>, a different stage of the same pipeline.</summary>
    public async Task DeleteStageAsync(Guid pipelineId, Guid stageId, Guid? moveDealsToStageId, CancellationToken cancellationToken = default)
    {
        var stage = await LoadStageAsync(pipelineId, stageId, cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var deals = await db.Deals.Where(d => d.StageId == stageId).ToListAsync(cancellationToken);

        if (deals.Count > 0)
        {
            if (moveDealsToStageId is not { } target)
            {
                throw new ConflictException("stage_has_deals", $"{deals.Count} deals are in this stage. Choose another stage to move them to.");
            }

            if (target == stageId || !await db.PipelineStages.AnyAsync(s => s.Id == target && s.PipelineId == pipelineId, cancellationToken))
            {
                throw Invalid("moveDealsToStageId", "Choose a different stage of this pipeline.");
            }

            foreach (var deal in deals)
            {
                deal.StageId = target;
            }
        }

        db.PipelineStages.Remove(stage);
        var remaining = await LoadOrderedStagesAsync(pipelineId, cancellationToken);
        remaining.RemoveAll(s => s.Id == stageId);
        Renumber(remaining);
        await db.SaveChangesAsync(cancellationToken);
        await ValidateRulesAsync(pipelineId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    // ---------- helpers ----------

    private async Task<PipelineDto> GetPipelineAsync(Guid id, CancellationToken cancellationToken)
    {
        var pipeline = await LoadPipelineAsync(id, cancellationToken);
        var stages = await db.PipelineStages.AsNoTracking().Where(s => s.PipelineId == id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        return MapPipeline(pipeline, stages);
    }

    /// <summary>The stages of a pipeline in their current order. Stages that share a number are ordered by name so the order is always the same.</summary>
    private async Task<List<PipelineStage>> LoadOrderedStagesAsync(Guid pipelineId, CancellationToken cancellationToken)
        => await db.PipelineStages.Where(s => s.PipelineId == pipelineId).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(cancellationToken);

    /// <summary>Numbers the stages 1, 2, 3 in list order, which also repairs any gaps or repeats left by older data.</summary>
    private static void Renumber(List<PipelineStage> stages)
    {
        for (var i = 0; i < stages.Count; i++)
        {
            stages[i].SortOrder = i + 1;
        }
    }

    private async Task ValidateRulesAsync(Guid pipelineId, CancellationToken cancellationToken)
    {
        var stages = await db.PipelineStages.AsNoTracking()
            .Where(s => s.PipelineId == pipelineId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ValueTuple<string, int>(s.StageType, s.Probability))
            .ToListAsync(cancellationToken);

        var problem = PipelineRules.Validate(stages);
        if (problem is not null)
        {
            throw new BusinessRuleException("pipeline_rules", problem);
        }
    }

    private async Task<PicklistValue> LoadValueAsync(string picklistKey, Guid id, CancellationToken cancellationToken)
        => await db.PicklistValues.FirstOrDefaultAsync(p => p.PicklistKey == picklistKey && p.Id == id, cancellationToken)
           ?? throw new NotFoundException("Picklist value", id);

    private async Task<Pipeline> LoadPipelineAsync(Guid id, CancellationToken cancellationToken)
        => await db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Pipeline", id);

    private async Task<PipelineStage> LoadStageAsync(Guid pipelineId, Guid stageId, CancellationToken cancellationToken)
        => await db.PipelineStages.FirstOrDefaultAsync(s => s.Id == stageId && s.PipelineId == pipelineId, cancellationToken)
           ?? throw new NotFoundException("Stage", stageId);

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (await db.Pipelines.AnyAsync(p => p.Name == trimmed && p.Id != exceptId, cancellationToken))
        {
            throw new ConflictException("pipeline_name_taken", "A pipeline with this name already exists.");
        }
    }

    private async Task ClearDefaultAsync(string picklistKey, CancellationToken cancellationToken)
    {
        var defaults = await db.PicklistValues.Where(p => p.PicklistKey == picklistKey && p.IsDefault).ToListAsync(cancellationToken);
        foreach (var row in defaults)
        {
            row.IsDefault = false;
        }
    }

    private async Task ClearDefaultPipelineAsync(CancellationToken cancellationToken)
    {
        var defaults = await db.Pipelines.Where(p => p.IsDefault).ToListAsync(cancellationToken);
        foreach (var row in defaults)
        {
            row.IsDefault = false;
        }
    }

    /// <summary>How many records hold a value, across every system field bound to this picklist.</summary>
    private async Task<long> UsageAsync(string picklistKey, string value, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var (table, column) in BoundColumns(picklistKey))
        {
            var sql = $"SELECT COUNT(*) AS \"Value\" FROM \"{table}\" WHERE tenant_id = {{0}} AND is_deleted = false AND \"{column}\" = {{1}}";
            total += await db.Database.SqlQueryRaw<long>(sql, tenant.TenantId!.Value, value).SingleAsync(cancellationToken);
        }

        return total;
    }

    private async Task ReplaceUsageAsync(string picklistKey, string oldValue, string newValue, CancellationToken cancellationToken)
    {
        foreach (var (table, column) in BoundColumns(picklistKey))
        {
            var sql = $"UPDATE \"{table}\" SET \"{column}\" = {{1}} WHERE tenant_id = {{0}} AND \"{column}\" = {{2}}";
            await db.Database.ExecuteSqlRawAsync(sql, [tenant.TenantId!.Value, newValue, oldValue], cancellationToken);
        }
    }

    /// <summary>Physical table and column for every system field bound to a picklist, read from the EF model.</summary>
    private IEnumerable<(string Table, string Column)> BoundColumns(string picklistKey)
    {
        foreach (var entity in MindLift.Application.Common.Fields.SystemFields.Entities)
        {
            var entityType = db.Model.FindEntityType(entity.ClrType)!;
            foreach (var field in entity.SystemFields.Where(f => f.PicklistKey == picklistKey))
            {
                yield return (entityType.GetTableName()!, entityType.FindProperty(field.PropertyName!)!.GetColumnName());
            }
        }
    }

    private static PipelineStage NewStage(Guid pipelineId, string name, int order, int probability, string type) => new()
    {
        PipelineId = pipelineId,
        Name = name,
        SortOrder = order,
        Probability = probability,
        StageType = type,
    };

    private static PicklistValueDto Map(PicklistValue p) => new(
        p.Id, p.Value, p.Label, p.LabelAr, p.Category, p.Color, p.SortOrder, p.IsDefault, p.IsActive, p.IsSystem);

    private static PipelineStageDto MapStage(PipelineStage s) => new(
        s.Id, s.Name, s.NameAr, s.SortOrder, s.Probability, s.StageType, s.Color, s.RottingDays);

    private static PipelineDto MapPipeline(Pipeline p, IReadOnlyList<PipelineStage> stages) => new(
        p.Id, p.Name, p.NameAr, p.IsDefault, p.IsActive, p.SortOrder, stages.Select(MapStage).ToList());

    private static ValidationException Invalid(string key, string message)
        => new(new Dictionary<string, string[]> { [key] = [message] });
}


