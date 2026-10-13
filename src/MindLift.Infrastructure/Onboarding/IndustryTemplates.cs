using System.Reflection;
using System.Text.Json;
using MindLift.Application.Common.Fields;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Onboarding;

// The JSON shape of an industry template (plan 6.4). Files live in Templates/Industries and are embedded in the assembly.

public sealed class IndustryTemplate
{
    public string Key { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public TemplateModules Modules { get; set; } = new();
    public Dictionary<string, TemplatePicklist> Picklists { get; set; } = new();
    public List<TemplatePipeline> Pipelines { get; set; } = [];
    public List<TemplateCustomField> CustomFields { get; set; } = [];

    /// <summary>entity key → kind (Form or Detail) → sections.</summary>
    public Dictionary<string, Dictionary<string, List<TemplateLayoutSection>>> Layouts { get; set; } = new();
    public List<string> DashboardHighlights { get; set; } = [];
    public TemplateSample SampleData { get; set; } = new();
}

public sealed class TemplateModules
{
    public Dictionary<string, TemplateLabel> Labels { get; set; } = new();
    public List<string> Enable { get; set; } = [];
    public List<string> Disable { get; set; } = [];
}

public sealed class TemplateLabel
{
    public string Singular { get; set; } = string.Empty;
    public string Plural { get; set; } = string.Empty;
}

public sealed class TemplatePicklist
{
    public string Mode { get; set; } = "merge";
    public List<TemplateOption> Values { get; set; } = [];
}

public sealed class TemplateOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
}

public sealed class TemplatePipeline
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public bool IsDefault { get; set; }
    public List<TemplateStage> Stages { get; set; } = [];
}

public sealed class TemplateStage
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public int Probability { get; set; }
    public string Type { get; set; } = "Open";
    public int? RottingDays { get; set; }
}

public sealed class TemplateCustomField
{
    public string Entity { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? LabelAr { get; set; }
    public string Type { get; set; } = "Text";
    public string? Section { get; set; }
    public bool ShowInList { get; set; }
    public List<TemplateOption>? Options { get; set; }
}

/// <summary>Same property names as the layout the settings API returns, so the stored JSON reads back directly.</summary>
public sealed class TemplateLayoutSection
{
    public string Section { get; set; } = string.Empty;
    public string? SectionAr { get; set; }
    public int Columns { get; set; } = 2;
    public List<string> Fields { get; set; } = [];
}

public sealed class TemplateSample
{
    public string Profile { get; set; } = "general";
}

/// <summary>Loads the embedded templates once.</summary>
public static class IndustryTemplates
{
    private static readonly Lazy<IReadOnlyList<IndustryTemplate>> Loaded = new(Load);

    public static IReadOnlyList<IndustryTemplate> All => Loaded.Value;

    public static IndustryTemplate? Get(string key) => All.FirstOrDefault(t => t.Key == key);

    private static IReadOnlyList<IndustryTemplate> Load()
    {
        var assembly = typeof(IndustryTemplates).Assembly;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var templates = new List<IndustryTemplate>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json", StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            templates.Add(JsonSerializer.Deserialize<IndustryTemplate>(stream, options)
                ?? throw new InvalidOperationException($"Template {resource} is empty."));
        }

        return templates;
    }
}

/// <summary>Checks a template against the rules of the product. Run by tests, so a broken template never ships.</summary>
public static class IndustryTemplateValidator
{
    public static IReadOnlyList<string> Validate(IndustryTemplate template)
    {
        var errors = new List<string>();
        var where = template.Key;

        if (!System.Text.RegularExpressions.Regex.IsMatch(template.Key, "^[a-z][a-z_]{1,40}$"))
        {
            errors.Add($"{where}: key must be lowercase letters and underscores.");
        }

        foreach (var name in template.Modules.Labels.Keys.Concat(template.Modules.Enable).Concat(template.Modules.Disable))
        {
            if (!Enum.TryParse<AppModule>(name, out _))
            {
                errors.Add($"{where}: unknown module '{name}'.");
            }
        }

        var systemPicklists = BasePicklists.All.Select(p => p.Key).ToHashSet();
        foreach (var (key, picklist) in template.Picklists)
        {
            if (!systemPicklists.Contains(key))
            {
                errors.Add($"{where}: picklist '{key}' is not a known picklist.");
            }

            if (picklist.Mode is not ("merge" or "replace"))
            {
                errors.Add($"{where}: picklist '{key}' mode must be merge or replace.");
            }

            if (picklist.Values.Select(v => v.Value).Distinct().Count() != picklist.Values.Count)
            {
                errors.Add($"{where}: picklist '{key}' has duplicate values.");
            }
        }

        if (template.Pipelines.Count(p => p.IsDefault) != 1)
        {
            errors.Add($"{where}: exactly one pipeline must be the default.");
        }

        foreach (var pipeline in template.Pipelines)
        {
            var stages = pipeline.Stages.Select(s => (s.Type, s.Probability)).ToList();
            var problem = MindLift.Application.Common.Fields.PipelineRules.Validate(stages);
            if (problem is not null)
            {
                errors.Add($"{where}: pipeline '{pipeline.Name}': {problem}");
            }
        }

        var customKeys = new HashSet<(string, string)>();
        foreach (var field in template.CustomFields)
        {
            if (!SystemFieldsKnown(field.Entity))
            {
                errors.Add($"{where}: custom field {field.Key} is on unknown entity '{field.Entity}'.");
                continue;
            }

            if (!CustomFieldRules.IsValidKey(field.Key))
            {
                errors.Add($"{where}: custom field key '{field.Key}' is not valid.");
            }

            if (!customKeys.Add((field.Entity, field.Key)))
            {
                errors.Add($"{where}: custom field {field.Key} is defined twice on {field.Entity}.");
            }

            if (!Enum.TryParse<FieldType>(field.Type, out var type))
            {
                errors.Add($"{where}: custom field {field.Key} has unknown type '{field.Type}'.");
            }
            else if (type is FieldType.Picklist or FieldType.MultiPicklist && (field.Options is null || field.Options.Count == 0))
            {
                errors.Add($"{where}: picklist field {field.Key} needs options.");
            }
        }

        foreach (var (entity, kinds) in template.Layouts)
        {
            if (!SystemFieldsKnown(entity))
            {
                errors.Add($"{where}: layout for unknown entity '{entity}'.");
                continue;
            }

            var known = SystemFields.Get(entity).SystemFields.Select(f => f.Key)
                .Concat(template.CustomFields.Where(c => c.Entity == entity).Select(c => c.Key))
                .ToHashSet();

            foreach (var section in kinds.Values.SelectMany(s => s))
            {
                foreach (var field in section.Fields.Where(f => !known.Contains(f)))
                {
                    errors.Add($"{where}: layout for {entity} uses unknown field '{field}'.");
                }
            }
        }

        return errors;
    }

    private static bool SystemFieldsKnown(string entity) => SystemFields.Entities.Any(e => e.Key == entity);
}
