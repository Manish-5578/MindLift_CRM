using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Custom fields, label overrides and layouts (plan 4.6.2, 5.1, 7.2). Each entity is identified by its registry key
/// ("leads", "contacts", ...). Every write bumps the settings version.
/// </summary>
public sealed partial class FieldSettingsService(AppDbContext db, ITenantContext tenant, SettingsVersion version)
{
    private static readonly FieldType[] AllowedCustomTypes =
    [
        FieldType.Text, FieldType.TextArea, FieldType.Email, FieldType.Phone, FieldType.Url,
        FieldType.Number, FieldType.Decimal, FieldType.Currency, FieldType.Percent,
        FieldType.Date, FieldType.DateTime, FieldType.Boolean, FieldType.Picklist, FieldType.MultiPicklist,
    ];

    [GeneratedRegex("^[a-z0-9_]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionValuePattern();

    /// <summary>System fields plus active custom fields, as the query and filter code sees them.</summary>
    public async Task<IReadOnlyList<FieldDef>> RegistryAsync(string entityKey, CancellationToken cancellationToken = default)
    {
        var entity = SystemFields.Get(entityKey);
        var custom = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == entityKey && d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        return entity.SystemFields
            .Concat(custom.Select(d => new FieldDef(d.Key, d.Label, d.LabelAr, Enum.Parse<FieldType>(d.FieldType), true, d.IsRequired, d.Filterable, null, null)))
            .ToList();
    }

    /// <summary>Validation rules for the custom fields of one entity, used when records are saved.</summary>
    public async Task<IReadOnlyList<CustomFieldRule>> CustomRulesAsync(string entityKey, CancellationToken cancellationToken = default)
    {
        var defs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == entityKey && d.IsActive)
            .ToListAsync(cancellationToken);

        return defs.Select(d => new CustomFieldRule(
            d.Key, d.Label, Enum.Parse<FieldType>(d.FieldType), d.IsRequired, ReadOptions(d).Select(o => o.Value).ToList())).ToList();
    }

    /// <summary>Every field of an entity with label overrides applied. Hidden fields are included so the UI can show them in settings.</summary>
    public async Task<IReadOnlyList<FieldDto>> GetFieldsAsync(string entityKey, CancellationToken cancellationToken = default)
    {
        var entity = SystemFields.Get(entityKey);
        var overrides = await db.FieldLabelOverrides.AsNoTracking()
            .Where(o => o.EntityType == entityKey)
            .ToDictionaryAsync(o => o.FieldKey, cancellationToken);
        var pickValues = await db.PicklistValues.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);

        var result = new List<FieldDto>();
        foreach (var field in entity.SystemFields)
        {
            overrides.TryGetValue(field.Key, out var over);
            var options = field.PicklistKey is null ? null
                : pickValues.Where(p => p.PicklistKey == field.PicklistKey)
                    .Select(p => new PicklistOptionDto(p.Value, p.Label, p.LabelAr, p.Color)).ToList();

            result.Add(new FieldDto(field.Key, over?.Label ?? field.Label, over?.LabelAr ?? field.LabelAr, field.Type,
                false, field.IsRequired, over?.IsHidden ?? false, field.Filterable, field.PicklistKey, options));
        }

        var customs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == entityKey && d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);
        foreach (var definition in customs)
        {
            overrides.TryGetValue(definition.Key, out var over);
            result.Add(new FieldDto(definition.Key, over?.Label ?? definition.Label, over?.LabelAr ?? definition.LabelAr,
                Enum.Parse<FieldType>(definition.FieldType), true, definition.IsRequired, over?.IsHidden ?? false,
                definition.Filterable, null, ReadOptions(definition)));
        }

