using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A subscribing business. Global (not tenant-filtered): it is the tenant itself.
/// </summary>
public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Trial, Active, PastDue, Suspended or Cancelled (plan 6.9).</summary>
    public string Status { get; set; } = "Trial";

    public Guid PlanId { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public string Country { get; set; } = "AE";
    public string? IndustryKey { get; set; }
    public int OnboardingStep { get; set; } = 1;
    public DateTime? OnboardingCompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Wizard steps finished and steps skipped (plan 6.3). Steps are 1 company, 2 industry, 3 pipeline, 4 team, 5 data, 6 channels.</summary>
    public int[] OnboardingDone { get; set; } = [];
    public int[] OnboardingSkipped { get; set; } = [];
    public bool ChecklistDismissed { get; set; }

    /// <summary>Version of the industry template last applied (plan 6.5).</summary>
    public int TemplateVersion { get; set; }

    /// <summary>Set once the trial reminder email has been sent (plan 6.9).</summary>
    public bool TrialReminderSent { get; set; }

    /// <summary>
    /// Modules (AppModule names) the platform owner has switched off for this company. Empty means everything built is available.
    /// A module listed here is hidden from the company and refused by the API, whatever the company's own administrator chooses.
    /// </summary>
    public string[] PlatformDisabledModules { get; set; } = [];

    /// <summary>False when the platform owner does not allow this company to switch on AI reading of documents.</summary>
    public bool AiReadingAllowed { get; set; } = true;

    /// <summary>A user limit set by the platform owner for this company. When set it replaces the plan's limit.</summary>
    public int? MaxUsersOverride { get; set; }

    /// <summary>Private notes the platform owner keeps about the company. Never shown to the company.</summary>
    public string? PlatformNotes { get; set; }
}

/// <summary>
/// A change the platform owner made to a company or a plan, or something that happened to a company, kept for the history in the platform console.
/// Global: not tied to one workspace, so it survives the company.
/// </summary>
public sealed class PlatformAuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }

    /// <summary>The company's name when it happened, so the entry still reads well after a rename.</summary>
    public string? Company { get; set; }

    public Guid? ActorId { get; set; }
    public string Actor { get; set; } = string.Empty;

    /// <summary>A short code such as "modules.changed", "status.changed" or "plan.changed".</summary>
    public string Action { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A subscription plan. Global.</summary>
public sealed class Plan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>starter, pro or enterprise.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public int MaxUsers { get; set; }
    public int MaxRecords { get; set; }
    public decimal PriceMonthly { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>What the plan is for, in a sentence the platform owner writes.</summary>
    public string? Description { get; set; }

    /// <summary>The modules (AppModule names) a company on this plan can have. Empty means every module that is built.</summary>
    public string[] IncludedModules { get; set; } = [];
}

/// <summary>Company profile and regional settings (plan 5.1). One row per tenant.</summary>
[Audited]
public sealed class TenantSettings : TenantEntity
{
    public string CompanyName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public string DefaultLanguage { get; set; } = "en";
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>0 = Sunday ... 6 = Saturday, as System.DayOfWeek.</summary>
    public int WeekStartDay { get; set; } = 1;

    public int FiscalYearStartMonth { get; set; } = 1;
    public bool TaxEnabled { get; set; }
    public string? TaxLabel { get; set; }
    public decimal DefaultTaxRate { get; set; }
    public string? TaxRegistrationNumber { get; set; }
    public string? PrimaryColor { get; set; }
    public bool SampleDataLoaded { get; set; }

    /// <summary>When true, documents are also read by a language model that finds the fields each document type defines. Off by default: the text is sent to the model.</summary>
    public bool AiReadingEnabled { get; set; }

    /// <summary>ISO 4217 codes the tenant can use besides the base currency.</summary>
    public string[] EnabledCurrencies { get; set; } = [];
}


