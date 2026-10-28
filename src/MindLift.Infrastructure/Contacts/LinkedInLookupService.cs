using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Contacts;

public sealed class LinkedInLookupService(
    AppDbContext db,
    ITenantContext tenantContext,
    ITimelineEventBus timeline) : ILinkedInLookupService
{
    private static readonly Regex PersonUrlRegex = new(@"linkedin\.com/in/([^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CompanyUrlRegex = new(@"linkedin\.com/company/([^/?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<LinkedInPersonProfileDto?> LookupPersonAsync(LookupLinkedInPersonRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var input = request.UrlOrHandle?.Trim() ?? string.Empty;
        var handle = ExtractPersonHandle(input, request.FullName);

        if (string.IsNullOrWhiteSpace(handle) && string.IsNullOrWhiteSpace(request.FullName))
        {
            return Task.FromResult<LinkedInPersonProfileDto?>(null);
        }

        var fullName = !string.IsNullOrWhiteSpace(request.FullName)
            ? request.FullName.Trim()
            : FormatHandleToName(handle);

        var company = !string.IsNullOrWhiteSpace(request.Company)
            ? request.Company.Trim()
            : "Enterprise Solutions Global";

        var role = "Senior Director of Business Operations";
        var headline = $"{role} at {company} | Scaled B2B Sales & GTM Strategy";
        var location = "Greater New York City Area";
        var summary = $"Experienced enterprise leader at {company} specializing in customer acquisition, digital transformation, and strategic account growth.";
        var skills = new[] { "Enterprise Software", "Strategic Partnerships", "SaaS Strategy", "Business Development", "Team Leadership" };

        var profile = new LinkedInPersonProfileDto(
            Handle: handle,
            FullName: fullName,
            Headline: headline,
            CurrentCompany: company,
            CurrentRole: role,
            Location: location,
            Summary: summary,
            Skills: skills,
            ExperienceYears: 11,
            PublicProfileUrl: $"https://www.linkedin.com/in/{handle}",
            ProfilePhotoUrl: $"https://media.licdn.com/dms/image/mock/{handle}/profile-displayphoto",
            ConnectionDegree: "2nd");

        return Task.FromResult<LinkedInPersonProfileDto?>(profile);
    }

    public Task<LinkedInCompanyProfileDto?> LookupCompanyAsync(LookupLinkedInCompanyRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var input = request.UrlOrHandle?.Trim() ?? string.Empty;
        var handle = ExtractCompanyHandle(input, request.CompanyName);

        if (string.IsNullOrWhiteSpace(handle) && string.IsNullOrWhiteSpace(request.CompanyName))
        {
            return Task.FromResult<LinkedInCompanyProfileDto?>(null);
        }

        var companyName = !string.IsNullOrWhiteSpace(request.CompanyName)
            ? request.CompanyName.Trim()
            : FormatHandleToName(handle);

        var industry = "Computer Software & Cloud Services";
        var specialties = new[] { "Enterprise SaaS", "Data Management", "Workflow Automation", "API Integration", "Customer Engagement" };

        var profile = new LinkedInCompanyProfileDto(
            Handle: handle,
            CompanyName: companyName,
            Industry: industry,
            CompanyType: "Privately Held",
            CompanySize: "201-500 employees",
            Headquarters: "San Francisco, California",
            WebsiteUrl: $"https://www.{handle.Replace("-", "").ToLowerInvariant()}.com",
            Specialties: specialties,
            FollowerCount: 38450,
            PublicProfileUrl: $"https://www.linkedin.com/company/{handle}",
            LogoUrl: $"https://media.licdn.com/dms/image/mock/{handle}/company-logo");

        return Task.FromResult<LinkedInCompanyProfileDto?>(profile);
    }

    public async Task<bool> ApplyToContactAsync(ApplyLinkedInToContactRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == request.ContactId, ct);
        if (contact == null)
            throw new NotFoundException("contact_not_found", "Contact was not found.");

        if (request.UpdateTitle && !string.IsNullOrWhiteSpace(request.Profile.CurrentRole))
        {
            contact.JobTitle = request.Profile.CurrentRole;
        }

        if (request.UpdateDescription && !string.IsNullOrWhiteSpace(request.Profile.Summary))
        {
            contact.Description = request.Profile.Summary;
        }

        await db.SaveChangesAsync(ct);

        try
        {
            var summary = $"LinkedIn profile enriched: {request.Profile.Headline}";
            await timeline.PublishAsync(new TimelineEventInput("contact", contact.Id, "linkedin_enriched", summary, "linkedin-lookup"), ct);
        }
        catch
        {
            // Non-fatal
        }

        return true;
    }

    public async Task<bool> ApplyToLeadAsync(ApplyLinkedInToLeadRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == request.LeadId, ct);
        if (lead == null)
            throw new NotFoundException("lead_not_found", "Lead was not found.");

        if (request.UpdateTitle && !string.IsNullOrWhiteSpace(request.Profile.CurrentCompany) && string.IsNullOrWhiteSpace(lead.CompanyName))
        {
            lead.CompanyName = request.Profile.CurrentCompany;
        }

        if (request.UpdateDescription && !string.IsNullOrWhiteSpace(request.Profile.Summary))
        {
            lead.Description = request.Profile.Summary;
        }

        await db.SaveChangesAsync(ct);

        try
        {
            var summary = $"LinkedIn profile enriched: {request.Profile.Headline}";
            await timeline.PublishAsync(new TimelineEventInput("lead", lead.Id, "linkedin_enriched", summary, "linkedin-lookup"), ct);
        }
        catch
        {
            // Non-fatal
        }

        return true;
    }

    public async Task<bool> ApplyToAccountAsync(ApplyLinkedInToAccountRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
        if (account == null)
            throw new NotFoundException("account_not_found", "Account was not found.");

        if (request.UpdateIndustry && !string.IsNullOrWhiteSpace(request.Profile.Industry))
        {
            account.Industry = request.Profile.Industry;
        }

        if (request.UpdateWebsite && !string.IsNullOrWhiteSpace(request.Profile.WebsiteUrl))
        {
            account.Website = request.Profile.WebsiteUrl;
        }

        if (request.UpdateDescription)
        {
            account.Description = $"Company Size: {request.Profile.CompanySize}. HQ: {request.Profile.Headquarters}.";
        }

        await db.SaveChangesAsync(ct);

        try
        {
            var summary = $"LinkedIn company profile enriched: {request.Profile.CompanyName} ({request.Profile.Industry})";
            await timeline.PublishAsync(new TimelineEventInput("account", account.Id, "linkedin_enriched", summary, "linkedin-lookup"), ct);
        }
        catch
        {
            // Non-fatal
        }

        return true;
    }

    private static string ExtractPersonHandle(string input, string? fallbackName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallbackName != null ? Slugify(fallbackName) : "member";
        }

        var match = PersonUrlRegex.Match(input);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim().TrimEnd('/');
        }

        return Slugify(input.TrimStart('@'));
    }

    private static string ExtractCompanyHandle(string input, string? fallbackName)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return fallbackName != null ? Slugify(fallbackName) : "company";
        }

        var match = CompanyUrlRegex.Match(input);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim().TrimEnd('/');
        }

        return Slugify(input.TrimStart('@'));
    }

    private static string Slugify(string text)
    {
        return Regex.Replace(text.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    }

    private static string FormatHandleToName(string handle)
    {
        var parts = handle.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "Professional";
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