        return result;
    }

    public async Task<IReadOnlyList<CustomFieldDto>> ListCustomFieldsAsync(string entityKey, CancellationToken cancellationToken = default)
    {
        SystemFields.Get(entityKey);
        var defs = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == entityKey)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);
        return defs.Select(Map).ToList();
    }

    public async Task<CustomFieldDto> CreateCustomFieldAsync(SaveCustomFieldRequest request, CancellationToken cancellationToken = default)
    {
        var entity = SystemFields.Get(request.EntityType);

        if (!CustomFieldRules.IsValidKey(request.Key))
        {
            throw Invalid("key", "The key must start with cf_, then lowercase letters, digits or underscores (for example cf_budget).");
        }

        if (await db.CustomFieldDefinitions.AnyAsync(d => d.EntityType == entity.Key && d.Key == request.Key, cancellationToken))
        {
            throw new ConflictException("field_key_taken", $"A field with the key {request.Key} already exists.");
        }

        if (await db.CustomFieldDefinitions.CountAsync(d => d.EntityType == entity.Key, cancellationToken) >= CustomFieldRules.MaxFieldsPerEntity)
        {
            throw new BusinessRuleException("field_limit", $"An entity can have at most {CustomFieldRules.MaxFieldsPerEntity} custom fields.");
        }

        var options = ValidateOptions(request.Type, request.Options);
        if (!AllowedCustomTypes.Contains(request.Type))
        {
            throw Invalid("type", "This field type cannot be created as a custom field.");
        }

        var definition = new CustomFieldDefinition
        {
            EntityType = entity.Key,
            Key = request.Key,
            Label = request.Label.Trim(),
            LabelAr = request.LabelAr,
            FieldType = request.Type.ToString(),
            IsRequired = request.IsRequired,
            ShowInList = request.ShowInList,
            Filterable = request.Filterable,
            Section = request.Section,
            SortOrder = request.SortOrder,
            Options = options is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(options)),
        };

        db.CustomFieldDefinitions.Add(definition);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return Map(definition);
    }

    /// <summary>The key and type are fixed after creation. Only labels, flags, options and order can change.</summary>
    public async Task<CustomFieldDto> UpdateCustomFieldAsync(Guid id, SaveCustomFieldRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await db.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Custom field", id);

        if (request.Key != definition.Key)
        {
            throw new ConflictException("field_key_fixed", "The key of a custom field cannot be changed.");
        }

        var type = Enum.Parse<FieldType>(definition.FieldType);
        var options = ValidateOptions(type, request.Options);

        definition.Label = request.Label.Trim();
        definition.LabelAr = request.LabelAr;
        definition.IsRequired = request.IsRequired;
        definition.ShowInList = request.ShowInList;
        definition.Filterable = request.Filterable;
        definition.Section = request.Section;
        definition.SortOrder = request.SortOrder;
        if (options is not null)
        {
            definition.Options = JsonDocument.Parse(JsonSerializer.Serialize(options));
        }

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return Map(definition);
    }

    /// <summary>
    /// Deletes a custom field. If records hold values for it, the caller must pass purgeData=true,
    /// which also removes the key from every record (plan 4.6.2).
    /// </summary>
    public async Task DeleteCustomFieldAsync(Guid id, bool purgeData, CancellationToken cancellationToken = default)
    {
        var definition = await db.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            ?? throw new NotFoundException("Custom field", id);
        var entity = SystemFields.Get(definition.EntityType);

        var used = await CountValuesAsync(entity, definition.Key, cancellationToken);
        if (used > 0 && !purgeData)
        {
            throw new ConflictException("field_has_data", $"{used} records have a value for this field. Confirm the deletion to remove those values too.");
        }

        if (used > 0)
        {
            var table = TableOf(entity);
            await db.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{table}\" SET custom_fields = custom_fields - {{1}}::text WHERE tenant_id = {{0}} AND custom_fields -> {{1}}::text IS NOT NULL",
                [tenant.TenantId!.Value, definition.Key],
                cancellationToken);
        }

        db.CustomFieldDefinitions.Remove(definition);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    /// <summary>Sets a label or hides a field. An empty label removes the override.</summary>
    public async Task<FieldDto> SetOverrideAsync(string entityKey, string fieldKey, SaveFieldOverrideRequest request, CancellationToken cancellationToken = default)
    {
        var fields = await GetFieldsAsync(entityKey, cancellationToken);
        if (fields.All(f => f.Key != fieldKey))
        {
            throw new NotFoundException("Field", fieldKey);
        }

        var row = await db.FieldLabelOverrides.FirstOrDefaultAsync(o => o.EntityType == entityKey && o.FieldKey == fieldKey, cancellationToken);
        var clearLabels = string.IsNullOrWhiteSpace(request.Label) && string.IsNullOrWhiteSpace(request.LabelAr);
        if (clearLabels && !request.IsHidden)
        {
            if (row is not null)
            {
                db.FieldLabelOverrides.Remove(row);
            }
        }
        else
        {
            if (row is null)
            {
                row = new FieldLabelOverride { EntityType = entityKey, FieldKey = fieldKey };
                db.FieldLabelOverrides.Add(row);
            }

            row.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();
            row.LabelAr = string.IsNullOrWhiteSpace(request.LabelAr) ? null : request.LabelAr.Trim();
            row.IsHidden = request.IsHidden;
        }

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return (await GetFieldsAsync(entityKey, cancellationToken)).Single(f => f.Key == fieldKey);
    }

    /// <summary>The layout for a form or detail page. When none is saved, the default is every visible system field in two columns.</summary>
    public async Task<LayoutDto> GetLayoutAsync(string entityKey, string kind, CancellationToken cancellationToken = default)
    {
        ValidateKind(kind);
        SystemFields.Get(entityKey);
        var row = await db.Layouts.AsNoTracking().FirstOrDefaultAsync(l => l.EntityType == entityKey && l.Kind == kind, cancellationToken);
        if (row is null)
        {
            var defaults = SystemFields.Get(entityKey).SystemFields.Select(f => f.Key).ToList();
            return new LayoutDto(entityKey, kind, [new LayoutSectionDto("Details", null, 2, defaults)]);
        }

        var sections = JsonSerializer.Deserialize<List<LayoutSectionDto>>(row.Definition.RootElement.GetRawText()) ?? [];
        return new LayoutDto(entityKey, kind, sections);
    }

    public async Task<LayoutDto> SaveLayoutAsync(string entityKey, string kind, SaveLayoutRequest request, CancellationToken cancellationToken = default)
    {
        ValidateKind(kind);
        SystemFields.Get(entityKey);

        var known = (await RegistryAsync(entityKey, cancellationToken)).Select(f => f.Key).ToHashSet();
        var used = new HashSet<string>();
        foreach (var section in request.Sections)
        {
            if (section.Columns is not (1 or 2))
            {
                throw Invalid("columns", "A section has 1 or 2 columns.");
            }

            foreach (var field in section.Fields)
            {
                if (!known.Contains(field))
                {
                    throw Invalid("fields", $"Unknown field '{field}'.");
                }

                if (!used.Add(field))
                {
                    throw Invalid("fields", $"Field '{field}' appears more than once.");
                }
            }
        }

        var row = await db.Layouts.FirstOrDefaultAsync(l => l.EntityType == entityKey && l.Kind == kind, cancellationToken);
        if (row is null)
        {
            row = new Layout { EntityType = entityKey, Kind = kind };
            db.Layouts.Add(row);
        }

        row.Definition = JsonDocument.Parse(JsonSerializer.Serialize(request.Sections));
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return new LayoutDto(entityKey, kind, request.Sections);
    }

    private static void ValidateKind(string kind)
    {
        if (kind is not ("Form" or "Detail"))
        {
            throw new NotFoundException("Layout", kind);
        }
    }

    private static List<PicklistOptionDto>? ValidateOptions(FieldType type, IReadOnlyList<PicklistOptionDto>? options)
    {
        if (type is not (FieldType.Picklist or FieldType.MultiPicklist))
        {
            return null;
        }

        if (options is null || options.Count == 0)
        {
            throw Invalid("options", "A picklist needs at least one option.");
        }

        if (options.Any(o => !OptionValuePattern().IsMatch(o.Value)))
        {
            throw Invalid("options", "Option values use lowercase letters, digits and underscores only.");
        }

        if (options.Select(o => o.Value).Distinct().Count() != options.Count)
        {
            throw Invalid("options", "Option values must be unique.");
        }

        return options.Select(o => new PicklistOptionDto(o.Value, o.Label.Trim(), o.LabelAr, o.Color)).ToList();
    }

    private static List<PicklistOptionDto> ReadOptions(CustomFieldDefinition definition)
        => definition.Options is null ? [] : JsonSerializer.Deserialize<List<PicklistOptionDto>>(definition.Options.RootElement.GetRawText()) ?? [];

    private static CustomFieldDto Map(CustomFieldDefinition d) => new(
        d.Id, d.EntityType, d.Key, d.Label, d.LabelAr, Enum.Parse<FieldType>(d.FieldType), d.IsRequired, d.IsActive,
        d.ShowInList, d.Filterable, d.Section, d.SortOrder, null, ReadOptions(d));

    private async Task<long> CountValuesAsync(EntityInfo entity, string key, CancellationToken cancellationToken)
    {
        var table = TableOf(entity);
        var sql = $"SELECT COUNT(*) AS \"Value\" FROM \"{table}\" WHERE tenant_id = {{0}} AND is_deleted = false AND custom_fields -> {{1}}::text IS NOT NULL";
        return await db.Database.SqlQueryRaw<long>(sql, tenant.TenantId!.Value, key).SingleAsync(cancellationToken);
    }

    /// <summary>The physical table of a registry entity, read from the EF model so SQL never uses unchecked names.</summary>
    internal string TableOf(EntityInfo entity)
        => db.Model.FindEntityType(entity.ClrType)!.GetTableName()!;

    private static ValidationException Invalid(string key, string message)
        => new(new Dictionary<string, string[]> { [key] = [message] });
}

