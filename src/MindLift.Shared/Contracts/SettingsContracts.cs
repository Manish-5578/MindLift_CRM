using MindLift.Shared.Filtering;

namespace MindLift.Shared.Contracts;

// ---------- Company ----------

public sealed record CompanySettingsDto(
    string CompanyName,
    string? Email,
    string? Phone,
    string BaseCurrency,
    string DefaultLanguage,
    string TimeZoneId,
    int WeekStartDay,
    int FiscalYearStartMonth,
    bool TaxEnabled,
    string? TaxLabel,
    decimal DefaultTaxRate,
    string? TaxRegistrationNumber,
    string? PrimaryColor);

public sealed record SaveCompanySettingsRequest(
    string CompanyName,
    string? Email,
    string? Phone,
    string DefaultLanguage,
    string TimeZoneId,
    int WeekStartDay,
    int FiscalYearStartMonth,
    bool TaxEnabled,
    string? TaxLabel,
    decimal DefaultTaxRate,
    string? TaxRegistrationNumber,
    string? PrimaryColor);

// ---------- Currencies ----------

public sealed record CurrencySettingsDto(string BaseCurrency, IReadOnlyList<string> EnabledCurrencies);

public sealed record SaveCurrencySettingsRequest(string BaseCurrency, IReadOnlyList<string> EnabledCurrencies);

public sealed record ExchangeRateDto(Guid Id, string Currency, decimal RateToBase, DateTime EffectiveDate);

public sealed record SaveExchangeRateRequest(string Currency, decimal RateToBase, DateTime EffectiveDate);

// ---------- Modules ----------

public sealed record ModuleSettingDto(
    string Module,
    bool IsEnabled,
    string SingularLabel,
    string PluralLabel,
    int SortOrder,
    bool PlatformLocked = false);

public sealed record SaveModuleSettingRequest(bool IsEnabled, string SingularLabel, string PluralLabel, int SortOrder);

// ---------- Fields ----------

/// <summary>A field as the UI sees it: system or custom, with label overrides already applied.</summary>
public sealed record FieldDto(
    string Key,
    string Label,
    string? LabelAr,
    FieldType Type,
    bool IsCustom,
    bool IsRequired,
    bool IsHidden,
    bool Filterable,
    string? PicklistKey,
    IReadOnlyList<PicklistOptionDto>? Options);

public sealed record CustomFieldDto(
    Guid Id,
    string EntityType,
    string Key,
    string Label,
    string? LabelAr,
    FieldType Type,
    bool IsRequired,
    bool IsActive,
    bool ShowInList,
    bool Filterable,
    string? Section,
    int SortOrder,
    string? PicklistKey,
    IReadOnlyList<PicklistOptionDto> Options);

public sealed record PicklistOptionDto(string Value, string Label, string? LabelAr, string? Color);

public sealed record SaveCustomFieldRequest(
    string EntityType,
    string Key,
    string Label,
    string? LabelAr,
    FieldType Type,
    bool IsRequired,
    bool ShowInList,
    bool Filterable,
    string? Section,
    int SortOrder,
    IReadOnlyList<PicklistOptionDto>? Options);

public sealed record FieldOverrideDto(string EntityType, string FieldKey, string? Label, string? LabelAr, bool IsHidden);

public sealed record SaveFieldOverrideRequest(string? Label, string? LabelAr, bool IsHidden);

// ---------- Layouts ----------

public sealed record LayoutSectionDto(string Section, string? SectionAr, int Columns, IReadOnlyList<string> Fields);

public sealed record LayoutDto(string EntityType, string Kind, IReadOnlyList<LayoutSectionDto> Sections);

public sealed record SaveLayoutRequest(IReadOnlyList<LayoutSectionDto> Sections);

// ---------- Picklists ----------

