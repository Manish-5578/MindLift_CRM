using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Phase 2 / #11: Custom Object Builder (custom-objects).
/// Service for managing custom object definitions, schema validation, and dynamic record payloads.
/// </summary>
public sealed partial class CustomObjectService(
    AppDbContext db,
    ITenantContext tenant,
    SettingsVersion version) : ICustomObjectService
{
    private static readonly Regex SlugRegex = GeneratedSlugRegex();
    private static readonly Regex KeyRegex = GeneratedKeyRegex();

    private static readonly HashSet<string> ValidFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "number", "boolean", "date", "datetime", "picklist", "email", "phone", "url", "textarea"
    };

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex GeneratedSlugRegex();

    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex GeneratedKeyRegex();

    public async Task<IReadOnlyList<CustomObjectDefinitionDto>> ListDefinitionsAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = db.CustomObjectDefinitions.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        var list = await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return list.Select(MapDefinition).ToList();
    }

    public async Task<CustomObjectDefinitionDto> GetDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.CustomObjectDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Custom object", id);
        return MapDefinition(row);
    }

    public async Task<CustomObjectDefinitionDto> GetDefinitionBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normSlug = slug.Trim().ToLowerInvariant();
        var row = await db.CustomObjectDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == normSlug, cancellationToken)
            ?? throw new NotFoundException("Custom object", slug);
        return MapDefinition(row);
    }

    public async Task<CustomObjectDefinitionDto> CreateDefinitionAsync(SaveCustomObjectDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDefinitionRequest(request);

        var normSlug = request.Slug.Trim().ToLowerInvariant();
        if (await db.CustomObjectDefinitions.AnyAsync(x => x.Slug == normSlug, cancellationToken))
        {
            throw new ConflictException("custom_object_slug_exists", $"A custom object with slug '{normSlug}' already exists.");
        }

        var fields = request.Fields ?? [];
        var fieldsJson = JsonSerializer.Serialize(fields);

        var row = new CustomObjectDefinition
        {
            Name = request.Name.Trim(),
            PluralName = request.PluralName.Trim(),
            Slug = normSlug,
            Description = request.Description?.Trim(),
            Icon = request.Icon?.Trim(),
            TitleField = string.IsNullOrWhiteSpace(request.TitleField) ? "name" : request.TitleField.Trim().ToLowerInvariant(),
            IsActive = request.IsActive,
            FieldsJson = fieldsJson
        };

        db.CustomObjectDefinitions.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return MapDefinition(row);
    }

    public async Task<CustomObjectDefinitionDto> UpdateDefinitionAsync(Guid id, SaveCustomObjectDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDefinitionRequest(request);

        var row = await db.CustomObjectDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Custom object", id);

        var normSlug = request.Slug.Trim().ToLowerInvariant();
        if (!string.Equals(row.Slug, normSlug, StringComparison.OrdinalIgnoreCase))
        {
            if (await db.CustomObjectDefinitions.AnyAsync(x => x.Slug == normSlug && x.Id != id, cancellationToken))
            {
                throw new ConflictException("custom_object_slug_exists", $"A custom object with slug '{normSlug}' already exists.");
            }
        }

        var fields = request.Fields ?? [];
        row.Name = request.Name.Trim();
        row.PluralName = request.PluralName.Trim();
        row.Slug = normSlug;
        row.Description = request.Description?.Trim();
        row.Icon = request.Icon?.Trim();
        row.TitleField = string.IsNullOrWhiteSpace(request.TitleField) ? "name" : request.TitleField.Trim().ToLowerInvariant();
        row.IsActive = request.IsActive;
        row.FieldsJson = JsonSerializer.Serialize(fields);

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return MapDefinition(row);
    }

    public async Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.CustomObjectDefinitions.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Custom object", id);

        // Soft-delete definition
        db.CustomObjectDefinitions.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<CustomObjectRecordPagedResult> ListRecordsAsync(string slug, CustomObjectRecordQuery query, CancellationToken cancellationToken = default)
    {
        var def = await GetActiveDefinitionBySlug(slug, cancellationToken);

        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 25 : query.PageSize;

        var q = db.CustomObjectRecords.AsNoTracking().Where(x => x.ObjectDefinitionId == def.Id);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            q = q.Where(x => x.DisplayName.ToLower().Contains(term));
        }

        var total = await q.CountAsync(cancellationToken);
        var rows = await q.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => MapRecord(r, def.Slug)).ToList();
        return new CustomObjectRecordPagedResult(items, total, page, pageSize);
    }

    public async Task<CustomObjectRecordDto> GetRecordAsync(string slug, Guid recordId, CancellationToken cancellationToken = default)
    {
        var def = await GetActiveDefinitionBySlug(slug, cancellationToken);
        var row = await db.CustomObjectRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == recordId && x.ObjectDefinitionId == def.Id, cancellationToken)
            ?? throw new NotFoundException("Custom object record", recordId);

        return MapRecord(row, def.Slug);
    }

    public async Task<CustomObjectRecordDto> CreateRecordAsync(string slug, SaveCustomObjectRecordRequest request, CancellationToken cancellationToken = default)
    {
        var def = await GetActiveDefinitionBySlug(slug, cancellationToken);
        var fields = ParseFields(def.FieldsJson);

        ValidateRecordData(fields, request.Data);

        var currentTenantId = tenant.TenantId ?? Guid.Empty;
        var maxNum = await db.CustomObjectRecords.IgnoreQueryFilters()
            .Where(x => x.TenantId == currentTenantId && x.ObjectDefinitionId == def.Id)
            .MaxAsync(x => (int?)x.RecordNumber, cancellationToken) ?? 0;
        var recordNum = maxNum + 1;

        var displayName = ComputeDisplayName(def, request.Data, recordNum);
        var dataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, object?>());

        var row = new CustomObjectRecord
        {
            ObjectDefinitionId = def.Id,
            RecordNumber = recordNum,
            DisplayName = displayName,
            DataJson = dataJson
        };

        db.CustomObjectRecords.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return MapRecord(row, def.Slug);
    }

    public async Task<CustomObjectRecordDto> UpdateRecordAsync(string slug, Guid recordId, SaveCustomObjectRecordRequest request, CancellationToken cancellationToken = default)
    {
        var def = await GetActiveDefinitionBySlug(slug, cancellationToken);
        var fields = ParseFields(def.FieldsJson);

        ValidateRecordData(fields, request.Data);

        var row = await db.CustomObjectRecords
            .FirstOrDefaultAsync(x => x.Id == recordId && x.ObjectDefinitionId == def.Id, cancellationToken)
            ?? throw new NotFoundException("Custom object record", recordId);

        row.DisplayName = ComputeDisplayName(def, request.Data, row.RecordNumber);
        row.DataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, object?>());

        await db.SaveChangesAsync(cancellationToken);

        return MapRecord(row, def.Slug);
    }

    public async Task DeleteRecordAsync(string slug, Guid recordId, CancellationToken cancellationToken = default)
    {
        var def = await GetActiveDefinitionBySlug(slug, cancellationToken);
        var row = await db.CustomObjectRecords
            .FirstOrDefaultAsync(x => x.Id == recordId && x.ObjectDefinitionId == def.Id, cancellationToken)
            ?? throw new NotFoundException("Custom object record", recordId);

        db.CustomObjectRecords.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CustomObjectDefinition> GetActiveDefinitionBySlug(string slug, CancellationToken cancellationToken)
    {
        var normSlug = slug.Trim().ToLowerInvariant();
        return await db.CustomObjectDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normSlug, cancellationToken)
            ?? throw new NotFoundException("Custom object", slug);
    }

    private static void ValidateDefinitionRequest(SaveCustomObjectDefinitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.PluralName))
            throw new ValidationException("PluralName is required.");
        if (string.IsNullOrWhiteSpace(request.Slug))
            throw new ValidationException("Slug is required.");

        var normSlug = request.Slug.Trim().ToLowerInvariant();
        if (!SlugRegex.IsMatch(normSlug))
            throw new ValidationException("Slug must contain only lowercase alphanumeric characters, dashes, or underscores.");

        if (request.Fields != null)
        {
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in request.Fields)
            {
                if (string.IsNullOrWhiteSpace(f.Key))
                    throw new ValidationException("Field Key cannot be empty.");
                var normKey = f.Key.Trim().ToLowerInvariant();
                if (!KeyRegex.IsMatch(normKey))
                    throw new ValidationException($"Field Key '{f.Key}' must contain only lowercase alphanumeric characters and underscores.");
                if (!seenKeys.Add(normKey))
                    throw new ValidationException($"Duplicate field key '{f.Key}'.");
                if (string.IsNullOrWhiteSpace(f.Label))
                    throw new ValidationException($"Field Label cannot be empty for field '{f.Key}'.");
                if (!ValidFieldTypes.Contains(f.Type))
                    throw new ValidationException($"Field Type '{f.Type}' is not supported. Supported types: {string.Join(", ", ValidFieldTypes)}.");
                if (string.Equals(f.Type, "picklist", StringComparison.OrdinalIgnoreCase) && (f.Options == null || f.Options.Count == 0))
                    throw new ValidationException($"Picklist field '{f.Key}' must have at least one option.");
            }
        }
    }

    private static void ValidateRecordData(IReadOnlyList<CustomObjectFieldDto> fields, IReadOnlyDictionary<string, object?>? data)
    {
        var dict = data != null
            ? new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in fields)
        {
            var hasVal = dict.TryGetValue(f.Key, out var raw);
            var isValEmpty = !hasVal || raw == null || (raw is string s && string.IsNullOrWhiteSpace(s));

            if (f.IsRequired && isValEmpty)
            {
                throw new ValidationException(f.Key, $"Field '{f.Label}' is required.");
            }

            if (!isValEmpty && raw != null)
            {
                var normType = f.Type.Trim().ToLowerInvariant();
                if (normType == "picklist")
                {
                    var str = raw.ToString() ?? "";
                    if (f.Options != null && f.Options.Count > 0 && !f.Options.Any(o => string.Equals(o, str, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new ValidationException(f.Key, $"Value '{str}' is not a valid option for '{f.Label}'. Allowed options: {string.Join(", ", f.Options)}.");
                    }
                }
                else if (normType == "number")
                {
                    if (raw is not (double or float or int or long or decimal or short or byte))
                    {
                        if (raw is JsonElement je)
                        {
                            if (je.ValueKind != JsonValueKind.Number && !(je.ValueKind == JsonValueKind.String && double.TryParse(je.GetString(), out _)))
                            {
                                throw new ValidationException(f.Key, $"Field '{f.Label}' must be a valid number.");
                            }
                        }
                        else if (!double.TryParse(raw.ToString(), out _))
                        {
                            throw new ValidationException(f.Key, $"Field '{f.Label}' must be a valid number.");
                        }
                    }
                }
                else if (normType == "boolean")
                {
                    if (raw is not bool)
                    {
                        if (raw is JsonElement je)
                        {
                            if (je.ValueKind != JsonValueKind.True && je.ValueKind != JsonValueKind.False && !(je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out _)))
                            {
                                throw new ValidationException(f.Key, $"Field '{f.Label}' must be a boolean (true/false).");
                            }
                        }
                        else if (!bool.TryParse(raw.ToString(), out _))
                        {
                            throw new ValidationException(f.Key, $"Field '{f.Label}' must be a boolean (true/false).");
                        }
                    }
                }
            }
        }
    }

    private static string ComputeDisplayName(CustomObjectDefinition def, IReadOnlyDictionary<string, object?>? data, int recordNum)
    {
        if (data != null && !string.IsNullOrWhiteSpace(def.TitleField))
        {
            var dict = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
            if (dict.TryGetValue(def.TitleField, out var titleVal) && titleVal != null)
            {
                var s = titleVal.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }
        }

        return $"{def.Name} #{recordNum}";
    }

    private static IReadOnlyList<CustomObjectFieldDto> ParseFields(string? fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CustomObjectFieldDto>>(fieldsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static CustomObjectDefinitionDto MapDefinition(CustomObjectDefinition d)
    {
        var fields = ParseFields(d.FieldsJson);
        return new CustomObjectDefinitionDto(
            d.Id,
            d.Name,
            d.PluralName,
            d.Slug,
            d.Description,
            d.Icon,
            d.TitleField,
            d.IsActive,
            fields,
            d.CreatedAt,
            d.UpdatedAt);
    }

    private static CustomObjectRecordDto MapRecord(CustomObjectRecord r, string definitionSlug)
    {
        Dictionary<string, object?> dataDict;
        try
        {
            dataDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.DataJson) ?? [];
        }
        catch
        {
            dataDict = [];
        }

        return new CustomObjectRecordDto(
            r.Id,
            r.ObjectDefinitionId,
            definitionSlug,
            r.RecordNumber,
            r.DisplayName,
            dataDict,
            r.CreatedAt,
            r.UpdatedAt,
            r.CreatedById,
            r.UpdatedById);
    }
}
