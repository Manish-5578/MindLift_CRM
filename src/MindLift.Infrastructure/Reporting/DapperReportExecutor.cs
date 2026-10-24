using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Reporting;

public interface IReportExecutor
{
    Task<ReportResult> ExecuteAsync(
        BuiltReportSql built,
        ReportQuery query,
        CancellationToken ct = default);
}

public sealed class DapperReportExecutor(AppDbContext db) : IReportExecutor
{
    public async Task<ReportResult> ExecuteAsync(
        BuiltReportSql built,
        ReportQuery query,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        // SET LOCAL only applies inside a transaction, so the report runs in one (read only, rolled back afterwards).
        await using var transaction = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("SET LOCAL statement_timeout = '15s';", transaction: transaction);
        var rawRows = (await conn.QueryAsync(built.Sql, built.Parameters, transaction)).ToList();
        await transaction.RollbackAsync(ct);
        sw.Stop();

        bool truncated = rawRows.Count > built.RequestedLimit;
        var selectedRows = truncated ? rawRows.Take(built.RequestedLimit).ToList() : rawRows;

        var columns = new List<ReportColumn>(built.Columns);
        var rows = new List<Dictionary<string, object?>>();

        foreach (var raw in selectedRows)
        {
            var dict = (IDictionary<string, object>)raw;
            var rowDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var col in columns)
            {
                if (dict.TryGetValue(col.Key, out var val))
                {
                    rowDict[col.Key] = NormalizeValue(val);
                }
                else
                {
                    rowDict[col.Key] = null;
                }
            }

            // Retain hidden _id if present for drilldown
            if (dict.TryGetValue("_id", out var idVal))
            {
                rowDict["_id"] = idVal?.ToString();
            }

            rows.Add(rowDict);
        }

        // Compute formulas
        if (query.Formulas.Count > 0)
        {
            foreach (var formula in query.Formulas)
            {
                var formulaKey = "formula_" + SanitizeKey(formula.Label);
                columns.Add(new ReportColumn
                {
                    Key = formulaKey,
                    Label = formula.Label,
                    Type = "Number",
                    Format = formula.Format ?? "decimal"
                });

                foreach (var row in rows)
                {
                    row[formulaKey] = ReportFormulaEvaluator.Evaluate(formula.Expression, row);
                }
            }
        }

        // Compute totals row for summary/matrix
        Dictionary<string, object?>? totals = null;
        if (query.Type != ReportType.Tabular && rows.Count > 0)
        {
            totals = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            bool isFirstCol = true;

            foreach (var col in columns)
            {
                if (isFirstCol)
                {
                    totals[col.Key] = "Total";
                    isFirstCol = false;
                    continue;
                }

                if (col.IsAggregate || col.Type == "Number" || col.Type == "Currency" || col.Type == "Percent")
                {
                    decimal sum = 0;
                    bool hasNumeric = false;
                    foreach (var row in rows)
                    {
                        if (row.TryGetValue(col.Key, out var val) && val != null && decimal.TryParse(val.ToString(), out var num))
                        {
                            sum += num;
                            hasNumeric = true;
                        }
                    }

                    totals[col.Key] = hasNumeric ? sum : null;
                }
                else
                {
                    totals[col.Key] = null;
                }
            }
        }

        return new ReportResult
        {
            Columns = columns,
            Rows = rows,
            Totals = totals,
            Truncated = truncated,
            RowCount = rows.Count,
            ExecutionMs = sw.ElapsedMilliseconds,
            Visualization = query.Visualization
        };
    }

    private static object? NormalizeValue(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (val is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }
        if (val is DateOnly d)
        {
            return d.ToString("yyyy-MM-dd");
        }
        return val;
    }

    private static string SanitizeKey(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());
}

public static class ReportFormulaEvaluator
{
    private static readonly Regex SafeTokensRegex = new(@"^[a-zA-Z0-9_\s\+\-\*\/\(\)\.\,]+$", RegexOptions.Compiled);

    public static decimal? Evaluate(string expression, Dictionary<string, object?> row)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        if (!SafeTokensRegex.IsMatch(expression)) return null;

        try
        {
            // Replace identifier names with their numeric values
            var expr = expression;
            foreach (var kvp in row)
            {
                if (decimal.TryParse(kvp.Value?.ToString(), out var num))
                {
                    expr = Regex.Replace(expr, $@"\b{Regex.Escape(kvp.Key)}\b", num.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    expr = Regex.Replace(expr, $@"\b{Regex.Escape(kvp.Key)}\b", "0");
                }
            }

            // Compute via DataTable compute
            var dt = new DataTable();
            var result = dt.Compute(expr, null);
            if (result != null && decimal.TryParse(result.ToString(), out var res))
            {
                var dVal = (double)res;
                if (double.IsInfinity(dVal) || double.IsNaN(dVal)) return 0;
                return Math.Round(res, 2);
            }
        }
        catch
        {
            return 0;
        }

        return null;
    }
}
