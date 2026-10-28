using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Tenancy;

/// <summary>
/// Creates a new workspace (plan 6.2): tenant on a 14-day Pro trial, country-based settings,
/// the seeded base configuration, and the owner account with the Admin role.
/// </summary>
public sealed class TenantSignupService(
    AppDbContext db,
    UserManager<AppUser> users,
    ITenantContext tenant,
    IClock clock,
    SystemSeedService seed)
{
    public const string TrialPlanKey = "pro";
    public const int TrialDays = 14;

    public async Task<AppUser> CreateTenantWithOwnerAsync(SignupRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = users.NormalizeEmail(request.Email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            throw new ConflictException("email_taken", "An account with this email already exists.");
        }

        if (!request.AcceptTerms)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["acceptTerms"] = ["You must accept the terms to continue."],
            });
        }

        var now = clock.UtcNow;
        var country = request.Country.ToUpperInvariant();
        var defaults = CountryDefaults.For(country);
        var plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Key == TrialPlanKey, cancellationToken);

        var workspace = new Tenant
        {
            Name = request.CompanyName.Trim(),
            Slug = await UniqueSlugAsync(request.CompanyName, cancellationToken),
            Status = "Trial",
            PlanId = plan.Id,
            TrialEndsAt = now.AddDays(TrialDays),
            Country = country,
            CreatedAt = now,
        };
        db.Tenants.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);

        using var scope = tenant.Use(workspace.Id);

        db.TenantSettings.Add(new TenantSettings
        {
            TenantId = workspace.Id,
            CompanyName = workspace.Name,
            Email = request.Email,
            Phone = request.Phone,
            BaseCurrency = defaults.Currency,
            DefaultLanguage = request.Language == "ar" ? "ar" : "en",
            TimeZoneId = defaults.TimeZoneId,
            WeekStartDay = (int)defaults.WeekStart,
            TaxEnabled = defaults.TaxRate > 0,
            TaxLabel = defaults.TaxLabel,
            DefaultTaxRate = defaults.TaxRate,
        });

        await seed.SeedTenantAsync(workspace.Id, cancellationToken);
        var adminRoleId = await seed.AdminRoleIdAsync(cancellationToken);

        var owner = new AppUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = workspace.Id,
            UserName = request.Email,
            Email = request.Email,
            PhoneNumber = request.Phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Language = request.Language == "ar" ? "ar" : "en",
            RoleId = adminRoleId,
            IsTenantOwner = true,
            IsActive = true,
            LockoutEnabled = true,
            CreatedAt = now,
        };

        var result = await users.CreateAsync(owner, request.Password);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }

        return owner;
    }

    public async Task<AppUser> CreateShadowTenantWithOwnerAsync(SignupRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.AcceptTerms)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["acceptTerms"] = ["You must accept the terms to continue."],
            });
        }

        var now = clock.UtcNow;
        var country = request.Country.ToUpperInvariant();
        var defaults = CountryDefaults.For(country);
        var plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Key == TrialPlanKey, cancellationToken);

        var workspace = new Tenant
        {
            Name = request.CompanyName.Trim(),
            Slug = await UniqueSlugAsync(request.CompanyName, cancellationToken),
            Status = "Trial",
            PlanId = plan.Id,
            TrialEndsAt = now.AddDays(TrialDays),
            Country = country,
            CreatedAt = now,
        };
        db.Tenants.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);

        using var scope = tenant.Use(workspace.Id);

        db.TenantSettings.Add(new TenantSettings
        {
            TenantId = workspace.Id,
            CompanyName = workspace.Name,
            Email = request.Email,
            Phone = request.Phone,
            BaseCurrency = defaults.Currency,
            DefaultLanguage = request.Language == "ar" ? "ar" : "en",
            TimeZoneId = defaults.TimeZoneId,
            WeekStartDay = (int)defaults.WeekStart,
            TaxEnabled = defaults.TaxRate > 0,
            TaxLabel = defaults.TaxLabel,
            DefaultTaxRate = defaults.TaxRate,
        });

        await seed.SeedTenantAsync(workspace.Id, cancellationToken);
        var adminRoleId = await seed.AdminRoleIdAsync(cancellationToken);

        var normalizedEmail = users.NormalizeEmail(request.Email);
        var shadowOwner = new AppUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = workspace.Id,
            UserName = request.Email,
            Email = request.Email,
            NormalizedEmail = $"{normalizedEmail}_shadow_{Guid.NewGuid():N}",
            NormalizedUserName = $"{normalizedEmail}_shadow_{Guid.NewGuid():N}",
            PhoneNumber = request.Phone,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Language = request.Language == "ar" ? "ar" : "en",
            RoleId = adminRoleId,
            IsTenantOwner = true,
            IsActive = true,
            LockoutEnabled = true,
            CreatedAt = now,
        };
        shadowOwner.PasswordHash = users.PasswordHasher.HashPassword(shadowOwner, request.Password);
        db.Users.Add(shadowOwner);
        await db.SaveChangesAsync(cancellationToken);

        return shadowOwner;
    }

    /// <summary>"Al Noor Properties" → "al-noor-properties", with -2, -3 ... appended until unique (plan 6.2).</summary>
    private async Task<string> UniqueSlugAsync(string companyName, CancellationToken cancellationToken)
    {
        var baseSlug = Regex.Replace(companyName.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = "workspace";
        }

        var candidate = baseSlug;
        var suffix = 2;
        while (await db.Tenants.AnyAsync(t => t.Slug == candidate, cancellationToken))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }

        return candidate;
    }
}
