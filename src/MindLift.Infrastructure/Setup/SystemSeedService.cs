using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Setup;

/// <summary>
/// Creates the base configuration for a new tenant (plan 6.2 step 5): roles and their permissions,
/// module settings, and the base picklists. Must run inside a tenant scope.
/// </summary>
public sealed class SystemSeedService(IAppDbContext db)
{
    /// <summary>Modules available in Phase 1. Others exist in the enum but start disabled.</summary>
    public static readonly AppModule[] Phase1Modules =
    [
        AppModule.Dashboard, AppModule.Leads, AppModule.Contacts, AppModule.Accounts, AppModule.Deals,
        AppModule.Activities, AppModule.Calendar, AppModule.Communication, AppModule.Reports,
        AppModule.Notifications, AppModule.Users, AppModule.Settings, AppModule.Documents,
        AppModule.Products, AppModule.Quotations, AppModule.Invoices, AppModule.Tickets, AppModule.Campaigns,
    ];

    /// <summary>Record modules where Staff and Manager can create and edit.</summary>
    private static readonly AppModule[] RecordModules =
    [
        AppModule.Leads, AppModule.Contacts, AppModule.Accounts, AppModule.Deals,
        AppModule.Activities, AppModule.Calendar, AppModule.Communication, AppModule.Documents,
        AppModule.Products, AppModule.Quotations, AppModule.Invoices, AppModule.Tickets,
    ];

    public async Task SeedTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var admin = AddRole("Admin", "Full access to everything, including settings and users.", isAdmin: true, DashboardView.Admin);
        var manager = AddRole("Manager", "Manages a team. Sees the team's records and reports.", isAdmin: false, DashboardView.Manager);
        var staff = AddRole("Staff", "Works on their own records.", isAdmin: false, DashboardView.Staff);

        foreach (var module in Enum.GetValues<AppModule>())
        {
            db.Set<RolePermission>().Add(AdminPermission(admin.Id, module));
            db.Set<RolePermission>().Add(ManagerPermission(manager.Id, module));
            db.Set<RolePermission>().Add(StaffPermission(staff.Id, module));
        }

        foreach (var module in Enum.GetValues<AppModule>())
        {
            db.Set<ModuleSetting>().Add(new ModuleSetting
            {
                TenantId = tenantId,
                Module = module,
                IsEnabled = Phase1Modules.Contains(module),
                SingularLabel = Labels.Singular(module),
                PluralLabel = Labels.Plural(module),
                SortOrder = (int)module,
            });
        }

