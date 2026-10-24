namespace MindLift.Shared.Enums;

/// <summary>One feature of the product as the platform owner sees it: a module that can be switched on or off for each company.</summary>
/// <param name="Icon">The name of the picture shown with the module (the screens map it to an icon).</param>
/// <param name="Core">True for the modules every company needs to run its own workspace. They can never be switched off.</param>
/// <param name="Available">False for modules planned but not built yet. They are listed so the roadmap is visible, and cannot be switched on.</param>
public sealed record ModuleInfo(AppModule Module, string Name, string Description, string Category, string Icon, bool Core, bool Available);

/// <summary>A ready-made choice of modules to apply to a company in one click.</summary>
public sealed record ModulePreset(string Key, string Name, string Description, IReadOnlyList<AppModule> Modules);

/// <summary>
/// Every feature of the product as a module (plan 4.4, 6.10): what it is, which group it belongs to, whether it is a core module or one still on the roadmap.
/// The platform owner switches non-core modules on or off for each company in the platform console; a module that is off is hidden from that company and refused by the API.
/// </summary>
public static class ModuleCatalog
{
    public const string Core = "Core";
    public const string Sales = "Sales";
    public const string Finance = "Quotes & billing";
    public const string Engagement = "Engagement";
    public const string Intelligence = "Intelligence";
    public const string Channels = "Customer portal";
    public const string Roadmap = "On the roadmap";

    /// <summary>The groups in the order they are shown.</summary>
    public static readonly IReadOnlyList<string> Categories = [Core, Sales, Finance, Engagement, Intelligence, Channels, Roadmap];

    private static ModuleInfo M(AppModule module, string name, string description, string category, string icon, bool core = false, bool available = true)
        => new(module, name, description, category, icon, core, available);

    public static readonly IReadOnlyList<ModuleInfo> All =
    [
        M(AppModule.Dashboard, "Dashboard", "Live numbers, pipeline snapshot and today's work for each person.", Core, "dashboard", core: true),
        M(AppModule.Users, "Users & roles", "Invite teammates, set roles, teams and branches.", Core, "people", core: true),
        M(AppModule.Settings, "Settings", "Company profile, fields, pipelines, picklists and set-up.", Core, "settings", core: true),
        M(AppModule.Notifications, "Notifications", "In-app and email alerts, reminders and preferences.", Core, "bell"),

        M(AppModule.Leads, "Leads", "Capture from forms and imports, score, assign and convert.", Sales, "leads"),
        M(AppModule.Contacts, "Contacts", "The people a company sells to, with their full history.", Sales, "contacts"),
        M(AppModule.Accounts, "Accounts", "Companies and their hierarchy, contacts and deals.", Sales, "accounts"),
        M(AppModule.Deals, "Deals & pipeline", "Boards, stages, forecasting and win or loss reasons.", Sales, "deals"),

        M(AppModule.Products, "Products", "A catalogue of products and prices.", Finance, "products"),
        M(AppModule.Quotations, "Quotations", "Quotes with tax and discounts, as PDF, with approvals.", Finance, "quotes"),
        M(AppModule.Invoices, "Invoices", "Invoices, payments and overdue tracking.", Finance, "invoices"),

        M(AppModule.Activities, "Activities", "Tasks, calls, notes and meetings, linked to records.", Engagement, "activities"),
        M(AppModule.Calendar, "Calendar & booking", "Team calendar, public booking pages and meeting invitations.", Engagement, "calendar"),
        M(AppModule.Communication, "Unified inbox", "Email, WhatsApp and SMS conversations in one place.", Engagement, "inbox"),
        M(AppModule.Automation, "Automation", "Rules that act on events: tasks, updates, emails and webhooks.", Engagement, "automation"),

        M(AppModule.Reports, "Reports & analytics", "Reports, charts, targets and scheduled delivery.", Intelligence, "reports"),
        M(AppModule.Documents, "Documents & smart OCR", "Read documents, define the fields to take from them, review and approve.", Intelligence, "documents"),

        M(AppModule.Portal, "Customer portal", "A branded portal where a company's own customers send and receive documents.", Channels, "portal"),

        M(AppModule.Tickets, "Support tickets", "A help desk: requests, owners, answer times and replies by email.", Engagement, "tickets"),
        M(AppModule.Projects, "Projects", "Delivery projects, milestones and time.", Roadmap, "projects", available: false),
        M(AppModule.Campaigns, "Campaigns", "Email campaigns to a chosen audience, with opens, clicks and unsubscribes.", Engagement, "campaigns"),
        M(AppModule.Integrations, "Integrations", "Connectors and a public API.", Roadmap, "integrations", available: false),
    ];

    private static readonly Dictionary<AppModule, ModuleInfo> ByModule = All.ToDictionary(m => m.Module);

    public static ModuleInfo? Find(AppModule module) => ByModule.GetValueOrDefault(module);

    /// <summary>Modules that can be switched on or off for a company: built and not core.</summary>
    public static IReadOnlyList<ModuleInfo> Switchable => All.Where(m => m.Available && !m.Core).ToList();

    /// <summary>Modules a company can use: everything built.</summary>
    public static IReadOnlyList<ModuleInfo> Offered => All.Where(m => m.Available).ToList();

    private static IReadOnlyList<AppModule> Of(params AppModule[] extra) => All.Where(m => m.Core && m.Available).Select(m => m.Module).Concat(extra).Distinct().ToList();

    public static readonly IReadOnlyList<ModulePreset> Presets =
    [
        new("full", "Full suite", "Every module that is built.", Offered.Select(m => m.Module).ToList()),
        new("sales", "Sales CRM", "Leads to deals, with activities, calendar and reports.",
            Of(AppModule.Notifications, AppModule.Leads, AppModule.Contacts, AppModule.Accounts, AppModule.Deals, AppModule.Activities, AppModule.Calendar, AppModule.Reports)),
        new("sales-billing", "Sales & billing", "Sales CRM plus products, quotations and invoices.",
            Of(AppModule.Notifications, AppModule.Leads, AppModule.Contacts, AppModule.Accounts, AppModule.Deals, AppModule.Activities, AppModule.Calendar, AppModule.Reports,
                AppModule.Products, AppModule.Quotations, AppModule.Invoices)),
        new("documents", "Document operations", "Contacts and accounts, smart document reading and the customer portal.",
            Of(AppModule.Notifications, AppModule.Contacts, AppModule.Accounts, AppModule.Activities, AppModule.Documents, AppModule.Portal)),
        new("minimal", "Minimal", "Contacts and activities only.", Of(AppModule.Contacts, AppModule.Activities)),
    ];
}
