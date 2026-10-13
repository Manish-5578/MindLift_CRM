using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Reporting;

public sealed record ReportFieldDef(
    string Key,
    string Label,
    FieldType Type,
    string SqlExpr,
    bool Groupable = false,
    bool Aggregatable = false,
    string[]? Joins = null,
    string? SortSql = null);

public interface IReportDataSource
{
    string Key { get; }
    string Label { get; }
    string BaseTable { get; }
    string BaseAlias { get; }
    AppModule Module { get; }
    string OwnerColumn { get; }
    string DefaultDateField { get; }
    IReadOnlyDictionary<string, string> Joins { get; }
    IReadOnlyDictionary<string, string[]> JoinDependencies { get; }
    IReadOnlyDictionary<string, ReportFieldDef> Fields { get; }
}

public sealed class LeadsReportDataSource : IReportDataSource
{
    public string Key => "leads";
    public string Label => "Leads";
    public string BaseTable => "leads";
    public string BaseAlias => "l";
    public AppModule Module => AppModule.Leads;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "created_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["owner"] = "LEFT JOIN users o ON o.id = l.owner_id"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "Lead ID", FieldType.Lookup, "l.id"),
        new("first_name", "First name", FieldType.Text, "l.first_name", Groupable: true),
        new("last_name", "Last name", FieldType.Text, "l.last_name", Groupable: true),
        new("name", "Name", FieldType.Text, "concat_ws(' ', l.first_name, l.last_name)", Groupable: true),
        new("email", "Email", FieldType.Email, "l.email"),
        new("phone", "Phone", FieldType.Phone, "l.phone"),
        new("company", "Company", FieldType.Text, "l.company_name", Groupable: true),
        new("status", "Status", FieldType.Picklist, "l.status", Groupable: true),
        new("source", "Source", FieldType.Picklist, "COALESCE(l.source, 'Unknown')", Groupable: true),
        new("rating", "Rating", FieldType.Picklist, "COALESCE(l.rating, 'None')", Groupable: true),
        new("is_converted", "Converted", FieldType.Boolean, "CASE WHEN l.converted_at IS NOT NULL THEN true ELSE false END", Groupable: true),
        new("created_at", "Created date", FieldType.DateTime, "l.created_at", Groupable: true),
        new("converted_at", "Converted date", FieldType.DateTime, "l.converted_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Groupable: true, Joins: ["owner"])
    }.ToDictionary(f => f.Key);
}

public sealed class ContactsReportDataSource : IReportDataSource
{
    public string Key => "contacts";
    public string Label => "Contacts";
    public string BaseTable => "contacts";
    public string BaseAlias => "c";
    public AppModule Module => AppModule.Contacts;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "created_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["owner"] = "LEFT JOIN users o ON o.id = c.owner_id",
        ["account"] = "LEFT JOIN accounts a ON a.id = c.account_id AND a.tenant_id = c.tenant_id AND a.is_deleted = false"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "Contact ID", FieldType.Lookup, "c.id"),
        new("first_name", "First name", FieldType.Text, "c.first_name", Groupable: true),
        new("last_name", "Last name", FieldType.Text, "c.last_name", Groupable: true),
        new("name", "Name", FieldType.Text, "concat_ws(' ', c.first_name, c.last_name)", Groupable: true),
        new("email", "Email", FieldType.Email, "c.email"),
        new("phone", "Phone", FieldType.Phone, "c.phone"),
        new("title", "Job title", FieldType.Text, "COALESCE(c.job_title, 'None')", Groupable: true),
        new("account_name", "Account", FieldType.Text, "COALESCE(a.name, 'No Account')", Groupable: true, Joins: ["account"]),
        new("source", "Source", FieldType.Picklist, "COALESCE(c.type, 'Unknown')", Groupable: true),
        new("created_at", "Created date", FieldType.DateTime, "c.created_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Groupable: true, Joins: ["owner"])
    }.ToDictionary(f => f.Key);
}

