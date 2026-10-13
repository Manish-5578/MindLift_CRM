namespace MindLift.Shared.Contracts;

/// <summary>Step numbers of the setup wizard (plan 6.1, 6.3).</summary>
public static class OnboardingSteps
{
    public const int Company = 1;
    public const int Industry = 2;
    public const int Pipeline = 3;
    public const int Team = 4;
    public const int Data = 5;
    public const int Channels = 6;
    public const int Completed = 99;
}

public sealed record OnboardingChecklistItemDto(string Key, string Title, bool Done, bool Available);

public sealed record OnboardingTenantDto(string Name, string Status, string? IndustryKey, DateTime? TrialEndsAtUtc, bool SampleDataLoaded);

public sealed record OnboardingDto(
    int CurrentStep,
    IReadOnlyList<int> CompletedSteps,
    IReadOnlyList<int> SkippedSteps,
    OnboardingTenantDto Tenant,
    IReadOnlyList<OnboardingChecklistItemDto> Checklist,
    bool ChecklistDismissed);

/// <summary>Step 1: company profile and regional settings.</summary>
public sealed record OnboardingCompanyRequest(
    string CompanyName,
    string Country,
    string BaseCurrency,
    string TimeZoneId,
    string DefaultLanguage,
    bool TaxEnabled,
    string? TaxLabel,
    decimal DefaultTaxRate,
    string? TaxRegistrationNumber);

public sealed record IndustrySummaryDto(
    string Key,
    string Name,
    string? NameAr,
    string Description,
    string DealLabel,
    IReadOnlyList<string> PipelineStages,
    IReadOnlyList<string> CustomFieldLabels,
    IReadOnlyList<string> EnabledModules);

public sealed record ApplyIndustryRequest(string IndustryKey);

public sealed record OnboardingStageDto(string Name, int Probability, string StageType);

/// <summary>Step 3: stages of the default pipeline, in order. Stages with deals keep their identity by position.</summary>
public sealed record SaveOnboardingPipelineRequest(IReadOnlyList<OnboardingStageDto> Stages);

public sealed record OnboardingInviteDto(string Email, string FirstName, string LastName, Guid RoleId, Guid? TeamId);

public sealed record OnboardingTeamRequest(IReadOnlyList<OnboardingInviteDto> Invites);

/// <summary>Step 5: sample, import or empty.</summary>
public sealed record OnboardingDataRequest(string Mode);

public sealed record SampleDataDto(int Accounts, int Contacts, int Leads, int Deals, int Activities);
