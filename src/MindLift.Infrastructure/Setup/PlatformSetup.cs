using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Setup;

/// <summary>Idempotent global data: the subscription plans.</summary>
public static class PlanCatalog
{
    public static readonly (string Key, string Name, int MaxUsers, int MaxRecords, decimal Price)[] Plans =
    [
        ("starter", "Starter", 5, 5_000, 99m),
        ("pro", "Pro", 25, 100_000, 249m),
        ("enterprise", "Enterprise", 500, 10_000_000, 799m),
    ];

    public static async Task EnsureAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        foreach (var (key, name, maxUsers, maxRecords, price) in Plans)
        {
            if (!await db.Plans.AnyAsync(p => p.Key == key, cancellationToken))
            {
                db.Plans.Add(new Plan { Key = key, Name = name, MaxUsers = maxUsers, MaxRecords = maxRecords, PriceMonthly = price });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Creates the internal platform tenant and a platform admin (plan 6.10).
/// Run from the command line: <c>dotnet run --project src/MindLift.Api -- seed-platform-admin --email ... --password ...</c>.
/// </summary>
public sealed class PlatformAdminSeeder(AppDbContext db, UserManager<AppUser> users, ITenantContext tenant, IClock clock, SystemSeedService seed)
{
    public const string PlatformSlug = "mindlift-platform";

    public async Task<string> SeedAsync(string email, string password, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var platform = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == PlatformSlug, cancellationToken);
        if (platform is null)
        {
            var enterprise = await db.Plans.SingleAsync(p => p.Key == "enterprise", cancellationToken);
            platform = new Tenant
            {
                Name = "MindLift Platform",
                Slug = PlatformSlug,
                Status = "Active",
                PlanId = enterprise.Id,
                CreatedAt = clock.UtcNow,
            };
            db.Tenants.Add(platform);
            await db.SaveChangesAsync(cancellationToken);
        }

        using var scope = tenant.Use(platform.Id);

        var hasSettings = await db.TenantSettings.IgnoreQueryFilters().AnyAsync(s => s.TenantId == platform.Id, cancellationToken);
        if (!hasSettings)
        {
            db.TenantSettings.Add(new TenantSettings
            {
                TenantId = platform.Id,
                CompanyName = platform.Name,
                Email = email,
                BaseCurrency = "USD",
                DefaultLanguage = "en",
                TimeZoneId = "UTC",
                WeekStartDay = 1,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var hasRoles = await db.Roles.IgnoreQueryFilters().AnyAsync(r => r.TenantId == platform.Id, cancellationToken);
        if (!hasRoles)
        {
            await seed.SeedTenantAsync(platform.Id, cancellationToken);
        }

        var adminRole = await db.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == platform.Id && r.IsAdmin, cancellationToken);
        var adminRoleId = adminRole?.Id;

        var normalized = users.NormalizeEmail(email);
        var existing = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        if (existing != null)
        {
            existing.TenantId = platform.Id;
            existing.IsPlatformAdmin = true;
            existing.IsTenantOwner = true;
            if (adminRoleId != null)
            {
                existing.RoleId = adminRoleId;
            }
            await db.SaveChangesAsync(cancellationToken);
            return $"Platform admin {email} updated with platform tenant and admin role.";
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = platform.Id,
            UserName = email,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IsPlatformAdmin = true,
            IsTenantOwner = true,
            IsActive = true,
            EmailConfirmed = true,
            LockoutEnabled = true,
            RoleId = adminRoleId,
            CreatedAt = clock.UtcNow,
        };

        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["password"] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }

        return $"Platform admin {email} created.";
    }
}