public sealed class AccountsReportDataSource : IReportDataSource
{
    public string Key => "accounts";
    public string Label => "Accounts";
    public string BaseTable => "accounts";
    public string BaseAlias => "a";
    public AppModule Module => AppModule.Accounts;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "created_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["owner"] = "LEFT JOIN users o ON o.id = a.owner_id"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "Account ID", FieldType.Lookup, "a.id"),
        new("name", "Account name", FieldType.Text, "a.name", Groupable: true),
        new("domain", "Domain", FieldType.Text, "COALESCE(a.website, '')"),
        new("industry", "Industry", FieldType.Picklist, "COALESCE(a.industry, 'Other')", Groupable: true),
        new("size", "Size", FieldType.Picklist, "'Standard'", Groupable: true),
        new("created_at", "Created date", FieldType.DateTime, "a.created_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Groupable: true, Joins: ["owner"])
    }.ToDictionary(f => f.Key);
}

public sealed class DealsReportDataSource : IReportDataSource
{
    public string Key => "deals";
    public string Label => "Deals";
    public string BaseTable => "deals";
    public string BaseAlias => "d";
    public AppModule Module => AppModule.Deals;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "closed_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["owner"] = "LEFT JOIN users o ON o.id = d.owner_id",
        ["stage"] = "LEFT JOIN pipeline_stages s ON s.id = d.stage_id AND s.tenant_id = d.tenant_id",
        ["pipeline"] = "LEFT JOIN pipelines p ON p.id = d.pipeline_id AND p.tenant_id = d.tenant_id",
        ["account"] = "LEFT JOIN accounts a ON a.id = d.account_id AND a.tenant_id = d.tenant_id AND a.is_deleted = false"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "Deal ID", FieldType.Lookup, "d.id"),
        new("name", "Deal name", FieldType.Text, "d.name", Groupable: true),
        new("amount_in_base", "Amount (base)", FieldType.Currency, "COALESCE(d.amount, 0)", Aggregatable: true),
        new("probability", "Probability (%)", FieldType.Percent, "COALESCE(d.probability, 0)", Aggregatable: true),
        new("weighted_amount", "Weighted amount", FieldType.Currency, "COALESCE(d.amount * d.probability / 100.0, 0)", Aggregatable: true),
        new("status", "Status", FieldType.Picklist, "d.status", Groupable: true),
        new("stage_name", "Stage", FieldType.Text, "COALESCE(s.name, 'Unknown')", Joins: ["stage"], Groupable: true, SortSql: "s.sort_order"),
        new("pipeline_name", "Pipeline", FieldType.Text, "COALESCE(p.name, 'Default')", Joins: ["pipeline"], Groupable: true),
        new("account_name", "Account", FieldType.Text, "COALESCE(a.name, 'No Account')", Joins: ["account"], Groupable: true),
        new("source", "Source", FieldType.Picklist, "'Direct'", Groupable: true),
        new("lost_reason", "Lost reason", FieldType.Picklist, "COALESCE(d.lost_reason, 'Not Lost')", Groupable: true),
        new("expected_close_date", "Expected close", FieldType.Date, "d.expected_close_date", Groupable: true),
        new("closed_at", "Closed date", FieldType.DateTime, "d.closed_at", Groupable: true),
        new("days_to_close", "Days to close", FieldType.Number, "ROUND(EXTRACT(EPOCH FROM (COALESCE(d.closed_at, now()) - d.created_at)) / 86400.0, 1)", Aggregatable: true),
        new("created_at", "Created date", FieldType.DateTime, "d.created_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Joins: ["owner"], Groupable: true)
    }.ToDictionary(f => f.Key);
}

public sealed class ActivitiesReportDataSource : IReportDataSource
{
    public string Key => "activities";
    public string Label => "Activities";
    public string BaseTable => "activities";
    public string BaseAlias => "x";
    public AppModule Module => AppModule.Activities;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "completed_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["owner"] = "LEFT JOIN users o ON o.id = x.owner_id",
        ["deal"] = "LEFT JOIN deals d ON d.id = x.deal_id AND d.tenant_id = x.tenant_id AND d.is_deleted = false",
        ["contact"] = "LEFT JOIN contacts c ON c.id = x.contact_id AND c.tenant_id = x.tenant_id AND c.is_deleted = false",
        ["lead"] = "LEFT JOIN leads l ON l.id = x.lead_id AND l.tenant_id = x.tenant_id AND l.is_deleted = false",
        ["account"] = "LEFT JOIN accounts a ON a.id = x.account_id AND a.tenant_id = x.tenant_id AND a.is_deleted = false"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>();

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "Activity ID", FieldType.Lookup, "x.id"),
        new("type", "Type", FieldType.Picklist, "x.type", Groupable: true),
        new("subject", "Subject", FieldType.Text, "x.subject", Groupable: true),
        new("status", "Status", FieldType.Picklist, "x.status", Groupable: true),
        new("priority", "Priority", FieldType.Picklist, "x.priority", Groupable: true),
        new("due_at", "Due date", FieldType.DateTime, "x.due_at", Groupable: true),
        new("completed_at", "Completed date", FieldType.DateTime, "x.completed_at", Groupable: true),
        new("days_overdue", "Days overdue", FieldType.Number, "ROUND(GREATEST(0, EXTRACT(EPOCH FROM (now() - x.due_at)) / 86400.0), 1)", Aggregatable: true),
        new("related_name", "Related to", FieldType.Text, "COALESCE(d.name, concat_ws(' ', c.first_name, c.last_name), concat_ws(' ', l.first_name, l.last_name), a.name, 'None')", Joins: ["deal", "contact", "lead", "account"], Groupable: true),
        new("created_at", "Created date", FieldType.DateTime, "x.created_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Joins: ["owner"], Groupable: true)
    }.ToDictionary(f => f.Key);
}