public sealed record PicklistValueDto(
    Guid Id,
    string Value,
    string Label,
    string? LabelAr,
    string? Category,
    string? Color,
    int SortOrder,
    bool IsDefault,
    bool IsActive,
    bool IsSystem);

public sealed record PicklistDto(string Key, IReadOnlyList<PicklistValueDto> Values);

public sealed record SavePicklistValueRequest(
    string Value,
    string Label,
    string? LabelAr,
    string? Category,
    string? Color,
    int SortOrder,
    bool IsDefault,
    bool IsActive);

// ---------- Pipelines ----------

public sealed record PipelineStageDto(Guid Id, string Name, string? NameAr, int SortOrder, int Probability, string StageType, string? Color, int? RottingDays);

public sealed record PipelineDto(Guid Id, string Name, string? NameAr, bool IsDefault, bool IsActive, int SortOrder, IReadOnlyList<PipelineStageDto> Stages);

public sealed record SavePipelineRequest(string Name, string? NameAr, bool IsDefault, bool IsActive, int SortOrder);

public sealed record SaveStageRequest(string Name, string? NameAr, int Probability, string StageType, string? Color, int? RottingDays, int SortOrder);

// ---------- Saved views ----------

public sealed record SavedViewDto(Guid Id, string EntityType, string Name, bool IsShared, bool IsDefault, Guid? OwnerUserId, FilterGroup? Filter, IReadOnlyList<string> Columns, string? SortField, string? SortDir);

public sealed record SaveSavedViewRequest(string EntityType, string Name, bool IsShared, bool IsDefault, FilterGroup? Filter, IReadOnlyList<string> Columns, string? SortField, string? SortDir);

// ---------- Bootstrap ----------

/// <summary>What the current user may do in one module. Scope is Own, Team or All.</summary>
public sealed record ModulePermissionDto(string Module, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete, bool CanExport, bool CanImport, string Scope);

/// <summary>Everything the staff app needs at login, in one call (plan 7.2).</summary>
public sealed record BootstrapDto(
    MeResponse User,
    IReadOnlyList<ModuleSettingDto> Modules,
    IReadOnlyDictionary<string, IReadOnlyList<FieldDto>> Fields,
    IReadOnlyList<PicklistDto> Picklists,
    IReadOnlyList<PipelineDto> Pipelines,
    IReadOnlyDictionary<string, IReadOnlyList<LayoutDto>> Layouts,
    CurrencySettingsDto Currencies,
    CompanySettingsDto? Company,
    IReadOnlyList<ModulePermissionDto> Permissions,
    string Version);


// Email sending: the company's daily limit for automated mail, its own sending domains, and addresses that must not be emailed.

public sealed record DnsRecordDto(string Type, string Name, string Value, string Purpose, bool Found);

public sealed record SendingDomainDto(Guid Id, string Domain, bool Verified, bool OwnershipVerified, bool SpfOk, bool DmarcOk, DateTime? LastCheckedAt, string? LastCheckMessage, IReadOnlyList<DnsRecordDto> Records);

public sealed record EmailSuppressionDto(Guid Id, string Email, string Reason, DateTime CreatedAt);

public sealed record EmailSendingDto(int SentToday, int DailyLimit, IReadOnlyList<SendingDomainDto> Domains, IReadOnlyList<EmailSuppressionDto> Suppressed);

public sealed record AddSendingDomainRequest(string Domain);

public sealed record AddSuppressionRequest(string Email);

/// <summary>A download of all of a company's data. <c>Status</c> is "running", "ready" or "failed".</summary>
public sealed record DataExportDto(Guid Id, string Status, DateTime CreatedAt, DateTime? CompletedAt, DateTime? ExpiresAt, long? SizeBytes);

/// <summary>The platform owner confirms the permanent removal of a company by typing its address (slug).</summary>
public sealed record PurgeCompanyRequest(string Confirm);

/// <summary>A short-lived address that downloads a file without signing in.</summary>
public sealed record DownloadLinkDto(string Url);
