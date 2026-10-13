using System.Text.Json;
using Dapper;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Reporting;

public static class FilterSqlCompiler
{
    public static string? Compile(
        FilterGroup? group,
        IReportDataSource source,
        DynamicParameters parameters,
        ref int paramIndex,
        TimeZoneInfo timeZone)
    {
        if (group == null)
            return null;

        return CompileGroup(group, source, parameters, ref paramIndex, timeZone, 0);
    }

    private static string? CompileGroup(
        FilterGroup group,
        IReportDataSource source,
        DynamicParameters parameters,
        ref int paramIndex,
        TimeZoneInfo timeZone,
        int depth)
    {
        if (depth > FilterGroup.MaxDepth)
            throw new ValidationException($"Filter nesting exceeds max depth of {FilterGroup.MaxDepth}.");

        var parts = new List<string>();

        foreach (var cond in group.Conditions)
        {
            if (string.IsNullOrWhiteSpace(cond.Field))
                continue;

            if (!source.Fields.TryGetValue(cond.Field, out var fieldDef))
                throw new ValidationException($"Unknown field: {cond.Field} in data source {source.Key}.");

            var expr = fieldDef.SqlExpr;
            var pName = $"p{paramIndex++}";

            switch (cond.Op)
            {
                case FilterOperator.Eq:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} = @{pName}");
                    break;

                case FilterOperator.Neq:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} <> @{pName}");
                    break;

                case FilterOperator.Contains:
                    parameters.Add(pName, $"%{ExtractJsonScalar(cond.Value)}%");
                    parts.Add($"{expr} ILIKE @{pName}");
                    break;

                case FilterOperator.NotContains:
                    parameters.Add(pName, $"%{ExtractJsonScalar(cond.Value)}%");
                    parts.Add($"{expr} NOT ILIKE @{pName}");
                    break;

                case FilterOperator.StartsWith:
                    parameters.Add(pName, $"{ExtractJsonScalar(cond.Value)}%");
                    parts.Add($"{expr} ILIKE @{pName}");
                    break;

                case FilterOperator.IsEmpty:
                    parts.Add($"({expr} IS NULL OR {expr} = '')");
                    break;

                case FilterOperator.IsNotEmpty:
                    parts.Add($"({expr} IS NOT NULL AND {expr} <> '')");
                    break;

                case FilterOperator.Gt:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} > @{pName}");
                    break;

                case FilterOperator.Gte:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} >= @{pName}");
                    break;

                case FilterOperator.Lt:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} < @{pName}");
                    break;

                case FilterOperator.Lte:
                    parameters.Add(pName, ExtractJsonScalar(cond.Value));
                    parts.Add($"{expr} <= @{pName}");
                    break;

                case FilterOperator.In:
                    var arrayValues = ExtractJsonArray(cond.Value);
                    parameters.Add(pName, arrayValues);
                    parts.Add($"{expr} = ANY(@{pName})");
                    break;

                case FilterOperator.NotIn:
                    var notInValues = ExtractJsonArray(cond.Value);
                    parameters.Add(pName, notInValues);
                    parts.Add($"{expr} <> ALL(@{pName})");
                    break;

                case FilterOperator.Between:
                    var (fromVal, toVal) = ExtractBetween(cond.Value);
                    var pFrom = $"p{paramIndex++}";
                    var pTo = $"p{paramIndex++}";
                    parameters.Add(pFrom, fromVal);
                    parameters.Add(pTo, toVal);
                    parts.Add($"({expr} >= @{pFrom} AND {expr} <= @{pTo})");
                    break;

                case FilterOperator.DatePreset:
                    var presetName = cond.Value?.GetString() ?? "ThisMonth";
                    if (Enum.TryParse<DatePreset>(presetName, true, out var datePreset))
                    {
                        var range = DateRangeResolver.Resolve(datePreset, DateTime.UtcNow, timeZone);
                        var pDateFrom = $"p{paramIndex++}";
                        var pDateTo = $"p{paramIndex++}";
                        parameters.Add(pDateFrom, range.StartUtc);
                        parameters.Add(pDateTo, range.EndUtc);
                        parts.Add($"({expr} >= @{pDateFrom} AND {expr} < @{pDateTo})");
                    }
                    break;
            }
        }

        foreach (var subGroup in group.Groups)
        {
            var subCompiled = CompileGroup(subGroup, source, parameters, ref paramIndex, timeZone, depth + 1);
            if (!string.IsNullOrWhiteSpace(subCompiled))
            {
                parts.Add(subCompiled);
            }
        }

        if (parts.Count == 0)
            return null;

        var op = group.Logic.Equals("or", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
        return $"({string.Join(op, parts)})";
    }

    private static object? ExtractJsonScalar(JsonElement? element)
    {
        if (!element.HasValue) return null;
        var el = element.Value;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        };
    }

    private static string[] ExtractJsonArray(JsonElement? element)
    {
        if (!element.HasValue) return [];
        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray().Select(x => x.GetString() ?? x.ToString()).ToArray();
        }
        return [el.GetString() ?? el.ToString()];
    }

    private static (object? From, object? To) ExtractBetween(JsonElement? element)
    {
        if (!element.HasValue) return (null, null);
        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Object)
        {
            var from = el.TryGetProperty("from", out var f) ? ExtractJsonScalar(f) : null;
            var to = el.TryGetProperty("to", out var t) ? ExtractJsonScalar(t) : null;
            return (from, to);
        }
        return (null, null);
    }
}