public sealed class DealStageHistoryReportDataSource : IReportDataSource
{
    public string Key => "deal_stage_history";
    public string Label => "Deal Stage History";
    public string BaseTable => "deal_stage_history";
    public string BaseAlias => "h";
    public AppModule Module => AppModule.Deals;
    public string OwnerColumn => "owner_id";
    public string DefaultDateField => "moved_at";

    public IReadOnlyDictionary<string, string> Joins => new Dictionary<string, string>
    {
        ["deal"] = "LEFT JOIN deals d ON d.id = h.deal_id AND d.tenant_id = h.tenant_id AND d.is_deleted = false",
        ["from_stage"] = "LEFT JOIN pipeline_stages from_s ON from_s.id = h.from_stage_id",
        ["to_stage"] = "LEFT JOIN pipeline_stages to_s ON to_s.id = h.to_stage_id",
        ["owner"] = "LEFT JOIN users o ON o.id = d.owner_id"
    };

    public IReadOnlyDictionary<string, string[]> JoinDependencies => new Dictionary<string, string[]>
    {
        ["owner"] = ["deal"]
    };

    public IReadOnlyDictionary<string, ReportFieldDef> Fields => new List<ReportFieldDef>
    {
        new("id", "History ID", FieldType.Lookup, "h.id"),
        new("deal_id", "Deal ID", FieldType.Lookup, "h.deal_id"),
        new("deal_name", "Deal name", FieldType.Text, "COALESCE(d.name, 'Unknown')", Joins: ["deal"], Groupable: true),
        new("from_stage_name", "From Stage", FieldType.Text, "COALESCE(from_s.name, 'Start')", Joins: ["from_stage"], Groupable: true),
        new("to_stage_name", "To Stage", FieldType.Text, "COALESCE(to_s.name, 'Unknown')", Joins: ["to_stage"], Groupable: true),
        new("seconds_in_from_stage", "Seconds in stage", FieldType.Number, "COALESCE(h.seconds_in_from_stage, 0)", Aggregatable: true),
        new("days_in_from_stage", "Days in stage", FieldType.Number, "ROUND(COALESCE(h.seconds_in_from_stage, 0) / 86400.0, 1)", Aggregatable: true),
        new("moved_at", "Moved date", FieldType.DateTime, "h.moved_at", Groupable: true),
        new("owner_name", "Owner", FieldType.Text, "COALESCE(concat_ws(' ', o.first_name, o.last_name), 'Unassigned')", Joins: ["deal", "owner"], Groupable: true)
    }.ToDictionary(f => f.Key);
}

public interface IReportDataSourceRegistry
{
    IReportDataSource? Get(string key);
    IReadOnlyList<IReportDataSource> GetAll();
}

public sealed class ReportDataSourceRegistry : IReportDataSourceRegistry
{
    private readonly Dictionary<string, IReportDataSource> _sources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leads"] = new LeadsReportDataSource(),
        ["contacts"] = new ContactsReportDataSource(),
        ["accounts"] = new AccountsReportDataSource(),
        ["deals"] = new DealsReportDataSource(),
        ["activities"] = new ActivitiesReportDataSource(),
        ["deal_stage_history"] = new DealStageHistoryReportDataSource()
    };

    public IReportDataSource? Get(string key) => _sources.GetValueOrDefault(key);
    public IReadOnlyList<IReportDataSource> GetAll() => _sources.Values.ToList();
}
