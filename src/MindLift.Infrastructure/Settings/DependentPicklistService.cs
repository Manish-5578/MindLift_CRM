using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Service for managing and evaluating controlling and dependent picklist relationships.
/// </summary>
public sealed class DependentPicklistService(
    AppDbContext db,
    ITenantContext tenant,
    SettingsVersion version) : IDependentPicklistService
{
    public async Task<IReadOnlyList<DependentPicklistMapDto>> ListAsync(string? entityType = null, CancellationToken cancellationToken = default)
    {
        var query = db.DependentPicklistMaps.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var norm = entityType.Trim().ToLowerInvariant();
            query = query.Where(x => x.EntityType == norm || x.EntityType == "");
        }

        var rows = await query.OrderBy(x => x.EntityType).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<DependentPicklistMapDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.DependentPicklistMaps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Dependent picklist map", id);
        return Map(row);
    }

    public async Task<DependentPicklistMapDto> CreateAsync(SaveDependentPicklistMapRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var normEntity = request.EntityType.Trim().ToLowerInvariant();
        var normParent = request.ParentField.Trim().ToLowerInvariant();
        var normChild = request.ChildField.Trim().ToLowerInvariant();

        if (await db.DependentPicklistMaps.AnyAsync(x =>
            x.EntityType == normEntity &&
            x.ParentField == normParent &&
            x.ChildField == normChild, cancellationToken))
        {
            throw new ConflictException("dependent_picklist_exists", $"A dependency mapping from '{normParent}' to '{normChild}' already exists for entity '{normEntity}'.");
        }

        var row = new DependentPicklistMap
        {
            EntityType = normEntity,
            ParentField = normParent,
            ChildField = normChild,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            MappingJson = JsonSerializer.Serialize(request.Mappings ?? new Dictionary<string, IReadOnlyList<string>>()),
        };

        db.DependentPicklistMaps.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return Map(row);
    }

    public async Task<DependentPicklistMapDto> UpdateAsync(Guid id, SaveDependentPicklistMapRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var row = await db.DependentPicklistMaps.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Dependent picklist map", id);

        var normEntity = request.EntityType.Trim().ToLowerInvariant();
        var normParent = request.ParentField.Trim().ToLowerInvariant();
        var normChild = request.ChildField.Trim().ToLowerInvariant();

        if (await db.DependentPicklistMaps.AnyAsync(x =>
            x.Id != id &&
            x.EntityType == normEntity &&
            x.ParentField == normParent &&
            x.ChildField == normChild, cancellationToken))
        {
            throw new ConflictException("dependent_picklist_exists", $"A dependency mapping from '{normParent}' to '{normChild}' already exists for entity '{normEntity}'.");
        }

        row.EntityType = normEntity;
        row.ParentField = normParent;
        row.ChildField = normChild;
        row.Name = request.Name.Trim();
        row.Description = request.Description?.Trim();
        row.IsActive = request.IsActive;
        row.MappingJson = JsonSerializer.Serialize(request.Mappings ?? new Dictionary<string, IReadOnlyList<string>>());

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return Map(row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.DependentPicklistMaps.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Dependent picklist map", id);

        db.DependentPicklistMaps.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<AllowedPicklistValuesDto> GetAllowedValuesAsync(
        string entityType,
        string parentField,
        string childField,
        string? parentValue,
        CancellationToken cancellationToken = default)
    {
        var normEntity = entityType.Trim().ToLowerInvariant();
        var normParent = parentField.Trim().ToLowerInvariant();
        var normChild = childField.Trim().ToLowerInvariant();

        var map = await db.DependentPicklistMaps.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.IsActive &&
                (x.EntityType == normEntity || x.EntityType == "") &&
                x.ParentField == normParent &&
                x.ChildField == normChild, cancellationToken);

        // Fetch known options for the child picklist key if configured in PicklistValues
        var knownOptions = await db.PicklistValues.AsNoTracking()
            .Where(p => p.PicklistKey == normChild && p.IsActive)
            .OrderBy(p => p.SortOrder)
            .ToListAsync(cancellationToken);

        // If no dependency map is defined, all child values are allowed
        if (map == null)
        {
            var allValues = knownOptions.Select(p => p.Value).ToList();
            var allDtoOptions = knownOptions.Select(MapPicklistValue).ToList();
            return new AllowedPicklistValuesDto(normChild, parentValue, allValues, allDtoOptions);
        }

        // If parent value is empty, no dependent values are allowed
        if (string.IsNullOrWhiteSpace(parentValue))
        {
            return new AllowedPicklistValuesDto(normChild, parentValue, [], []);
        }

        var mappings = DeserializeMappings(map.MappingJson);
        var normParentVal = parentValue.Trim().ToLowerInvariant();

        // Match parent value case-insensitively
        var matchingKey = mappings.Keys.FirstOrDefault(k => string.Equals(k, normParentVal, StringComparison.OrdinalIgnoreCase));
        if (matchingKey == null || !mappings.TryGetValue(matchingKey, out var allowedCodes) || allowedCodes == null)
        {
            return new AllowedPicklistValuesDto(normChild, parentValue, [], []);
        }

        var optionsMap = knownOptions.ToDictionary(k => k.Value, StringComparer.OrdinalIgnoreCase);
        var resolvedOptions = allowedCodes.Select(code =>
        {
            if (optionsMap.TryGetValue(code, out var existing))
            {
                return MapPicklistValue(existing);
            }
            return new PicklistValueDto(Guid.NewGuid(), code, code, null, null, null, 0, false, true, false);
        }).ToList();

        return new AllowedPicklistValuesDto(normChild, parentValue, allowedCodes, resolvedOptions);
    }

    public async Task<DependentPicklistValidationResult> ValidateAsync(
        string entityType,
        IReadOnlyDictionary<string, string?> fieldValues,
        CancellationToken cancellationToken = default)
    {
        var normEntity = entityType.Trim().ToLowerInvariant();
        var errors = new List<string>();

        if (fieldValues == null || fieldValues.Count == 0)
        {
            return new DependentPicklistValidationResult(true, errors);
        }

        // Standardize input keys to lower-case
        var normFields = fieldValues.ToDictionary(k => k.Key.Trim().ToLowerInvariant(), v => v.Value?.Trim());

        var maps = await db.DependentPicklistMaps.AsNoTracking()
            .Where(x => x.IsActive && (x.EntityType == normEntity || x.EntityType == ""))
            .ToListAsync(cancellationToken);

        foreach (var map in maps)
        {
            if (normFields.TryGetValue(map.ParentField, out var parentVal) &&
                normFields.TryGetValue(map.ChildField, out var childVal))
            {
                // Only validate if child has a value set
                if (!string.IsNullOrWhiteSpace(childVal))
                {
                    if (string.IsNullOrWhiteSpace(parentVal))
                    {
                        errors.Add($"Field '{map.ChildField}' cannot be selected without choosing '{map.ParentField}' first.");
                        continue;
                    }

                    var mappings = DeserializeMappings(map.MappingJson);
                    var matchingKey = mappings.Keys.FirstOrDefault(k => string.Equals(k, parentVal, StringComparison.OrdinalIgnoreCase));

                    if (matchingKey == null || !mappings.TryGetValue(matchingKey, out var allowedList) || allowedList == null)
                    {
                        errors.Add($"No valid options exist for '{map.ChildField}' when '{map.ParentField}' is '{parentVal}'.");
                        continue;
                    }

                    if (!allowedList.Any(a => string.Equals(a, childVal, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"Value '{childVal}' is not valid for '{map.ChildField}' when '{map.ParentField}' is '{parentVal}'.");
                    }
                }
            }
        }

        return new DependentPicklistValidationResult(errors.Count == 0, errors);
    }

    private static void ValidateRequest(SaveDependentPicklistMapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("Name is required.");
        if (string.IsNullOrWhiteSpace(request.ParentField))
            throw new ValidationException("ParentField is required.");
        if (string.IsNullOrWhiteSpace(request.ChildField))
            throw new ValidationException("ChildField is required.");

        if (string.Equals(request.ParentField.Trim(), request.ChildField.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("ParentField and ChildField cannot be the same field.");
        }
    }

    private static DependentPicklistMapDto Map(DependentPicklistMap m)
        => new(
            m.Id,
            m.EntityType,
            m.ParentField,
            m.ChildField,
            m.Name,
            m.Description,
            m.IsActive,
            DeserializeMappings(m.MappingJson));

    private static PicklistValueDto MapPicklistValue(PicklistValue p)
        => new(p.Id, p.Value, p.Label, p.LabelAr, p.Category, p.Color, p.SortOrder, p.IsDefault, p.IsActive, p.IsSystem);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> DeserializeMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (parsed == null)
            {
                return new Dictionary<string, IReadOnlyList<string>>();
            }

            return parsed.ToDictionary(
                k => k.Key,
                v => (IReadOnlyList<string>)(v.Value ?? []));
        }
        catch
        {
            return new Dictionary<string, IReadOnlyList<string>>();
        }
    }
}
