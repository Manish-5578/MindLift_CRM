namespace MindLift.Shared.Contracts;

/// <summary>
/// Phase 6 / #39: LinkedIn profile lookup (linkedin-lookup).
/// Lookup requests and enriched profile representations for people and companies.
/// </summary>
public sealed record LookupLinkedInPersonRequest(
    string? UrlOrHandle,
    string? FullName = null,
    string? Company = null);

public sealed record LookupLinkedInCompanyRequest(
    string? UrlOrHandle,
    string? CompanyName = null);

public sealed record LinkedInPersonProfileDto(
    string Handle,
    string FullName,
    string Headline,
    string CurrentCompany,
    string CurrentRole,
    string Location,
    string? Summary,
    IReadOnlyList<string> Skills,
    int? ExperienceYears,
    string PublicProfileUrl,
    string? ProfilePhotoUrl,
    string? ConnectionDegree);

public sealed record LinkedInCompanyProfileDto(
    string Handle,
    string CompanyName,
    string Industry,
    string CompanyType,
    string CompanySize,
    string Headquarters,
    string? WebsiteUrl,
    IReadOnlyList<string> Specialties,
    int? FollowerCount,
    string PublicProfileUrl,
    string? LogoUrl);

public sealed record ApplyLinkedInToContactRequest(
    Guid ContactId,
    LinkedInPersonProfileDto Profile,
    bool UpdateTitle = true,
    bool UpdateDescription = true);

public sealed record ApplyLinkedInToLeadRequest(
    Guid LeadId,
    LinkedInPersonProfileDto Profile,
    bool UpdateTitle = true,
    bool UpdateDescription = true);

public sealed record ApplyLinkedInToAccountRequest(
    Guid AccountId,
    LinkedInCompanyProfileDto Profile,
    bool UpdateIndustry = true,
    bool UpdateDescription = true,
    bool UpdateWebsite = true);
