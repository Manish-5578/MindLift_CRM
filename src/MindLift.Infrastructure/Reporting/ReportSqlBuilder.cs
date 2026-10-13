using System.Text;
using Dapper;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Reporting;

public sealed record BuiltReportSql(
    string Sql,
    DynamicParameters Parameters,
    List<ReportColumn> Columns,
    int RequestedLimit);

public sealed class ReportSqlBuilder(IReportDataSourceRegistry registry)
{
    public BuiltReportSql Build(
        ReportQuery query,
        Guid tenantId,
        IReadOnlyList<Guid>? scopeUserIds,
        TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(query.Source))
            throw new ValidationException("Data source is required.");

        var source = registry.Get(query.Source)
            ?? throw new ValidationException($"Unknown data source: '{query.Source}'.");

        var parameters = new DynamicParameters();
        parameters.Add("tenantId", tenantId);
        parameters.Add("tz", timeZone.Id);

        var neededJoins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectColumns = new List<ReportColumn>();
        var selectExpressions = new List<string>();
        var groupByExpressions = new List<string>();

        // 1. SELECT & Columns
        if (query.Type == ReportType.Tabular)
        {
            // Always include base ID for drill-down
            selectExpressions.Add($"{source.BaseAlias}.id AS \"_id\"");

            var cols = query.Columns.Count > 0
                ? query.Columns
                : source.Fields.Values.Take(8).Select(f => f.Key).ToList();

            if (cols.Count > 20)
                throw new ValidationException("Maximum 20 columns allowed in a report.");

            foreach (var colKey in cols)
            {
                if (!source.Fields.TryGetValue(colKey, out var field))
                    throw new ValidationException($"Unknown column field: '{colKey}' in source '{source.Key}'.");

                CollectJoins(field, neededJoins, source);
                selectExpressions.Add($"{field.SqlExpr} AS \"{field.Key}\"");
                selectColumns.Add(new ReportColumn
                {
                    Key = field.Key,
                    Label = field.Label,
                    Type = field.Type.ToString(),
                    Format = field.Type == FieldType.Currency ? "currency" : null
                });
            }
        }
        else // Summary or Matrix
        {
            if (query.GroupBy.Count > 3)
                throw new ValidationException("Maximum 3 group by fields allowed.");

            int groupIndex = 1;
            foreach (var grp in query.GroupBy)
            {
                if (!source.Fields.TryGetValue(grp.Field, out var field))
                    throw new ValidationException($"Unknown group field: '{grp.Field}' in source '{source.Key}'.");

                if (!field.Groupable)
                    throw new ValidationException($"Field '{grp.Field}' is not groupable.");

                CollectJoins(field, neededJoins, source);

                string expr;
                string colType = field.Type.ToString();
                if (grp.DateGrouping.HasValue)
                {
                    if (field.Type != FieldType.Date && field.Type != FieldType.DateTime)
                        throw new ValidationException($"Date grouping cannot be applied to non-date field '{grp.Field}'.");

                    var unit = grp.DateGrouping.Value switch
                    {
                        DateGrouping.Day => "day",
                        DateGrouping.Week => "week",
                        DateGrouping.Month => "month",
                        DateGrouping.Quarter => "quarter",
                        DateGrouping.Year => "year",
                        _ => "month"
                    };

                    expr = $"date_trunc('{unit}', ({field.SqlExpr} AT TIME ZONE @tz))";
                    colType = "Date";
                }
                else
                {
                    expr = field.SqlExpr;
                }

                selectExpressions.Add($"{expr} AS \"{field.Key}\"");
                groupByExpressions.Add(expr);

                selectColumns.Add(new ReportColumn
                {
                    Key = field.Key,
                    Label = field.Label,
                    Type = colType,
                    IsGroup = true
                });

                groupIndex++;
            }

            // Aggregates
            var aggregates = query.Aggregates.Count > 0
                ? query.Aggregates
                : [new ReportAggregate(source.Fields.Keys.First(), AggregateFunc.Count, "count")];

            if (aggregates.Count > 10)
                throw new ValidationException("Maximum 10 aggregates allowed.");

            int aggIdx = 1;
            foreach (var agg in aggregates)
            {
                var alias = !string.IsNullOrWhiteSpace(agg.Alias) ? agg.Alias : $"agg_{aggIdx++}";
                alias = SanitizeIdentifier(alias);

                if (agg.Func == AggregateFunc.Count && string.IsNullOrWhiteSpace(agg.Field))
                {
                    selectExpressions.Add($"COUNT(*) AS \"{alias}\"");
                    selectColumns.Add(new ReportColumn
                    {
                        Key = alias,
                        Label = "Count",
                        Type = "Number",
                        IsAggregate = true
                    });
                    continue;
                }

                if (!source.Fields.TryGetValue(agg.Field, out var field))
                    throw new ValidationException($"Unknown aggregate field: '{agg.Field}' in source '{source.Key}'.");

                if (agg.Func != AggregateFunc.Count && agg.Func != AggregateFunc.CountDistinct && !field.Aggregatable)
                    throw new ValidationException($"Field '{agg.Field}' is not aggregatable for {agg.Func}.");

                CollectJoins(field, neededJoins, source);

                string aggExpr = agg.Func switch
                {
                    AggregateFunc.Count => $"COUNT({field.SqlExpr})",
                    AggregateFunc.CountDistinct => $"COUNT(DISTINCT {field.SqlExpr})",
                    AggregateFunc.Sum => $"COALESCE(SUM({field.SqlExpr}), 0)",
                    AggregateFunc.Avg => $"COALESCE(ROUND(AVG({field.SqlExpr}), 2), 0)",
                    AggregateFunc.Min => $"MIN({field.SqlExpr})",
                    AggregateFunc.Max => $"MAX({field.SqlExpr})",
                    _ => $"COUNT(*)"
                };

                selectExpressions.Add($"{aggExpr} AS \"{alias}\"");
                selectColumns.Add(new ReportColumn
                {
                    Key = alias,
                    Label = $"{agg.Func} of {field.Label}",
                    Type = agg.Func == AggregateFunc.Count || agg.Func == AggregateFunc.CountDistinct ? "Number" : field.Type.ToString(),
                    Format = field.Type == FieldType.Currency ? "currency" : null,
                    IsAggregate = true
                });
            }
        }

