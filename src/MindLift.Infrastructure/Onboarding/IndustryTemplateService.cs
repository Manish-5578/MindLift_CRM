using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Onboarding;

/// <summary>
/// Applies an industry template (plan 6.5). Everything it creates is marked Origin = Template, so applying a
/// different template later removes exactly those rows and nothing the user added.
/// Switching is refused once real (non-sample) records exist, because their values would no longer match the fields.
/// </summary>
public sealed class IndustryTemplateService(AppDbContext db, ITenantContext tenant, SettingsVersion version, SampleDataService samples)
{
    public const string TemplateOrigin = "Template";

    public IReadOnlyList<IndustrySummaryDto> List()
        => IndustryTemplates.All.Select(Summary).ToList();

    public async Task ApplyAsync(string key, CancellationToken cancellationToken = default)
    {
        var template = IndustryTemplates.Get(key) ?? throw new NotFoundException("Industry", key);
        var workspace = await db.Tenants.FirstAsync(t => t.Id == tenant.TenantId, cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (workspace.IndustryKey is not null)
        {
            if (await HasRealRecordsAsync(cancellationToken))
            {
                throw new ConflictException("template_locked", "This workspace already has real records. The industry can no longer be changed here.");
            }

            await samples.RemoveAsync(cancellationToken);
            await RemoveTemplateRowsAsync(cancellationToken);
        }

        await ApplyModulesAsync(template, cancellationToken);
        await ApplyPicklistsAsync(template, cancellationToken);
        await ApplyPipelinesAsync(template, cancellationToken);
        await ApplyCustomFieldsAsync(template, cancellationToken);
        await ApplyLayoutsAsync(template, cancellationToken);

        workspace.IndustryKey = template.Key;
        workspace.TemplateVersion = template.Version;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    private async Task<bool> HasRealRecordsAsync(CancellationToken cancellationToken)
        => await db.Leads.AnyAsync(x => !x.IsSample, cancellationToken)
           || await db.Contacts.AnyAsync(x => !x.IsSample, cancellationToken)
           || await db.Accounts.AnyAsync(x => !x.IsSample, cancellationToken)
           || await db.Deals.AnyAsync(x => !x.IsSample, cancellationToken);

    /// <summary>Removes every row a previous template created. Sample records are removed by the caller first.</summary>
    private async Task RemoveTemplateRowsAsync(CancellationToken cancellationToken)
    {
        var templatePipelineIds = await db.Pipelines.Where(p => p.Origin == TemplateOrigin).Select(p => p.Id).ToListAsync(cancellationToken);
        var stages = await db.PipelineStages.Where(s => templatePipelineIds.Contains(s.PipelineId)).ToListAsync(cancellationToken);
        db.PipelineStages.RemoveRange(stages);
        db.Pipelines.RemoveRange(await db.Pipelines.Where(p => p.Origin == TemplateOrigin).ToListAsync(cancellationToken));
        db.PicklistValues.RemoveRange(await db.PicklistValues.Where(p => p.Origin == TemplateOrigin).ToListAsync(cancellationToken));
        db.CustomFieldDefinitions.RemoveRange(await db.CustomFieldDefinitions.Where(d => d.Origin == TemplateOrigin).ToListAsync(cancellationToken));
        db.Layouts.RemoveRange(await db.Layouts.Where(l => l.Origin == TemplateOrigin).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Every module returns to its default state first, then the template's labels and switches apply.</summary>
    private async Task ApplyModulesAsync(IndustryTemplate template, CancellationToken cancellationToken)
    {
        var rows = await db.ModuleSettings.ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsEnabled = SystemSeedService.Phase1Modules.Contains(row.Module);
            row.SingularLabel = Labels.Singular(row.Module);
            row.PluralLabel = Labels.Plural(row.Module);
        }

        foreach (var (name, label) in template.Modules.Labels)
        {
            var row = rows.Single(r => r.Module == Enum.Parse<AppModule>(name));
            row.SingularLabel = label.Singular;
            row.PluralLabel = label.Plural;
        }

        foreach (var name in template.Modules.Enable)
        {
            rows.Single(r => r.Module == Enum.Parse<AppModule>(name)).IsEnabled = true;
        }

        foreach (var name in template.Modules.Disable)
        {
            rows.Single(r => r.Module == Enum.Parse<AppModule>(name)).IsEnabled = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Adds the template's values to a picklist. Existing values with the same code are kept as they are.</summary>
    private async Task ApplyPicklistsAsync(IndustryTemplate template, CancellationToken cancellationToken)
    {
        foreach (var (key, picklist) in template.Picklists)
        {
            var existing = await db.PicklistValues.Where(p => p.PicklistKey == key).ToListAsync(cancellationToken);
            var order = existing.Count == 0 ? 0 : existing.Max(p => p.SortOrder) + 1;

            foreach (var option in picklist.Values)
            {
                if (existing.Any(p => p.Value == option.Value))
                {
                    continue;
                }

                db.PicklistValues.Add(new PicklistValue
                {
                    PicklistKey = key,
                    Value = option.Value,
                    Label = option.Label,
                    LabelAr = option.LabelAr,
                    SortOrder = order++,
                    IsActive = true,
                    Origin = TemplateOrigin,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyPipelinesAsync(IndustryTemplate template, CancellationToken cancellationToken)
    {
        var userPipelines = await db.Pipelines.Where(p => p.Origin != TemplateOrigin).ToListAsync(cancellationToken);
        var taken = userPipelines.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var templatePipeline in template.Pipelines)
        {
            if (templatePipeline.IsDefault)
            {
                foreach (var user in userPipelines)
                {
                    user.IsDefault = false;
                }
            }

            var name = taken.Contains(templatePipeline.Name) ? $"{templatePipeline.Name} (template)" : templatePipeline.Name;
            var pipeline = new Pipeline
            {
                Name = name,
                NameAr = templatePipeline.NameAr,
                IsDefault = templatePipeline.IsDefault,
                IsActive = true,
                SortOrder = order++,
                Origin = TemplateOrigin,
            };
            db.Pipelines.Add(pipeline);
            taken.Add(name);

            var stageOrder = 0;
            foreach (var stage in templatePipeline.Stages)
            {
                db.PipelineStages.Add(new PipelineStage
                {
                    PipelineId = pipeline.Id,
                    Name = stage.Name,
                    NameAr = stage.NameAr,
                    SortOrder = stageOrder++,
                    Probability = stage.Probability,
                    StageType = stage.Type,
                    RottingDays = stage.RottingDays,
                    Origin = TemplateOrigin,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyCustomFieldsAsync(IndustryTemplate template, CancellationToken cancellationToken)
    {
        var index = 0;
        foreach (var field in template.CustomFields)
        {
            if (await db.CustomFieldDefinitions.AnyAsync(d => d.EntityType == field.Entity && d.Key == field.Key, cancellationToken))
            {
                throw new ConflictException("field_key_taken", $"A custom field {field.Key} already exists on {field.Entity} and was not created by a template.");
            }

            db.CustomFieldDefinitions.Add(new CustomFieldDefinition
            {
                EntityType = field.Entity,
                Key = field.Key,
                Label = field.Label,
                LabelAr = field.LabelAr,
                FieldType = field.Type,
                ShowInList = field.ShowInList,
                Filterable = true,
                Section = field.Section,
                SortOrder = index++,
                Options = field.Options is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(field.Options)),
                Origin = TemplateOrigin,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyLayoutsAsync(IndustryTemplate template, CancellationToken cancellationToken)
    {
        foreach (var (entity, kinds) in template.Layouts)
        {
            foreach (var (kind, sections) in kinds)
            {
                if (await db.Layouts.AnyAsync(l => l.EntityType == entity && l.Kind == kind && l.Origin != TemplateOrigin, cancellationToken))
                {
                    continue; // the user designed this layout; keep it
                }

                db.Layouts.Add(new Layout
                {
                    EntityType = entity,
                    Kind = kind,
                    Definition = JsonDocument.Parse(JsonSerializer.Serialize(sections)),
                    Origin = TemplateOrigin,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IndustrySummaryDto Summary(IndustryTemplate t)
    {
        var defaultPipeline = t.Pipelines.FirstOrDefault(p => p.IsDefault) ?? t.Pipelines[0];
        var dealLabel = t.Modules.Labels.TryGetValue("Deals", out var deals) ? deals.Singular : "Deal";
        return new IndustrySummaryDto(
            t.Key,
            t.Name,
            t.NameAr,
            t.Description ?? string.Empty,
            dealLabel,
            defaultPipeline.Stages.Select(s => s.Name).ToList(),
            t.CustomFields.Select(f => f.Label).ToList(),
            t.Modules.Enable);
    }
}
