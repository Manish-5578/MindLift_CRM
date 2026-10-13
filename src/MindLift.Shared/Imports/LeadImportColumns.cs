namespace MindLift.Shared.Imports;

/// <summary>How one column of an uploaded file is read.</summary>
public sealed record ColumnMatch(string Header, string? Field, string? FieldLabel);

/// <summary>
/// Which columns of a lead CSV the import understands. The server uses it to read the file and the import wizard uses it to
/// show the user, before importing, how each of their columns will be read.
/// Headers are matched ignoring case, spaces, underscores and other punctuation: "First name", "first_name" and "FIRSTNAME" are the same.
/// </summary>
public static class LeadImportColumns
{
    /// <summary>The fields the import fills, with the label shown to people and the header names (normalized) that map to each.</summary>
    public static IReadOnlyList<(string Field, string Label, string[] Aliases)> Fields { get; } =
    [
        ("first_name", "First name", ["firstname"]),
        ("last_name", "Last name", ["lastname"]),
        ("email", "Email", ["email"]),
        ("phone", "Phone", ["phone"]),
        ("company_name", "Company", ["company", "companyname"]),
        ("source", "Source", ["source"]),
        ("description", "Notes", ["description", "notes"]),
    ];

    /// <summary>The only field a row cannot be created without.</summary>
    public const string RequiredField = "first_name";

    /// <summary>Lower-cases a header and keeps only letters and digits.</summary>
    public static string Normalize(string header)
        => new((header ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The position of each field in the header row, or -1 when the file has no such column. The first matching column wins.</summary>
    public static Dictionary<string, int> Map(IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(Normalize).ToList();
        var result = new Dictionary<string, int>();
        foreach (var (field, _, aliases) in Fields)
        {
            result[field] = aliases.Select(alias => normalized.IndexOf(alias)).FirstOrDefault(index => index >= 0, -1);
        }

        return result;
    }

    /// <summary>For each column of the file: the field it fills, or null when it will be ignored. A field is only filled by its first column.</summary>
    public static IReadOnlyList<ColumnMatch> Recognize(IReadOnlyList<string> headers)
    {
        var map = Map(headers);
        var matches = new List<ColumnMatch>();
        for (var i = 0; i < headers.Count; i++)
        {
            var field = Fields.FirstOrDefault(f => map[f.Field] == i);
            matches.Add(field.Field is null ? new ColumnMatch(headers[i], null, null) : new ColumnMatch(headers[i], field.Field, field.Label));
        }

        return matches;
    }

    /// <summary>A header-only file people can download and fill in.</summary>
    public static string TemplateCsv => string.Join(",", Fields.Select(f => f.Label)) + "\n";
}
