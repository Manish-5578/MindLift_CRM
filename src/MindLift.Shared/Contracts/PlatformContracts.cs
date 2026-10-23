namespace MindLift.Shared.Contracts;

// The platform console (plan 6.10): what the owner of the product sees of every company, and what the owner can change for each.

/// <summary>A labelled count, for charts: a day, a status or a plan.</summary>
public sealed record PlatformPoint(string Label, int Value);

public sealed record PlatformSlice(string Key, string Label, int Value);

/// <summary>Something about a company that needs the owner's eye. <c>Severity</c> is "info", "warning" or "critical".</summary>
public sealed record PlatformAttentionItem(Guid TenantId, string Company, string Kind, string Message, string Severity);

/// <summary>How many companies have a module available to them.</summary>
public sealed record PlatformModuleAdoption(string Module, string Name, int Companies, int Total);

/// <summary>One company as the owner sees it in a list: who they are, what they pay for, how much they use the product.</summary>
public sealed record PlatformCompanyDto(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string PlanKey,
    string PlanName,
    int Users,
    int ActiveUsers7d,
    int Leads,
    int Contacts,
    int Accounts,
    int Deals,
    int Documents,
    int ModulesEnabled,
    int ModulesTotal,
    bool AiAllowed,
    bool AiEnabled,
    DateTime? TrialEndsAt,
    DateTime CreatedAt,
    DateTime? LastActiveAt,
    string Country,
    string? Industry,
    int Health,
    IReadOnlyList<string> DisabledModules);

/// <summary>A change the owner made, or something that happened to a company, for the history.</summary>
public sealed record PlatformAuditDto(Guid Id, Guid? TenantId, string? Company, string Actor, string Action, string Summary, DateTime At);

public sealed record PlatformOverviewDto(
    int Companies,
    int Active,
    int Trial,
    int TrialEndingSoon,
    int PastDue,
    int Suspended,
    int Cancelled,
    int Users,
    int ActiveUsers7d,
    int SignupsLast30,
    int SignupsPrevious30,
    int Documents,
    int Records,
    int AiCompanies,
    IReadOnlyList<PlatformPoint> Signups,
    IReadOnlyList<PlatformSlice> ByStatus,
    IReadOnlyList<PlatformSlice> ByPlan,
    IReadOnlyList<PlatformCompanyDto> RecentCompanies,
    IReadOnlyList<PlatformAttentionItem> Attention,
    IReadOnlyList<PlatformModuleAdoption> ModuleAdoption,
    IReadOnlyList<PlatformAuditDto> Activity,
    decimal MonthlyRevenue);

/// <summary>
/// One company in full. <c>DisabledModules</c> are the modules the platform owner switched off; <c>TenantDisabledModules</c> are those the company's own
/// administrator switched off in its Settings (shown so the owner can tell the two apart).
/// </summary>
public sealed record PlatformCompanyDetailDto(
    PlatformCompanyDto Company,
    int MaxUsers,
    int PlanMaxUsers,
    int? MaxUsersOverride,
    int MaxRecords,
    int Records,
    string? Notes,
    IReadOnlyList<string> DisabledModules,
    IReadOnlyList<string> TenantDisabledModules,
    string? OwnerName,
    string? OwnerEmail,
    IReadOnlyList<PlatformAuditDto> History,
    IReadOnlyList<string>? PlanExcludedModules = null);

/// <summary>The modules the owner switches off for a company. Core modules and modules not built yet are refused.</summary>
public sealed record SetCompanyModulesRequest(IReadOnlyList<string> DisabledModules);

/// <summary>Limits and notes for a company. <c>MaxUsersOverride</c> replaces the plan's user limit when set; <c>TrialEndsAt</c> is only for a company on trial.</summary>
public sealed record SetCompanyLimitsRequest(int? MaxUsersOverride, bool AiReadingAllowed, string? Notes, DateTime? TrialEndsAt);

/// <summary>A module with how many companies have it, for the module catalogue.</summary>
public sealed record PlatformModuleDto(string Key, string Name, string Description, string Category, string Icon, bool Core, bool Available, int Companies, int Total);

public sealed record PlatformPlanDto(Guid Id, string Key, string Name, int MaxUsers, int MaxRecords, decimal PriceMonthly, bool IsActive, int Companies, string? Description = null, IReadOnlyList<string>? IncludedModules = null);

/// <summary><c>IncludedModules</c> null leaves the plan's modules as they are; a list sets them (every switchable module, or none listed, means all).</summary>
public sealed record SavePlanRequest(string Name, int MaxUsers, int MaxRecords, decimal PriceMonthly, bool IsActive, string? Description = null, IReadOnlyList<string>? IncludedModules = null);

/// <summary>A page of companies with the counts that power the filter chips (they follow the search, not the chips).</summary>
public sealed record PlatformCompanyListDto(
    IReadOnlyList<PlatformCompanyDto> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<PlatformSlice> Statuses,
    IReadOnlyList<PlatformSlice> Plans,
    int ActiveCount = 0,
    int InactiveCount = 0);

/// <summary>A ready-made choice of modules: the modules a company has when it is applied.</summary>
public sealed record ModulePresetDto(string Key, string Name, string Description, IReadOnlyList<string> Modules);


/// <summary>Onboards a new company: the company, its owner's sign-in, the plan it starts on and the modules it gets. <c>Status</c> is "Trial" or "Active".</summary>
public sealed record CreateCompanyRequest(
    string CompanyName,
    string OwnerFirstName,
    string OwnerLastName,
    string OwnerEmail,
    string Password,
    string Country,
    string PlanKey,
    string Status,
    int TrialDays = 14,
    string? ModulePreset = null);

/// <summary>One thing the platform owner should know about: a company that signed up, or one that needs a look. <c>Id</c> is stable, so the screen can remember what was read.</summary>
public sealed record PlatformNotificationDto(string Id, string Kind, string Severity, string Title, string Message, Guid? TenantId, string? Company, DateTime At);
