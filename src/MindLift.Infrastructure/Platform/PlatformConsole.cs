using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Common;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Platform;

/// <summary>
/// What the owner of the product sees and changes across every company (plan 6.10): numbers for each company, which modules each company has,
/// limits, plans and status, with every change recorded in a history. Only a platform administrator reaches this (the API checks the token).
/// </summary>
public sealed class PlatformConsole(AppDbContext db, ITenantContext tenant, IClock clock, IPlatformEntitlements entitlements, MindLift.Infrastructure.Settings.SettingsVersion settingsVersion, MindLift.Infrastructure.Tenancy.TenantSignupService signup)
{
    private static readonly string[] Statuses = ["Trial", "Active", "PastDue", "Suspended", "Cancelled"];
    private const int MaxNotes = 2000;

    /// <summary>What was counted for each company, so a list of companies is built from a handful of queries rather than a handful per company.</summary>
    private sealed class Stats
    {
        public Dictionary<Guid, (int Users, int Active7d, DateTime? LastActive)> Users { get; } = [];
        public Dictionary<Guid, int> Leads { get; } = [];
        public Dictionary<Guid, int> Contacts { get; } = [];
        public Dictionary<Guid, int> Accounts { get; } = [];
        public Dictionary<Guid, int> Deals { get; } = [];
        public Dictionary<Guid, int> Documents { get; } = [];
        public Dictionary<Guid, bool> AiEnabled { get; } = [];
        public Dictionary<Guid, Plan> Plans { get; } = [];
        public Dictionary<Guid, string> PlanById { get; } = [];
    }

    private IQueryable<Tenant> Companies() => db.Tenants.AsNoTracking().Where(t => t.Slug != PlatformAdminSeeder.PlatformSlug);

    // ------------------------------------------------------------------ reading

    public async Task<PlatformOverviewDto> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var tenants = await Companies().ToListAsync(cancellationToken);
        var ids = tenants.Select(t => t.Id).ToList();
        var stats = await LoadStatsAsync(ids, now, cancellationToken);
        var companies = tenants.Select(t => Map(t, stats, now)).ToList();

        // Signups per day over the last 30 days, every day present, so a chart has no gaps.
        var today = now.Date;
        var signups = Enumerable.Range(0, 30).Select(i => today.AddDays(i - 29))
            .Select(day => new PlatformPoint(day.ToString("MMM d", CultureInfo.InvariantCulture), tenants.Count(t => t.CreatedAt.Date == day))).ToList();

        var planNames = stats.PlanById;
        var attention = new List<PlatformAttentionItem>();
        foreach (var c in companies)
        {
            var t = tenants.First(x => x.Id == c.Id);
            if (c.Status == "Trial" && t.TrialEndsAt is { } ends)
            {
                var days = (int)Math.Ceiling((ends - now).TotalDays);
                if (days < 0) attention.Add(new(c.Id, c.Name, "trial-ended", $"The trial ended {-days} day{(days == -1 ? "" : "s")} ago and no plan was chosen.", "critical"));
                else if (days <= 7) attention.Add(new(c.Id, c.Name, "trial-ending", days == 0 ? "The trial ends today." : $"The trial ends in {days} day{(days == 1 ? "" : "s")}.", days <= 2 ? "critical" : "warning"));
            }

            if (c.Status == "PastDue") attention.Add(new(c.Id, c.Name, "past-due", "Payment is overdue.", "critical"));
            else if (c.Status == "Suspended") attention.Add(new(c.Id, c.Name, "suspended", "Suspended: nobody in this company can sign in.", "info"));
            else if (c.Status == "Active" && c.CreatedAt < now.AddDays(-30) && (c.LastActiveAt is null || c.LastActiveAt < now.AddDays(-30)))
                attention.Add(new(c.Id, c.Name, "inactive", c.LastActiveAt is null ? "Nobody has ever signed in." : $"Nobody has signed in for {(int)(now - c.LastActiveAt.Value).TotalDays} days.", "warning"));

            var limit = t.MaxUsersOverride ?? stats.Plans.GetValueOrDefault(t.PlanId)?.MaxUsers ?? 0;
            if (limit > 0 && c.Users * 10 >= limit * 9 && c.Status is "Active" or "Trial")
                attention.Add(new(c.Id, c.Name, "seats", c.Users >= limit ? $"All {limit} seats are used." : $"{c.Users} of {limit} seats are used.", "warning"));
        }

