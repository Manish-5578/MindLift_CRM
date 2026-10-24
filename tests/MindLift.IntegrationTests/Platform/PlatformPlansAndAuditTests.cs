using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>Plans the owner shapes (limits, price, modules), the audit trail and the owner's notifications.</summary>
public sealed class PlatformPlansAndAuditTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private async Task<Session> PlatformAsync()
    {
        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync(email, Workspaces.Password, "Priya", "Owner");
        }

        return await Workspaces.LoginAsync(factory, email, Workspaces.Password);
    }

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.ShouldBe(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static Task<PlatformPlanDto> NewPlanAsync(Session p, string name, params string[] modules)
        => Ok<PlatformPlanDto>(p.Client.PostAsJsonAsync("/api/v1/platform/plans", new SavePlanRequest(name, 5, 1000, 29, true, "A plan for tests", modules)).GetAwaiter().GetResult());

    [Fact]
    public async Task The_owner_creates_a_plan_with_its_own_modules_and_companies_on_it_get_only_those()
    {
        var platform = await PlatformAsync();
        var plan = await NewPlanAsync(platform, $"Lean {Guid.NewGuid():N}"[..14], "Leads", "Contacts", "Deals");
        plan.IncludedModules!.Count.ShouldBe(3);
        plan.Description.ShouldBe("A plan for tests");

        var company = await Workspaces.SignupAsync(factory);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        (await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest(plan.Key))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = (await platform.Client.GetFromJsonAsync<PlatformCompanyDetailDto>($"/api/v1/platform/companies/{id}"))!;
        detail.PlanExcludedModules!.ShouldContain("Invoices");
        detail.PlanExcludedModules!.ShouldNotContain("Deals");
        detail.Company.ModulesEnabled.ShouldBeLessThan(detail.Company.ModulesTotal);

        // The company feels it at once: its own API refuses an excluded module and allows an included one.
        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var modules = (await company.Client.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules"))!;
        modules.Single(m => m.Module == "Invoices").PlatformLocked.ShouldBeTrue();
        modules.Single(m => m.Module == "Deals").PlatformLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Changing_the_modules_of_a_plan_changes_every_company_on_it_straight_away()
    {
        var platform = await PlatformAsync();
        var plan = await NewPlanAsync(platform, $"Wide {Guid.NewGuid():N}"[..14]);   // every module
        var company = await Workspaces.SignupAsync(factory);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest(plan.Key));
        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var narrowed = await Ok<PlatformPlanDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{plan.Id}",
            new SavePlanRequest(plan.Name, 5, 1000, 29, true, null, new[] { "Leads", "Contacts" })));
        narrowed.IncludedModules!.Count.ShouldBe(2);
        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A name that is not a module is refused; an empty list means every module again.
        (await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{plan.Id}", new SavePlanRequest(plan.Name, 5, 1000, 29, true, null, new[] { "Nope" }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Ok<PlatformPlanDto>(await platform.Client.PutAsJsonAsync($"/api/v1/platform/plans/{plan.Id}", new SavePlanRequest(plan.Name, 5, 1000, 29, true, null, Array.Empty<string>())));
        (await company.Client.GetAsync("/api/v1/automation/rules")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_plan_in_use_cannot_be_deleted_and_an_unused_one_can()
    {
        var platform = await PlatformAsync();
        var plan = await NewPlanAsync(platform, $"Temp {Guid.NewGuid():N}"[..14]);
        var company = await Workspaces.SignupAsync(factory);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest(plan.Key));

        (await platform.Client.DeleteAsync($"/api/v1/platform/plans/{plan.Id}")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/plan", new ChangeTenantPlanRequest("pro"));
        (await platform.Client.DeleteAsync($"/api/v1/platform/plans/{plan.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await platform.Client.GetFromJsonAsync<List<PlatformPlanDto>>("/api/v1/platform/plans"))!.ShouldNotContain(p => p.Id == plan.Id);

        (await platform.Client.PostAsJsonAsync("/api/v1/platform/plans", new SavePlanRequest("", 0, 0, -1, true))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await company.Client.PostAsJsonAsync("/api/v1/platform/plans", new SavePlanRequest("Sneaky", 5, 5, 5, true))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_audit_trail_can_be_narrowed_by_company_kind_words_and_dates_and_exported()
    {
        var platform = await PlatformAsync();
        var company = await Workspaces.SignupAsync(factory, $"Audited Co {Guid.NewGuid():N}"[..22]);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        await platform.Client.PutAsJsonAsync($"/api/v1/platform/companies/{id}/modules", new SetCompanyModulesRequest(["Invoices"]));
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("Active"));

        var mine = (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}"))!;
        mine.Total.ShouldBe(2);
        mine.Items.ShouldAllBe(a => a.TenantId == id);

        var modulesOnly = (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}&kind=modules"))!;
        modulesOnly.Items.Single().Action.ShouldBe("modules.changed");

        (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}&search=Invoices"))!.Total.ShouldBe(1);
        (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}&from=2099-01-01"))!.Total.ShouldBe(0);
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}&from={today}&to={today}"))!.Total.ShouldBe(2);

        var csv = await platform.Client.GetAsync($"/api/v1/platform/activity/export?company={id}");
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var text = await csv.Content.ReadAsStringAsync();
        text.ShouldContain("What changed");
        text.ShouldContain("modules.changed");
        text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(3);
        (await company.Client.GetAsync("/api/v1/platform/activity/export")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_owner_is_told_about_new_companies_and_about_companies_that_need_a_look()
    {
        var platform = await PlatformAsync();
        var company = await Workspaces.SignupAsync(factory, $"Notified Co {Guid.NewGuid():N}"[..22]);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        await platform.Client.PatchAsJsonAsync($"/api/v1/platform/companies/{id}/status", new ChangeTenantStatusRequest("PastDue"));

        var notifications = (await platform.Client.GetFromJsonAsync<List<PlatformNotificationDto>>("/api/v1/platform/notifications"))!;
        notifications.ShouldContain(n => n.Kind == "signup" && n.TenantId == id);
        notifications.ShouldContain(n => n.Kind == "past-due" && n.TenantId == id && n.Severity == "critical");
        notifications.Select(n => n.Id).Distinct().Count().ShouldBe(notifications.Count);
        (await company.Client.GetAsync("/api/v1/platform/notifications")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_owners_sign_ins_and_exports_are_on_the_trail_and_company_users_are_not()
    {
        var email = $"platform-{Guid.NewGuid():N}@mindlift.test";
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync(email, Workspaces.Password, "Priya", "Owner");
        }

        var wrong = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, "Wrong-pass-1"));
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var platform = await Workspaces.LoginAsync(factory, email, Workspaces.Password);
        await Workspaces.SignupAsync(factory);   // a company user signing up and in must not appear

        (await platform.Client.GetAsync("/api/v1/platform/activity/export")).EnsureSuccessStatusCode();
        var trail = (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>("/api/v1/platform/activity?search=Priya&pageSize=100"))!;
        trail.Items.ShouldContain(a => a.Action == "auth.failed");
        trail.Items.ShouldContain(a => a.Action == "auth.signin");
        trail.Items.ShouldContain(a => a.Action == "audit.exported");
        trail.Items.ShouldAllBe(a => a.Actor == "Priya Owner");
        (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>("/api/v1/platform/activity?kind=auth&pageSize=100"))!.Items.ShouldAllBe(a => a.Action.StartsWith("auth."));
    }
}
