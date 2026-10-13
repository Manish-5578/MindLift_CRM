using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MindLift.Application.Common.Fields;

/// <summary>
/// Reads one key from a custom-field document inside a SQL query. Mapped to the SQL functions
/// crm_cf_text and crm_cf_numeric, which the migration creates (plan 4.6.4).
/// These methods only exist for expression trees; calling them directly throws.
/// </summary>
public static class CustomFieldFunctions
{
    [DbFunction("crm_cf_text", Schema = "public")]
    public static string? CfText(JsonDocument? custom, string key)
        => throw new NotSupportedException("Only valid inside a database query.");

    [DbFunction("crm_cf_numeric", Schema = "public")]
    public static decimal? CfNumeric(JsonDocument? custom, string key)
        => throw new NotSupportedException("Only valid inside a database query.");
}
