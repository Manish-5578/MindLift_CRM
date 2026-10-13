namespace MindLift.Shared.Imports;

/// <summary>Header matching for any CSV import: ignores case, spaces, underscores and other punctuation.</summary>
public static class ImportColumns
{
    /// <summary>The position of each field in the header row, or -1 when the file has no such column. The first matching column wins.</summary>
    public static Dictionary<string, int> Map(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string[]> aliases)
    {
        var normalized = headers.Select(LeadImportColumns.Normalize).ToList();
        return aliases.ToDictionary(kv => kv.Key, kv => kv.Value.Select(alias => normalized.IndexOf(alias)).FirstOrDefault(index => index >= 0, -1));
    }

    /// <summary>For each column of the file: the field it fills, or null when it will be ignored.</summary>
    public static IReadOnlyList<ColumnMatch> Recognize(IReadOnlyList<string> headers, IReadOnlyList<(string Field, string Label, string[] Aliases)> fields)
    {
        var map = Map(headers, fields.ToDictionary(f => f.Field, f => f.Aliases));
        var matches = new List<ColumnMatch>();
        for (var i = 0; i < headers.Count; i++)
        {
            var field = fields.FirstOrDefault(f => map[f.Field] == i);
            matches.Add(field.Field is null ? new ColumnMatch(headers[i], null, null) : new ColumnMatch(headers[i], field.Field, field.Label));
        }

        return matches;
    }

    /// <summary>A header-only file people can download and fill in.</summary>
    public static string TemplateCsv(IReadOnlyList<(string Field, string Label, string[] Aliases)> fields) => string.Join(",", fields.Select(f => f.Label)) + "\n";
}

/// <summary>The columns the contact and account imports understand, shared by the server and the import wizard.</summary>
public static class ImportFieldSets
{
    public static IReadOnlyList<(string Field, string Label, string[] Aliases)> Contacts { get; } =
    [
        ("first_name", "First name", ["firstname", "first", "givenname"]),
        ("last_name", "Last name", ["lastname", "last", "surname", "familyname"]),
        ("email", "Email", ["email", "emailaddress", "mail"]),
        ("phone", "Phone", ["phone", "mobile", "telephone", "phonenumber"]),
        ("job_title", "Job title", ["jobtitle", "title", "position", "role"]),
        ("account", "Company", ["account", "company", "companyname", "organisation", "organization"]),
    ];

    public static IReadOnlyList<(string Field, string Label, string[] Aliases)> Deals { get; } =
    [
        ("name", "Name", ["name", "dealname", "deal"]),
        ("amount", "Amount", ["amount", "value", "dealvalue"]),
        ("currency", "Currency", ["currency"]),
        ("account", "Company", ["account", "company", "companyname"]),
        ("stage", "Stage", ["stage"]),
        ("expected_close_date", "Expected close", ["expectedclosedate", "closedate", "expectedclose"]),
    ];

    public static IReadOnlyList<(string Field, string Label, string[] Aliases)> Accounts { get; } =
    [
        ("name", "Name", ["name", "accountname", "company", "companyname"]),
        ("website", "Website", ["website", "url", "web"]),
        ("phone", "Phone", ["phone", "telephone", "mobile"]),
        ("industry", "Industry", ["industry", "sector"]),
    ];
}