        attention = attention.OrderBy(a => a.Severity switch { "critical" => 0, "warning" => 1, _ => 2 }).ThenBy(a => a.Company).Take(12).ToList();

        var adoption = ModuleCatalog.Offered
            .Select(m => new PlatformModuleAdoption(m.Module.ToString(), m.Name, tenants.Count(t => m.Core || !PlanModules.Disabled(t, stats.Plans.GetValueOrDefault(t.PlanId)).Contains(m.Module.ToString())), tenants.Count))
            .ToList();

        return new PlatformOverviewDto(
            companies.Count,
            companies.Count(c => c.Status == "Active"),
            companies.Count(c => c.Status == "Trial"),
            companies.Count(c => c.Status == "Trial" && c.TrialEndsAt is { } e && e >= now && e <= now.AddDays(7)),
            companies.Count(c => c.Status == "PastDue"),
            companies.Count(c => c.Status == "Suspended"),
            companies.Count(c => c.Status == "Cancelled"),
            companies.Sum(c => c.Users),
            companies.Sum(c => c.ActiveUsers7d),
            tenants.Count(t => t.CreatedAt >= now.AddDays(-30)),
            tenants.Count(t => t.CreatedAt >= now.AddDays(-60) && t.CreatedAt < now.AddDays(-30)),
            companies.Sum(c => c.Documents),
            companies.Sum(c => c.Leads + c.Contacts + c.Accounts + c.Deals),
            companies.Count(c => c.AiEnabled),
            signups,
            Statuses.Select(s => new PlatformSlice(s, s, companies.Count(c => c.Status == s))).ToList(),
            planNames.Select(p => new PlatformSlice(p.Value, p.Value, companies.Count(c => c.PlanKey == p.Value))).ToList(),
            companies.OrderByDescending(c => c.CreatedAt).Take(6).ToList(),
            attention,
            adoption,
            await RecentAuditAsync(null, 10, cancellationToken),
            // What the companies that are paying would be charged a month at their plan's price.
            tenants.Where(t => t.Status == "Active").Sum(t => stats.Plans.GetValueOrDefault(t.PlanId)?.PriceMonthly ?? 0m));
    }

    public async Task<PlatformCompanyListDto> ListCompaniesAsync(string? search, string? status, string? plan, string? sort, int page, int pageSize, CancellationToken cancellationToken = default, string? activity = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var now = clock.UtcNow;

        var query = Companies();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term));
        }

        var tenants = await query.ToListAsync(cancellationToken);
        var stats = await LoadStatsAsync(tenants.Select(t => t.Id).ToList(), now, cancellationToken);
        var all = tenants.Select(t => Map(t, stats, now)).ToList();

        // Counts for the filter chips follow the search, not the chips themselves, so each chip shows what choosing it would give.
        var statusCounts = Statuses.Select(s => new PlatformSlice(s, s, all.Count(c => c.Status == s))).ToList();
        var planCounts = stats.PlanById.Select(p => new PlatformSlice(p.Value, stats.Plans.Values.First(x => x.Key == p.Value).Name, all.Count(c => c.PlanKey == p.Value))).ToList();

        var cutoff = now.AddDays(-30);
        bool IsActive(PlatformCompanyDto c) => c.LastActiveAt is { } seen && seen >= cutoff;
        var activeCount = all.Count(IsActive);
        IEnumerable<PlatformCompanyDto> filtered = all;
        if (string.Equals(activity, "active", StringComparison.OrdinalIgnoreCase)) filtered = filtered.Where(IsActive);
        else if (string.Equals(activity, "inactive", StringComparison.OrdinalIgnoreCase)) filtered = filtered.Where(c => !IsActive(c));
        if (!string.IsNullOrWhiteSpace(status)) filtered = filtered.Where(c => string.Equals(c.Status, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(plan)) filtered = filtered.Where(c => string.Equals(c.PlanKey, plan, StringComparison.OrdinalIgnoreCase));

        filtered = (sort ?? "name").ToLowerInvariant() switch
        {
            "newest" => filtered.OrderByDescending(c => c.CreatedAt),
            "oldest" => filtered.OrderBy(c => c.CreatedAt),
            "users" => filtered.OrderByDescending(c => c.Users).ThenBy(c => c.Name),
            "active" => filtered.OrderByDescending(c => c.LastActiveAt ?? DateTime.MinValue).ThenBy(c => c.Name),
            "health" => filtered.OrderBy(c => c.Health).ThenBy(c => c.Name),
            "records" => filtered.OrderByDescending(c => c.Leads + c.Contacts + c.Accounts + c.Deals).ThenBy(c => c.Name),
            _ => filtered.OrderBy(c => c.Name),
        };

        var list = filtered.ToList();
        return new PlatformCompanyListDto(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize, statusCounts, planCounts, activeCount, all.Count - activeCount);
    }

    /// <summary>Onboards a company the way signing up does, then sets the plan, status and modules the owner chose.</summary>
    public async Task<PlatformCompanyDetailDto> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Status is not ("Trial" or "Active")) throw new ValidationException("status", "A new company starts on trial or active.");
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == request.PlanKey && p.IsActive, cancellationToken)
            ?? throw new ValidationException("planKey", "Choose a plan that is on offer.");
        var preset = string.IsNullOrWhiteSpace(request.ModulePreset) ? null : ModuleCatalog.Presets.FirstOrDefault(p => p.Key == request.ModulePreset)
            ?? throw new ValidationException("modulePreset", "That module choice does not exist.");

        var owner = await signup.CreateTenantWithOwnerAsync(new SignupRequest(
            request.CompanyName, request.OwnerFirstName, request.OwnerLastName, request.OwnerEmail, request.Password, null,
            string.IsNullOrWhiteSpace(request.Country) ? "AE" : request.Country, "en", true), cancellationToken);

        // The owner was vouched for by the platform owner, so there is no email to confirm before the first sign-in.
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == owner.Id, cancellationToken);
        user.EmailConfirmed = true;
        var company = await db.Tenants.SingleAsync(t => t.Id == owner.TenantId, cancellationToken);
        company.PlanId = plan.Id;
        company.Status = request.Status;
        company.TrialEndsAt = request.Status == "Trial" ? clock.UtcNow.AddDays(Math.Clamp(request.TrialDays, 1, 365)) : null;
        await RecordAsync(company, "company.created", $"Onboarded {company.Name} on the {plan.Name} plan ({request.Status})", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (preset is not null)
        {
            var allowed = preset.Modules.Select(m => m.ToString()).ToHashSet();
            var off = ModuleCatalog.Switchable.Select(m => m.Module.ToString()).Where(m => !allowed.Contains(m)).ToList();
            if (off.Count > 0) await SetModulesAsync(company.Id, new SetCompanyModulesRequest(off), cancellationToken);
        }

        return await GetCompanyAsync(company.Id, cancellationToken);
    }

    public async Task<PlatformCompanyDetailDto> GetCompanyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var t = await Companies().FirstOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new NotFoundException("Company", id);
        var stats = await LoadStatsAsync([id], now, cancellationToken);
        var company = Map(t, stats, now);
        var plan = stats.Plans.GetValueOrDefault(t.PlanId);

        var tenantOff = await db.ModuleSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.TenantId == id && !m.IsDeleted && !m.IsEnabled).Select(m => m.Module).ToListAsync(cancellationToken);
        var owner = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == id && u.IsTenantOwner).OrderBy(u => u.CreatedAt)
            .Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefaultAsync(cancellationToken);

        return new PlatformCompanyDetailDto(
            company,
            t.MaxUsersOverride ?? plan?.MaxUsers ?? 0,
            plan?.MaxUsers ?? 0,
            t.MaxUsersOverride,
            plan?.MaxRecords ?? 0,
            company.Leads + company.Contacts + company.Accounts + company.Deals,
            t.PlatformNotes,
            t.PlatformDisabledModules,
            tenantOff.Where(m => ModuleCatalog.Find(m) is { Available: true }).Select(m => m.ToString()).ToList(),
            owner is null ? null : $"{owner.FirstName} {owner.LastName}".Trim(),
            owner?.Email,
            await RecentAuditAsync(id, 30, cancellationToken),
            PlanModules.Excluded(plan));
    }

    public async Task<IReadOnlyList<PlatformModuleDto>> ModulesAsync(CancellationToken cancellationToken = default)
    {
        var plans = await db.Plans.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);
        var tenants = (await Companies().ToListAsync(cancellationToken)).Select(t => PlanModules.Disabled(t, plans.GetValueOrDefault(t.PlanId))).ToList();
        return ModuleCatalog.All.Select(m => new PlatformModuleDto(
            m.Module.ToString(), m.Name, m.Description, m.Category, m.Icon, m.Core, m.Available,
            !m.Available ? 0 : tenants.Count(d => m.Core || !d.Contains(m.Module.ToString())), tenants.Count)).ToList();
    }

    public async Task<IReadOnlyList<PlatformPlanDto>> PlansAsync(CancellationToken cancellationToken = default)
    {
        var counts = await Companies().GroupBy(t => t.PlanId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, cancellationToken);
        return (await db.Plans.AsNoTracking().OrderBy(p => p.PriceMonthly).ToListAsync(cancellationToken))
            .Select(p => new PlatformPlanDto(p.Id, p.Key, p.Name, p.MaxUsers, p.MaxRecords, p.PriceMonthly, p.IsActive, counts.GetValueOrDefault(p.Id), p.Description, p.IncludedModules.Length == 0 ? ModuleCatalog.Switchable.Select(m => m.Module.ToString()).ToList() : p.IncludedModules)).ToList();
    }

    private IQueryable<PlatformAuditEntry> AuditQuery(Guid? companyId, string? kind, string? search, DateTime? from, DateTime? to)
    {
        var query = db.PlatformAudit.AsNoTracking().AsQueryable();
        if (companyId is { } id) query = query.Where(a => a.TenantId == id);
        if (!string.IsNullOrWhiteSpace(kind)) { var prefix = kind.Trim().ToLowerInvariant() + "."; query = query.Where(a => a.Action.StartsWith(prefix)); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(a => a.Summary.ToLower().Contains(term) || (a.Company != null && a.Company.ToLower().Contains(term)) || a.Actor.ToLower().Contains(term));
        }

        if (from is { } start) query = query.Where(a => a.CreatedAt >= DateTime.SpecifyKind(start.Date, DateTimeKind.Utc));
        if (to is { } end) query = query.Where(a => a.CreatedAt < DateTime.SpecifyKind(end.Date.AddDays(1), DateTimeKind.Utc));
        return query;
    }

    /// <summary>The audit trail: every change made from the console, newest first, narrowed by company, kind of change, words and dates.</summary>
    public async Task<PagedResult<PlatformAuditDto>> ActivityAsync(int page, int pageSize, CancellationToken cancellationToken = default,
        Guid? companyId = null, string? kind = null, string? search = null, DateTime? from = null, DateTime? to = null)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(page, 1);
        var query = AuditQuery(companyId, kind, search, from, to);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<PlatformAuditDto>(rows.Select(MapAudit).ToList(), total, page, pageSize);
    }

    /// <summary>The same trail as a spreadsheet file (CSV), up to 5,000 rows.</summary>
    public async Task<string> ExportActivityAsync(Guid? companyId, string? kind, string? search, DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var rows = await AuditQuery(companyId, kind, search, from, to).OrderByDescending(a => a.CreatedAt).Take(5000).ToListAsync(cancellationToken);
        static string Cell(string? v)
        {
            v ??= "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;   // a spreadsheet must never run a cell as a formula
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        var sb = new System.Text.StringBuilder("When (UTC),Who,Action,Company,What changed\r\n");
        foreach (var a in rows)
        {
            sb.Append(string.Join(',', Cell(a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), Cell(a.Actor), Cell(a.Action), Cell(a.Company), Cell(a.Summary))).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>What the owner should know about: companies that signed up in the last 14 days, and companies that need a look today.</summary>
    public async Task<IReadOnlyList<PlatformNotificationDto>> NotificationsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var items = new List<PlatformNotificationDto>();

        var recent = await Companies().Where(t => t.CreatedAt >= now.AddDays(-14)).OrderByDescending(t => t.CreatedAt).Take(30).ToListAsync(cancellationToken);
        foreach (var t in recent)
        {
            items.Add(new($"signup:{t.Id}", "signup", "info", "New company", $"{t.Name} joined MindLift.", t.Id, t.Name, t.CreatedAt));
        }

        var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var a in (await OverviewAsync(cancellationToken)).Attention)
        {
            var title = a.Kind switch
            {
                "past-due" => "Payment overdue",
                "suspended" => "Company suspended",
                "trial-ending" => "Trial ending",
                "trial-ended" => "Trial ended",
                "inactive" => "Company gone quiet",
                "seats" => "Seats nearly used",
                _ => "Needs a look"
            };
            // The day is part of the id, so a problem that is still there tomorrow is raised again.
            items.Add(new($"{a.Kind}:{a.TenantId}:{day}", a.Kind, a.Severity, title, $"{a.Company}: {a.Message}", a.TenantId, a.Company, now));
        }

        return items.OrderByDescending(i => i.At).ThenBy(i => i.Severity == "critical" ? 0 : 1).ToList();
    }

    // ------------------------------------------------------------------ changing

    /// <summary>Switches modules off for a company (everything else stays available). Core modules and modules not built yet are refused.</summary>
    public async Task<PlatformCompanyDetailDto> SetModulesAsync(Guid id, SetCompanyModulesRequest request, CancellationToken cancellationToken = default)
    {
        var t = await LoadTenantAsync(id, cancellationToken);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in request.DisabledModules ?? [])
        {
            if (!Enum.TryParse<AppModule>(name, out var module) || ModuleCatalog.Find(module) is not { } info)
            {
                throw new ValidationException("disabledModules", $"\"{name}\" is not a module.");
            }

            if (info.Core)
            {
                throw new ValidationException("disabledModules", $"{info.Name} is a core module and cannot be switched off.");
            }

            if (!info.Available)
            {
                continue;   // not built yet, so already unavailable: nothing to record
            }

            wanted.Add(module.ToString());
        }

        var before = t.PlatformDisabledModules.ToHashSet(StringComparer.Ordinal);
        var turnedOff = wanted.Except(before).OrderBy(x => x).ToList();
        var turnedOn = before.Except(wanted).OrderBy(x => x).ToList();
        if (turnedOff.Count == 0 && turnedOn.Count == 0)
        {
            return await GetCompanyAsync(id, cancellationToken);
        }

        t.PlatformDisabledModules = wanted.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var parts = new List<string>();
        if (turnedOff.Count > 0) parts.Add("switched off " + string.Join(", ", turnedOff.Select(NameOf)));
        if (turnedOn.Count > 0) parts.Add("switched on " + string.Join(", ", turnedOn.Select(NameOf)));
        await RecordAsync(t, "modules.changed", char.ToUpperInvariant(parts[0][0]) + string.Join(" and ", parts)[1..], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await AppliedAsync(t.Id, cancellationToken);
        return await GetCompanyAsync(id, cancellationToken);
    }

    public async Task<PlatformCompanyDetailDto> SetLimitsAsync(Guid id, SetCompanyLimitsRequest request, CancellationToken cancellationToken = default)
    {
        var t = await LoadTenantAsync(id, cancellationToken);
        if (request.MaxUsersOverride is { } max && (max < 1 || max > 100_000))
        {
            throw new ValidationException("maxUsersOverride", "The user limit is between 1 and 100,000, or empty to use the plan's limit.");
        }

        if (request.Notes is { Length: > MaxNotes })
        {
            throw new ValidationException("notes", $"Notes are at most {MaxNotes} characters.");
        }

        var changes = new List<string>();
        if (request.MaxUsersOverride != t.MaxUsersOverride)
        {
            changes.Add(request.MaxUsersOverride is { } n ? $"user limit set to {n}" : "user limit back to the plan's");
            t.MaxUsersOverride = request.MaxUsersOverride;
        }

        if (request.AiReadingAllowed != t.AiReadingAllowed)
        {
            changes.Add(request.AiReadingAllowed ? "AI reading allowed" : "AI reading not allowed");
            t.AiReadingAllowed = request.AiReadingAllowed;
            if (!request.AiReadingAllowed)
            {
                // A company that is no longer allowed AI reading has it switched off too, so nothing keeps reading by AI behind its back.
                var settings = await db.TenantSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == id && !s.IsDeleted, cancellationToken);
                if (settings is not null) settings.AiReadingEnabled = false;
            }
        }

        if (request.TrialEndsAt is { } ends)
        {
            if (t.Status != "Trial")
            {
                throw new ValidationException("trialEndsAt", "Only a company on trial has a trial end date.");
            }

            var utc = DateTime.SpecifyKind(ends, DateTimeKind.Utc);
            if (t.TrialEndsAt is null || Math.Abs((t.TrialEndsAt.Value - utc).TotalMinutes) > 1)
            {
                changes.Add($"trial now ends {utc:d MMM yyyy}");
                t.TrialEndsAt = utc;
                t.TrialReminderSent = false;
            }
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes != t.PlatformNotes)
        {
            changes.Add("notes updated");
            t.PlatformNotes = notes;
        }

        if (changes.Count > 0)
        {
            await RecordAsync(t, "limits.changed", char.ToUpperInvariant(changes[0][0]) + string.Join(", ", changes)[1..], cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await AppliedAsync(t.Id, cancellationToken);
        }

        return await GetCompanyAsync(id, cancellationToken);
    }

    public async Task ChangeStatusAsync(Guid id, string status, CancellationToken cancellationToken = default)
    {
        if (!Statuses.Contains(status))
        {
            throw new ValidationException("status", "Unknown status.");
        }

        var t = await LoadTenantAsync(id, cancellationToken);
        if (t.Status == status)
        {
            return;
        }

        var before = t.Status;
        t.Status = status;
        await RecordAsync(t, "status.changed", $"Status changed from {before} to {status}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePlanAsync(Guid id, string planKey, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == planKey && p.IsActive, cancellationToken)
            ?? throw new ValidationException("planKey", "Unknown plan.");
        var t = await LoadTenantAsync(id, cancellationToken);
        if (t.PlanId == plan.Id)
        {
            return;
        }

        var before = (await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == t.PlanId, cancellationToken))?.Name ?? "no plan";
        t.PlanId = plan.Id;
        await RecordAsync(t, "plan.changed", $"Plan changed from {before} to {plan.Name}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await AppliedAsync(t.Id, cancellationToken);
    }

    public async Task<PlatformPlanDto> SavePlanAsync(Guid id, SavePlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Plan", id);
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 60) errors["name"] = ["Enter a name of up to 60 characters."];
        if (request.MaxUsers is < 1 or > 100_000) errors["maxUsers"] = ["The user limit is between 1 and 100,000."];
        if (request.MaxRecords is < 1 or > 100_000_000) errors["maxRecords"] = ["The record limit is between 1 and 100,000,000."];
        if (request.PriceMonthly is < 0 or > 1_000_000) errors["priceMonthly"] = ["The price is between 0 and 1,000,000."];
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        if (!request.IsActive && plan.IsActive && await db.Tenants.AnyAsync(t => t.PlanId == id, cancellationToken))
        {
            throw new ValidationException("isActive", "Companies are still on this plan. Move them to another plan before retiring it.");
        }

        plan.Name = request.Name.Trim();
        plan.MaxUsers = request.MaxUsers;
        plan.MaxRecords = request.MaxRecords;
        plan.PriceMonthly = request.PriceMonthly;
        plan.IsActive = request.IsActive;
        plan.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var modulesChanged = false;
        if (request.IncludedModules is not null)
        {
            var included = ParseIncluded(request.IncludedModules);
            modulesChanged = !included.OrderBy(x => x).SequenceEqual(plan.IncludedModules.OrderBy(x => x));
            plan.IncludedModules = included;
        }

        await RecordAsync(null, "plan.edited", $"Plan {plan.Name} edited: {plan.MaxUsers} users, {plan.MaxRecords:N0} records, {plan.PriceMonthly:0.##} a month"
            + (modulesChanged ? $", {(plan.IncludedModules.Length == 0 ? "every module" : plan.IncludedModules.Length + " modules")}" : ""), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (modulesChanged)
        {
            await AppliedToPlanAsync(id, cancellationToken);
        }

        return (await PlansAsync(cancellationToken)).First(p => p.Id == id);
    }

    /// <summary>A new plan, with the limits, price and modules the owner chooses.</summary>
    public async Task<PlatformPlanDto> CreatePlanAsync(SavePlanRequest request, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 60) errors["name"] = ["Enter a name of up to 60 characters."];
        if (request.MaxUsers is < 1 or > 100_000) errors["maxUsers"] = ["The user limit is between 1 and 100,000."];
        if (request.MaxRecords is < 1 or > 100_000_000) errors["maxRecords"] = ["The record limit is between 1 and 100,000,000."];
        if (request.PriceMonthly is < 0 or > 1_000_000) errors["priceMonthly"] = ["The price is between 0 and 1,000,000."];
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var baseKey = System.Text.RegularExpressions.Regex.Replace(request.Name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (baseKey.Length == 0) baseKey = "plan";
        var key = baseKey;
        for (var n = 2; await db.Plans.AnyAsync(p => p.Key == key, cancellationToken); n++) key = $"{baseKey}-{n}";

        var plan = new Plan
        {
            Key = key,
            Name = request.Name.Trim(),
            MaxUsers = request.MaxUsers,
            MaxRecords = request.MaxRecords,
            PriceMonthly = request.PriceMonthly,
            IsActive = request.IsActive,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IncludedModules = ParseIncluded(request.IncludedModules ?? []),
        };
        db.Plans.Add(plan);
        await RecordAsync(null, "plan.created", $"Plan {plan.Name} created: {plan.MaxUsers} users, {plan.MaxRecords:N0} records, {plan.PriceMonthly:0.##} a month", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await PlansAsync(cancellationToken)).First(p => p.Id == plan.Id);
    }

    /// <summary>Removes a plan nobody is on.</summary>
    public async Task DeletePlanAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Plan", id);
        if (await db.Tenants.AnyAsync(t => t.PlanId == id, cancellationToken))
        {
            throw new ValidationException("plan", "Companies are on this plan. Move them to another plan first.");
        }

        if (await db.Plans.CountAsync(cancellationToken) <= 1)
        {
            throw new ValidationException("plan", "There must be at least one plan.");
        }

        db.Plans.Remove(plan);
        await RecordAsync(null, "plan.deleted", $"Plan {plan.Name} deleted", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Only modules that can be switched on or off are kept; a list that names all of them (or none) means the plan includes everything.</summary>
    private static string[] ParseIncluded(IEnumerable<string> names)
    {
        var all = ModuleCatalog.Switchable.Select(m => m.Module.ToString()).ToHashSet(StringComparer.Ordinal);
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!all.Contains(name)) throw new ValidationException("includedModules", $"\"{name}\" is not a module that can be included.");
            chosen.Add(name);
        }

        return chosen.Count == 0 || chosen.Count == all.Count ? [] : chosen.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>A plan's modules changed, so every company on it must see that at once.</summary>
    private async Task AppliedToPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        foreach (var companyId in await db.Tenants.Where(t => t.PlanId == planId).Select(t => t.Id).ToListAsync(cancellationToken))
        {
            await AppliedAsync(companyId, cancellationToken);
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Tenant> LoadTenantAsync(Guid id, CancellationToken cancellationToken)
        => await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && t.Slug != PlatformAdminSeeder.PlatformSlug, cancellationToken) ?? throw new NotFoundException("Company", id);

    private static string NameOf(string module) => Enum.TryParse<AppModule>(module, out var m) && ModuleCatalog.Find(m) is { } info ? info.Name : module;

    /// <summary>After an entitlement changed: forget what was remembered for the company and refresh the settings its staff app loaded, so the change shows at once.</summary>
    private async Task AppliedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await entitlements.InvalidateAsync(tenantId, cancellationToken);
        using (tenant.Use(tenantId))
        {
            await settingsVersion.BumpAsync(cancellationToken);
        }
    }

    private async Task RecordAsync(Tenant? company, string action, string summary, CancellationToken cancellationToken)
    {
        string actor = "Platform";
        Guid? actorId = tenant.UserId;
        if (actorId is { } userId)
        {
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FirstName, u.LastName, u.Email }).FirstOrDefaultAsync(cancellationToken);
            if (user is not null)
            {
                actor = $"{user.FirstName} {user.LastName}".Trim();
                if (actor.Length == 0) actor = user.Email ?? "Platform";
            }
        }

        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            TenantId = company?.Id,
            Company = company?.Name,
            ActorId = actorId,
            Actor = actor,
            Action = action,
            Summary = summary,
            CreatedAt = clock.UtcNow,
        });
    }

    private async Task<IReadOnlyList<PlatformAuditDto>> RecentAuditAsync(Guid? tenantId, int take, CancellationToken cancellationToken)
    {
        var query = db.PlatformAudit.AsNoTracking().AsQueryable();
        if (tenantId is { } id)
        {
            query = query.Where(a => a.TenantId == id);
        }

        return (await query.OrderByDescending(a => a.CreatedAt).Take(take).ToListAsync(cancellationToken)).Select(MapAudit).ToList();
    }

    private static PlatformAuditDto MapAudit(PlatformAuditEntry a) => new(a.Id, a.TenantId, a.Company, a.Actor, a.Action, a.Summary, a.CreatedAt);

    private async Task<Stats> LoadStatsAsync(IReadOnlyList<Guid> ids, DateTime now, CancellationToken cancellationToken)
    {
        var stats = new Stats();
        var cutoff = now.AddDays(-7);

        foreach (var u in await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && ids.Contains(u.TenantId))
            .GroupBy(u => u.TenantId)
            // A person who has not signed in yet counts from the day their account was made, so a company that just signed up is not shown as dormant.
            .Select(g => new { Id = g.Key, Users = g.Count(), Active = g.Count(x => (x.LastLoginAt ?? x.CreatedAt) >= cutoff), Last = (DateTime?)g.Max(x => x.LastLoginAt ?? x.CreatedAt) })
            .ToListAsync(cancellationToken))
        {
            stats.Users[u.Id] = (u.Users, u.Active, u.Last);
        }

        async Task FillAsync<T>(Dictionary<Guid, int> into) where T : TenantEntity
        {
            foreach (var row in await db.Set<T>().IgnoreQueryFilters().AsNoTracking()
                .Where(x => !x.IsDeleted && ids.Contains(x.TenantId))
                .GroupBy(x => x.TenantId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(cancellationToken))
            {
                into[row.Key] = row.N;
            }
        }

        await FillAsync<Lead>(stats.Leads);
        await FillAsync<Contact>(stats.Contacts);
        await FillAsync<Account>(stats.Accounts);
        await FillAsync<Deal>(stats.Deals);
        await FillAsync<BusinessDocument>(stats.Documents);

        foreach (var s in await db.TenantSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => !s.IsDeleted && ids.Contains(s.TenantId)).Select(s => new { s.TenantId, s.AiReadingEnabled }).ToListAsync(cancellationToken))
        {
            stats.AiEnabled[s.TenantId] = s.AiReadingEnabled;
        }

        foreach (var p in await db.Plans.AsNoTracking().OrderBy(p => p.PriceMonthly).ToListAsync(cancellationToken))
        {
            stats.Plans[p.Id] = p;
            stats.PlanById[p.Id] = p.Key;
        }

        return stats;
    }

    private static PlatformCompanyDto Map(Tenant t, Stats s, DateTime now)
    {
        var (users, active, last) = s.Users.GetValueOrDefault(t.Id);
        var plan = s.Plans.GetValueOrDefault(t.PlanId);
        var leads = s.Leads.GetValueOrDefault(t.Id);
        var contacts = s.Contacts.GetValueOrDefault(t.Id);
        var accounts = s.Accounts.GetValueOrDefault(t.Id);
        var deals = s.Deals.GetValueOrDefault(t.Id);
        var documents = s.Documents.GetValueOrDefault(t.Id);
        var offered = ModuleCatalog.Offered;
        var disabled = PlanModules.Disabled(t, plan);
        var enabled = offered.Count(m => m.Core || !disabled.Contains(m.Module.ToString()));

        return new PlatformCompanyDto(
            t.Id, t.Name, t.Slug, t.Status, plan?.Key ?? string.Empty, plan?.Name ?? string.Empty,
            users, active, leads, contacts, accounts, deals, documents,
            enabled, offered.Count, t.AiReadingAllowed, s.AiEnabled.GetValueOrDefault(t.Id),
            t.TrialEndsAt, t.CreatedAt, last, t.Country, t.IndustryKey,
            Health(t, last, users, leads + contacts + accounts + deals, enabled, offered.Count, now),
            disabled.Where(m => Enum.TryParse<AppModule>(m, out var module) && ModuleCatalog.Find(module) is { Available: true, Core: false }).ToList());
    }

    /// <summary>
    /// A rough 0 to 100 sign of how alive a company is: how recently anyone signed in (up to 45), how much data it holds (25), whether more than one person uses it (15)
    /// and how much of the product it has (15). A suspended or cancelled company is capped low. It is for spotting who to look at, not for billing.
    /// </summary>
    private static int Health(Tenant t, DateTime? last, int users, int records, int modules, int totalModules, DateTime now)
    {
        var recency = last is null ? 0 : (now - last.Value).TotalDays switch { <= 2 => 45, <= 7 => 35, <= 14 => 22, <= 30 => 10, _ => 0 };
        var data = records == 0 ? 0 : records >= 50 ? 25 : records >= 10 ? 18 : 10;
        var team = users >= 3 ? 15 : users == 2 ? 10 : 0;
        var breadth = totalModules == 0 ? 0 : (int)Math.Round(15.0 * modules / totalModules);
        var score = recency + data + team + breadth;
        return t.Status is "Suspended" or "Cancelled" ? Math.Min(score, 20) : Math.Clamp(score, 0, 100);
    }
}
