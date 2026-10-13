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

    /// <summary>ISO 4217 codes the tenant can use besides the base currency.</summary>
    public string[] EnabledCurrencies { get; set; } = [];
}