        // 2. FROM & JOINS
        var sb = new StringBuilder();
        sb.Append($"SELECT {string.Join(", ", selectExpressions)} ");
        sb.Append($"FROM {source.BaseTable} {source.BaseAlias} ");

        var orderedJoins = OrderJoins(neededJoins, source);
        foreach (var joinKey in orderedJoins)
        {
            if (source.Joins.TryGetValue(joinKey, out var joinClause))
            {
                sb.Append($"{joinClause} ");
            }
        }

        // 3. WHERE
        sb.Append($"WHERE {source.BaseAlias}.tenant_id = @tenantId AND {source.BaseAlias}.is_deleted = false ");

        // Scope filter
        if (scopeUserIds != null)
        {
            if (scopeUserIds.Count > 0)
            {
                parameters.Add("scopeUserIds", scopeUserIds.ToArray());
                sb.Append($"AND {source.BaseAlias}.{source.OwnerColumn} = ANY(@scopeUserIds) ");
            }
            else
            {
                sb.Append("AND 1 = 0 ");
            }
        }

        int paramIndex = 1;

        // Date Filter
        var dateFilter = query.DateFilter;
        if (dateFilter != null && !string.IsNullOrWhiteSpace(dateFilter.Field))
        {
            if (!source.Fields.TryGetValue(dateFilter.Field, out var dateField))
                throw new ValidationException($"Unknown date filter field: '{dateFilter.Field}'.");

            CollectJoins(dateField, neededJoins, source);

            DateTime? from = dateFilter.From;
            DateTime? to = dateFilter.To;

            if (!string.IsNullOrWhiteSpace(dateFilter.Preset) && Enum.TryParse<DatePreset>(dateFilter.Preset, true, out var preset))
            {
                var resolved = DateRangeResolver.Resolve(preset, DateTime.UtcNow, timeZone);
                from = resolved.StartUtc;
                to = resolved.EndUtc;
            }

            if (from.HasValue)
            {
                parameters.Add("dateFrom", from.Value);
                sb.Append($"AND {dateField.SqlExpr} >= @dateFrom ");
            }
            if (to.HasValue)
            {
                parameters.Add("dateTo", to.Value);
                sb.Append($"AND {dateField.SqlExpr} < @dateTo ");
            }
        }

        // Custom FilterGroup
        var filterSql = FilterSqlCompiler.Compile(query.Filter, source, parameters, ref paramIndex, timeZone);
        if (!string.IsNullOrWhiteSpace(filterSql))
        {
            sb.Append($"AND {filterSql} ");
        }

        // 4. GROUP BY
        if (groupByExpressions.Count > 0)
        {
            sb.Append($"GROUP BY {string.Join(", ", groupByExpressions)} ");
        }

        // 5. ORDER BY
        if (query.Sort.Count > 0)
        {
            var sortClauses = new List<string>();
            foreach (var s in query.Sort)
            {
                if (source.Fields.TryGetValue(s.Field, out var sField))
                {
                    var dir = s.Descending ? "DESC" : "ASC";
                    var sortExpr = sField.SortSql ?? sField.SqlExpr;
                    sortClauses.Add($"{sortExpr} {dir}");
                }
                else if (selectColumns.Any(c => c.Key.Equals(s.Field, StringComparison.OrdinalIgnoreCase)))
                {
                    var dir = s.Descending ? "DESC" : "ASC";
                    sortClauses.Add($"\"{SanitizeIdentifier(s.Field)}\" {dir}");
                }
            }
            if (sortClauses.Count > 0)
            {
                sb.Append($"ORDER BY {string.Join(", ", sortClauses)} ");
            }
        }
        else if (groupByExpressions.Count > 0)
        {
            sb.Append($"ORDER BY 1 ASC ");
        }
        else
        {
            sb.Append($"ORDER BY {source.BaseAlias}.created_at DESC ");
        }

        // 6. LIMIT
        int limit = query.Limit.HasValue && query.Limit.Value > 0
            ? Math.Min(query.Limit.Value, 50000)
            : 1000;

        parameters.Add("limitPlusOne", limit + 1);
        sb.Append("LIMIT @limitPlusOne");

        return new BuiltReportSql(sb.ToString(), parameters, selectColumns, limit);
    }

    private static void CollectJoins(ReportFieldDef field, HashSet<string> neededJoins, IReportDataSource source)
    {
        if (field.Joins != null)
        {
            foreach (var j in field.Joins)
            {
                neededJoins.Add(j);
                if (source.JoinDependencies.TryGetValue(j, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        neededJoins.Add(dep);
                    }
                }
            }
        }
    }

    private static List<string> OrderJoins(HashSet<string> neededJoins, IReportDataSource source)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string j)
        {
            if (visited.Contains(j)) return;
            visited.Add(j);

            if (source.JoinDependencies.TryGetValue(j, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (neededJoins.Contains(dep))
                        Visit(dep);
                }
            }
            result.Add(j);
        }

        foreach (var j in neededJoins)
        {
            Visit(j);
        }

        return result;
    }

    private static string SanitizeIdentifier(string id)
    {
        var cleaned = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "col" : cleaned;
    }
}