        foreach (var picklist in BasePicklists.All)
        {
            var order = 0;
            foreach (var item in picklist.Values)
            {
                db.Set<PicklistValue>().Add(new PicklistValue
                {
                    TenantId = tenantId,
                    PicklistKey = picklist.Key,
                    Value = item.Value,
                    Label = item.Label,
                    LabelAr = item.LabelAr,
                    Category = item.Category,
                    SortOrder = order++,
                    IsDefault = item.IsDefault,
                    IsSystem = true,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Id of the tenant's Admin role.</summary>
    public async Task<Guid> AdminRoleIdAsync(CancellationToken cancellationToken = default)
        => await db.Set<Role>().Where(r => r.IsAdmin).Select(r => r.Id).SingleAsync(cancellationToken);

    private Role AddRole(string name, string description, bool isAdmin, DashboardView view)
    {
        var role = new Role
        {
            Name = name,
            Description = description,
            IsSystem = true,
            IsAdmin = isAdmin,
            DashboardView = view,
        };
        db.Set<Role>().Add(role);
        return role;
    }

    private static RolePermission AdminPermission(Guid roleId, AppModule module) => new()
    {
        RoleId = roleId,
        Module = module,
        CanView = true,
        CanCreate = true,
        CanEdit = true,
        CanDelete = true,
        CanExport = true,
        CanImport = true,
        Scope = DataScope.All,
    };

    private static RolePermission ManagerPermission(Guid roleId, AppModule module)
    {
        if (module is AppModule.Settings)
        {
            return None(roleId, module);
        }

        if (module is AppModule.Users)
        {
            return new RolePermission { RoleId = roleId, Module = module, CanView = true, Scope = DataScope.Team };
        }

        var record = RecordModules.Contains(module);
        return new RolePermission
        {
            RoleId = roleId,
            Module = module,
            CanView = true,
            CanCreate = record,
            CanEdit = record,
            CanDelete = record,
            CanExport = record,
            CanImport = record,
            Scope = DataScope.Team,
        };
    }

    private static RolePermission StaffPermission(Guid roleId, AppModule module)
    {
        if (module is AppModule.Settings or AppModule.Users)
        {
            return None(roleId, module);
        }

        var record = RecordModules.Contains(module);
        return new RolePermission
        {
            RoleId = roleId,
            Module = module,
            CanView = true,
            CanCreate = record,
            CanEdit = record,
            CanDelete = false,
            CanExport = false,
            CanImport = false,
            Scope = DataScope.Own,
        };
    }

    private static RolePermission None(Guid roleId, AppModule module) => new()
    {
        RoleId = roleId,
        Module = module,
        Scope = DataScope.Own,
    };
}

/// <summary>Human-readable module names, used as default labels (plan 4.4, module_settings).</summary>
public static class Labels
{
    public static string Singular(AppModule module) => module switch
    {
        AppModule.Activities => "Activity",
        AppModule.Accounts => "Account",
        _ => Plural(module).TrimEnd('s'),
    };

    public static string Plural(AppModule module) => module switch
    {
        AppModule.Dashboard => "Dashboard",
        AppModule.Communication => "Communication",
        AppModule.Calendar => "Calendar",
        AppModule.Settings => "Settings",
        AppModule.Reports => "Reports",
        AppModule.Notifications => "Notifications",
        AppModule.Users => "Users",
        _ => module.ToString(),
    };
}

/// <summary>Base picklist values in English and Arabic (plan 5.3). Categories carry meaning; labels do not.</summary>
public static class BasePicklists
{
    public sealed record Item(string Value, string Label, string LabelAr, string? Category = null, bool IsDefault = false);

    public sealed record Set(string Key, Item[] Values);

    public static readonly Set[] All =
    [
        new("lead_status",
        [
            new("new", "New", "جديد", "New", IsDefault: true),
            new("working", "Contacted", "تم التواصل", "Working"),
            new("qualified", "Qualified", "مؤهل", "Qualified"),
            new("disqualified", "Not interested", "غير مهتم", "Disqualified"),
            new("converted", "Converted", "تم التحويل", "Converted"),
        ]),
        new("lead_source",
        [
            new("website", "Website", "الموقع الإلكتروني"),
            new("referral", "Referral", "إحالة"),
            new("social_media", "Social media", "وسائل التواصل"),
            new("walk_in", "Walk-in", "زيارة مباشرة"),
            new("campaign", "Campaign", "حملة"),
            new("whatsapp", "WhatsApp", "واتساب"),
            new("email", "Email", "البريد الإلكتروني"),
            new("phone", "Phone", "هاتف"),
            new("booking", "Booking", "حجز"),
            new("import", "Import", "استيراد"),
            new("other", "Other", "أخرى", IsDefault: true),
        ]),
        new("lead_rating",
        [
            new("hot", "Hot", "ساخن"),
            new("warm", "Warm", "دافئ"),
            new("cold", "Cold", "بارد"),
        ]),
        new("contact_type",
        [
            new("client", "Client", "عميل", "Client"),
            new("prospect", "Prospect", "عميل محتمل", "Prospect", IsDefault: true),
            new("partner", "Partner", "شريك", "Partner"),
            new("supplier", "Supplier", "مورد", "Supplier"),
        ]),
        new("lost_reason",
        [
            new("price", "Price", "السعر"),
            new("competitor", "Competitor", "منافس"),
            new("no_budget", "No budget", "لا توجد ميزانية"),
            new("no_response", "No response", "لا رد"),
            new("timing", "Timing", "التوقيت"),
            new("other", "Other", "أخرى", IsDefault: true),
        ]),
        new("call_outcome",
        [
            new("connected", "Connected", "متصل"),
            new("no_answer", "No answer", "لا رد"),
            new("busy", "Busy", "مشغول"),
            new("left_voicemail", "Left voicemail", "ترك رسالة صوتية"),
            new("wrong_number", "Wrong number", "رقم خاطئ"),
        ]),
    ];
}

/// <summary>Defaults applied at sign-up from the country (plan 6.2 table).</summary>
public sealed record CountryDefaults(string Currency, string TimeZoneId, string TaxLabel, decimal TaxRate, DayOfWeek WeekStart)
{
    public static CountryDefaults For(string country) => country.ToUpperInvariant() switch
    {
        "AE" => new("AED", "Asia/Dubai", "VAT", 5, DayOfWeek.Monday),
        "SA" => new("SAR", "Asia/Riyadh", "VAT", 15, DayOfWeek.Sunday),
        "QA" => new("QAR", "Asia/Qatar", "Tax", 0, DayOfWeek.Sunday),
        "OM" => new("OMR", "Asia/Muscat", "VAT", 5, DayOfWeek.Sunday),
        "BH" => new("BHD", "Asia/Bahrain", "VAT", 10, DayOfWeek.Sunday),
        "KW" => new("KWD", "Asia/Kuwait", "Tax", 0, DayOfWeek.Sunday),
        "IN" => new("INR", "Asia/Kolkata", "GST", 18, DayOfWeek.Monday),
        "GB" => new("GBP", "Europe/London", "VAT", 20, DayOfWeek.Monday),
        "US" => new("USD", "America/New_York", "Sales tax", 0, DayOfWeek.Sunday),
        _ => new("USD", "UTC", "Tax", 0, DayOfWeek.Monday),
    };
}
