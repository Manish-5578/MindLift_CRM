using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>
/// The platform console (plan 6.10) through the real API and database: the numbers the owner sees for each company, and the switches the owner has
/// (modules, AI reading, user limit, trial, status, plan), each taking effect on the company's very next request and each written to the history.
/// </summary>
public sealed class PlatformConsoleTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static readonly string[] AllModules = [.. ModuleCatalog.Switchable.Select(m => m.Module.ToString())];

    private async Task<Session> PlatformAsync()
    {
        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync(email, Workspaces.Password, "Priya", "Owner");
        }

        return await Workspaces.LoginAsync(factory, email, Workspaces.Password);
    }

    private static async Task<Guid> TenantIdAsync(Session company) => (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;

    private static async Task<T> SendAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<PlatformCompanyDetailDto> SetModulesAsync(Session platform, Guid id, params string[] off)
        => SendAsync<PlatformCompanyDetailDto>(platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/modules", new SetCompanyModulesRequest(off)).GetAwaiter().GetResult());

    private static Task<PlatformCompanyDetailDto> DetailAsync(Session platform, Guid id)
        => platform.Client.GetFromJsonAsync<PlatformCompanyDetailDto>($"/api/v1/platform/companies/{id}")!;

    private async Task<(Session Platform, Session Company, Guid Id)> SetUpAsync(string? name = null)
    {
        var platform = await PlatformAsync();
        var company = await Workspaces.SignupAsync(factory, name);
        return (platform, company, await TenantIdAsync(company));
    }

    // ---------- Numbers ----------

    [Fact]
    public async Task The_company_list_shows_each_company_with_its_plan_users_and_how_much_data_it_holds()
    {
        var (platform, company, id) = await SetUpAsync($"Counted Co {Guid.NewGuid():N}"[..24]);
        for (var i = 0; i < 3; i++)
        {
            (await company.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest($"Person{i}", "Counted", null, null, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        }

        var list = await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?pageSize=200");

        var row = list!.Items.Single(c => c.Id == id);
        row.Contacts.ShouldBe(3);
        row.Users.ShouldBe(1);
        row.ActiveUsers7d.ShouldBe(1);
        row.PlanKey.ShouldNotBeNullOrEmpty();
        row.Status.ShouldBe("Trial");
        row.LastActiveAt.ShouldNotBeNull();
        row.ModulesEnabled.ShouldBe(row.ModulesTotal);
        row.Health.ShouldBeInRange(1, 100);
    }

    [Fact]
    public async Task The_platforms_own_workspace_is_not_listed_as_a_customer()
    {
        var platform = await PlatformAsync();

        var list = await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?pageSize=200");

        list!.Items.ShouldNotContain(c => c.Slug == PlatformAdminSeeder.PlatformSlug);
    }

    [Fact]
    public async Task The_list_can_be_searched_filtered_by_status_and_sorted()
    {
        var (platform, _, id) = await SetUpAsync($"Findable {Guid.NewGuid():N}"[..20]);
        var detail = await DetailAsync(platform, id);

        var byName = await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>($"/api/v1/platform/companies?search={Uri.EscapeDataString(detail.Company.Name[..12])}");
        byName!.Items.ShouldContain(c => c.Id == id);

        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Suspended"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var suspended = await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?status=Suspended&pageSize=200");
        suspended!.Items.ShouldContain(c => c.Id == id);
        suspended.Items.ShouldAllBe(c => c.Status == "Suspended");
        suspended.Statuses.Single(s => s.Key == "Suspended").Value.ShouldBeGreaterThanOrEqualTo(1);

        var newest = await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?sort=newest&pageSize=200");
        newest!.Items.Select(c => c.CreatedAt).ShouldBe(newest.Items.Select(c => c.CreatedAt).OrderByDescending(d => d));
    }

    [Fact]
    public async Task The_overview_counts_companies_by_status_and_plan_and_adds_up()
    {
        var (platform, _, _) = await SetUpAsync();

        var overview = await platform.Client.GetFromJsonAsync<PlatformOverviewDto>("/api/v1/platform/overview");

        overview!.Companies.ShouldBeGreaterThanOrEqualTo(1);
        overview.Companies.ShouldBe(overview.ByStatus.Sum(s => s.Value));
        overview.Companies.ShouldBe(overview.ByPlan.Sum(p => p.Value));
        overview.Signups.Count.ShouldBe(30);
        overview.Signups.Sum(p => p.Value).ShouldBe(overview.SignupsLast30);
        overview.Users.ShouldBeGreaterThanOrEqualTo(1);
        overview.ModuleAdoption.ShouldAllBe(m => m.Companies <= m.Total);
    }

    [Fact]
    public async Task A_trial_ending_soon_or_overdue_payment_is_flagged_for_attention()
    {
        var (platform, _, id) = await SetUpAsync($"Needs Eyes {Guid.NewGuid():N}"[..22]);
        var end = DateTime.UtcNow.AddDays(2);
        await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, null, end)));

        var soon = await platform.Client.GetFromJsonAsync<PlatformOverviewDto>("/api/v1/platform/overview");
        soon!.Attention.ShouldContain(a => a.TenantId == id && a.Kind == "trial-ending");

        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("PastDue"))).EnsureSuccessStatusCode();
        var late = await platform.Client.GetFromJsonAsync<PlatformOverviewDto>("/api/v1/platform/overview");
        late!.Attention.ShouldContain(a => a.TenantId == id && a.Kind == "past-due" && a.Severity == "critical");
    }

    [Fact]
    public async Task A_suspended_company_health_is_capped_low()
    {
        var (platform, company, id) = await SetUpAsync();
        (await company.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("A", "B", null, null, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Suspended"))).EnsureSuccessStatusCode();

        (await DetailAsync(platform, id)).Company.Health.ShouldBeLessThanOrEqualTo(20);
    }

    // ---------- Modules ----------

    [Fact]
    public async Task Switching_a_module_off_refuses_it_on_the_companys_very_next_request_and_switching_it_on_restores_it()
    {
        var (platform, company, id) = await SetUpAsync();
        (await company.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var off = await SetModulesAsync(platform, id, "Documents");

        off.DisabledModules.ShouldBe(["Documents"]);
        off.Company.ModulesEnabled.ShouldBe(off.Company.ModulesTotal - 1);
        (await company.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await company.Client.GetAsync("/api/v1/documents/types")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await SetModulesAsync(platform, id);
        (await company.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_module_switched_off_is_missing_from_the_companys_menu_data_and_marked_locked_in_its_own_settings()
    {
        var (platform, company, id) = await SetUpAsync();
        await SetModulesAsync(platform, id, "Quotations", "Invoices");

        var bootstrap = (await company.Client.GetFromJsonAsync<BootstrapDto>("/api/v1/app/bootstrap"))!;

        bootstrap.Permissions.Single(p => p.Module == "Quotations").CanView.ShouldBeFalse();
        bootstrap.Permissions.Single(p => p.Module == "Leads").CanView.ShouldBeTrue();
        var quotations = bootstrap.Modules.Single(m => m.Module == "Quotations");
        quotations.IsEnabled.ShouldBeFalse();
        quotations.PlatformLocked.ShouldBeTrue();
        bootstrap.Modules.Single(m => m.Module == "Leads").PlatformLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_company_cannot_switch_on_a_module_the_platform_took_away()
    {
        var (platform, company, id) = await SetUpAsync();
        await SetModulesAsync(platform, id, "Products");

        var response = await company.Client.PutAsJsonAsync("/api/v1/settings/modules/Products", new SaveModuleSettingRequest(true, "Product", "Products", 12));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("module_not_in_plan");
    }

    [Fact]
    public async Task Core_modules_cannot_be_switched_off_and_unknown_names_are_refused()
    {
        var (platform, _, id) = await SetUpAsync();

        foreach (var core in new[] { "Settings", "Users", "Dashboard" })
        {
            (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/modules", new SetCompanyModulesRequest([core]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/modules", new SetCompanyModulesRequest(["Teleportation"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await DetailAsync(platform, id)).DisabledModules.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_module_still_on_the_roadmap_is_ignored_because_it_is_unavailable_to_everyone_already()
    {
        var (platform, _, id) = await SetUpAsync();

        var detail = await SetModulesAsync(platform, id, "Projects", "Integrations");

        detail.DisabledModules.ShouldBeEmpty();
    }

    [Fact]
    public async Task Switching_Automation_off_refuses_its_api()
    {
        var (platform, company, id) = await SetUpAsync();
        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetModulesAsync(platform, id, "Automation");

        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Switching_the_customer_portal_off_makes_the_public_portal_unavailable()
    {
        var (platform, company, id) = await SetUpAsync();
        var slug = (await DetailAsync(platform, id)).Company.Slug;
        var anonymous = Workspaces.MobileClient(factory);
        (await company.Client.PutAsJsonAsync("/api/v1/settings/portal", new SavePortalSettingsRequest(true, null, null, true, true, true, null, null))).EnsureSuccessStatusCode();
        (await anonymous.GetAsync($"/api/v1/portal/{slug}/branding")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await SetModulesAsync(platform, id, "Portal");

        (await anonymous.GetAsync($"/api/v1/portal/{slug}/branding")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await SetModulesAsync(platform, id);
        (await anonymous.GetAsync($"/api/v1/portal/{slug}/branding")).StatusCode.ShouldBe(HttpStatusCode.OK);
        _ = company;
    }

    [Fact]
    public async Task One_company_losing_a_module_does_not_affect_another()
    {
        var platform = await PlatformAsync();
        var first = await Workspaces.SignupAsync(factory);
        var second = await Workspaces.SignupAsync(factory);

        await SetModulesAsync(platform, await TenantIdAsync(first), "Documents");

        (await first.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await second.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_catalogue_lists_every_module_with_how_many_companies_have_it()
    {
        var (platform, _, id) = await SetUpAsync();
        await SetModulesAsync(platform, id, "Reports");

        var modules = await platform.Client.GetFromJsonAsync<List<PlatformModuleDto>>("/api/v1/platform/modules");

        modules!.Count.ShouldBe(ModuleCatalog.All.Count);
        var reports = modules.Single(m => m.Key == "Reports");
        reports.Companies.ShouldBeLessThan(reports.Total);
        modules.Single(m => m.Key == "Settings").Companies.ShouldBe(modules.Single(m => m.Key == "Settings").Total);
        modules.Single(m => m.Key == "Projects").Available.ShouldBeFalse();
    }

    [Fact]
    public async Task The_ready_made_module_choices_only_name_modules_that_exist_and_always_keep_the_core()
    {
        var platform = await PlatformAsync();

        var presets = await platform.Client.GetFromJsonAsync<List<ModulePresetDto>>("/api/v1/platform/module-presets");

        presets!.Count.ShouldBeGreaterThanOrEqualTo(4);
        foreach (var preset in presets)
        {
            preset.Modules.ShouldAllBe(m => Enum.IsDefined(typeof(AppModule), m));
            preset.Modules.ShouldContain("Settings");
            preset.Modules.ShouldContain("Dashboard");
            preset.Modules.ShouldContain("Users");
        }

        presets.Single(p => p.Key == "full").Modules.Count.ShouldBe(ModuleCatalog.Offered.Count);
    }

    // ---------- Limits ----------

    [Fact]
    public async Task A_user_limit_set_by_the_owner_replaces_the_plans_and_removing_it_restores_the_plans()
    {
        var (platform, company, id) = await SetUpAsync();
        var roles = await company.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staff = roles!.Single(r => r.Name == "Staff").Id;
        await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(1, true, null, null)));

        var refused = await company.Client.PostAsJsonAsync("/api/v1/invitations", new CreateInvitationRequest($"one-{Guid.NewGuid():N}@example.test", "A", "B", staff, null));
        refused.StatusCode.ShouldBe((HttpStatusCode)402);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("limited to 1 users");

        var back = await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, null, null)));
        back.MaxUsersOverride.ShouldBeNull();
        back.MaxUsers.ShouldBe(back.PlanMaxUsers);
        (await company.Client.PostAsJsonAsync("/api/v1/invitations", new CreateInvitationRequest($"two-{Guid.NewGuid():N}@example.test", "A", "B", staff, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bad_limits_are_refused()
    {
        var (platform, _, id) = await SetUpAsync();

        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(0, true, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, new string('x', 2001), null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_trial_can_be_extended_but_only_for_a_company_on_trial()
    {
        var (platform, _, id) = await SetUpAsync();
        var later = DateTime.UtcNow.AddDays(30);

        var extended = await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, null, later)));
        extended.Company.TrialEndsAt!.Value.ShouldBe(later, TimeSpan.FromMinutes(1));

        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Active"))).EnsureSuccessStatusCode();
        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, null, later.AddDays(5)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Not_allowing_AI_reading_switches_it_off_for_the_company_and_stops_it_being_switched_on()
    {
        var (platform, company, id) = await SetUpAsync();
        factory.Llm.Reset();
        (await company.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true))).EnsureSuccessStatusCode();
        (await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?pageSize=200"))!.Items.Single(c => c.Id == id).AiEnabled.ShouldBeTrue();

        await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, false, null, null)));

        var status = (await company.Client.GetFromJsonAsync<DocumentAiStatusDto>("/api/v1/settings/document-types/ai"))!;
        status.Allowed.ShouldBeFalse();
        status.Enabled.ShouldBeFalse();
        status.Message!.ShouldContain("not included");
        var again = await company.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true));
        again.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await DetailAsync(platform, id)).Company.AiEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Notes_are_kept_for_the_owner_and_never_shown_to_the_company()
    {
        var (platform, company, id) = await SetUpAsync();

        var detail = await SendAsync<PlatformCompanyDetailDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(null, true, "  Pilot customer, call on Monday  ", null)));

        detail.Notes.ShouldBe("Pilot customer, call on Monday");
        (await company.Client.GetStringAsync("/api/v1/app/bootstrap")).ShouldNotContain("Pilot customer");
    }

    // ---------- Status and plans ----------

    [Fact]
    public async Task A_plan_can_be_changed_and_an_unknown_one_is_refused()
    {
        var (platform, _, id) = await SetUpAsync();

        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest("enterprise"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DetailAsync(platform, id)).Company.PlanKey.ShouldBe("enterprise");

        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest("platinum-deluxe"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Imaginary"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Plans_can_be_edited_and_a_plan_in_use_cannot_be_retired()
    {
        var (platform, _, id) = await SetUpAsync();
        var plans = (await platform.Client.GetFromJsonAsync<List<PlatformPlanDto>>("/api/v1/platform/plans"))!;
        var inUse = plans.Single(p => p.Key == (DetailAsync(platform, id).GetAwaiter().GetResult()).Company.PlanKey);
        inUse.Companies.ShouldBeGreaterThanOrEqualTo(1);

        var edited = await SendAsync<PlatformPlanDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{inUse.Id}", new SavePlanRequest(inUse.Name, inUse.MaxUsers + 1, inUse.MaxRecords, inUse.PriceMonthly, true)));
        edited.MaxUsers.ShouldBe(inUse.MaxUsers + 1);

        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{inUse.Id}", new SavePlanRequest(inUse.Name, inUse.MaxUsers, inUse.MaxRecords, inUse.PriceMonthly, false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{inUse.Id}", new SavePlanRequest("", 0, -1, -5, true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- History ----------

    [Fact]
    public async Task Every_change_is_written_to_the_history_with_who_made_it_and_what_changed()
    {
        var (platform, _, id) = await SetUpAsync();

        await SetModulesAsync(platform, id, "Documents", "Portal");
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Active"));
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest("starter"));
        await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(25, true, null, null));

        var history = (await DetailAsync(platform, id)).History;

        history.Select(h => h.Action).ShouldBe(["limits.changed", "plan.changed", "status.changed", "modules.changed"], ignoreOrder: true);
        history.ShouldAllBe(h => h.Actor == "Priya Owner");
        history.Single(h => h.Action == "modules.changed").Summary.ShouldContain("Documents");
        history.Single(h => h.Action == "status.changed").Summary.ShouldContain("Active");
        history.Single(h => h.Action == "limits.changed").Summary.ShouldContain("25");

        var feed = await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>("/api/v1/platform/activity?pageSize=100");
        feed!.Items.ShouldContain(a => a.TenantId == id && a.Action == "modules.changed");
    }

    [Fact]
    public async Task Changing_nothing_writes_nothing_to_the_history()
    {
        var (platform, _, id) = await SetUpAsync();
        var before = (await DetailAsync(platform, id)).History.Count;

        await SetModulesAsync(platform, id);
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Trial"));

        (await DetailAsync(platform, id)).History.Count.ShouldBe(before);
    }

    // ---------- Who may use it ----------

    [Fact]
    public async Task Only_a_platform_administrator_reaches_any_of_it()
    {
        var company = await Workspaces.SignupAsync(factory);
        var id = await TenantIdAsync(company);

        foreach (var path in new[] { "overview", "companies", $"companies/{id}", "modules", "module-presets", "plans", "activity" })
        {
            (await company.Client.GetAsync($"/api/v1/platform/{path}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        (await company.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/modules", new SetCompanyModulesRequest(["Documents"]))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await company.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/limits", new SetCompanyLimitsRequest(1, true, null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Workspaces.MobileClient(factory).GetAsync("/api/v1/platform/overview")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_company_that_does_not_exist_is_not_found_and_the_platform_workspace_cannot_be_edited()
    {
        var platform = await PlatformAsync();

        (await platform.Client.GetAsync($"/api/v1/platform/companies/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{Guid.NewGuid()}/modules", new SetCompanyModulesRequest([]))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var me = await platform.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        (await platform.Client.GetAsync($"/api/v1/platform/companies/{me!.TenantId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_company_admin_still_runs_its_own_workspace_with_every_module_the_owner_left_on()
    {
        var (platform, company, id) = await SetUpAsync();
        await SetModulesAsync(platform, id, AllModules.Where(m => m is not ("Contacts" or "Activities")).ToArray());

        (await company.Client.GetAsync("/api/v1/contacts/export".Replace("/export", string.Empty))).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        (await company.Client.GetAsync("/api/v1/settings/modules")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await company.Client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await company.Client.GetAsync("/api/v1/documents")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
