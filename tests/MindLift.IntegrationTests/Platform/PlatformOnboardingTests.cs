using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>The platform owner onboards a company, and tells active companies from inactive ones.</summary>
public sealed class PlatformOnboardingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
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

    private static CreateCompanyRequest Request(string name, string email, string status = "Trial", string? preset = null)
        => new(name, "Nadia", "Haddad", email, "Welcome#2026", "AE", "starter", status, 14, preset);

    [Fact]
    public async Task A_company_onboarded_by_the_owner_can_sign_in_at_once_on_the_chosen_plan_and_modules()
    {
        var platform = await PlatformAsync();
        var email = $"owner-{Guid.NewGuid():N}@newco.test";

        var response = await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request($"Newco {Guid.NewGuid():N}"[..16], email, "Trial", "sales"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = (await response.Content.ReadFromJsonAsync<PlatformCompanyDetailDto>())!;

        created.Company.PlanKey.ShouldBe("starter");
        created.Company.Status.ShouldBe("Trial");
        created.Company.TrialEndsAt.ShouldNotBeNull();
        created.OwnerEmail.ShouldBe(email);
        created.DisabledModules.ShouldContain("Invoices");
        created.DisabledModules.ShouldNotContain("Deals");
        created.History.ShouldContain(h => h.Action == "company.created");

        // The owner signs in with the password they were given, with no email to confirm first.
        var owner = await Workspaces.LoginAsync(factory, email, "Welcome#2026");
        (await owner.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var modules = (await owner.Client.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules"))!;
        modules.Single(m => m.Module == "Invoices").PlatformLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task A_company_can_start_active_and_the_owners_email_must_be_new()
    {
        var platform = await PlatformAsync();
        var email = $"owner-{Guid.NewGuid():N}@newco.test";
        var first = await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request($"Active Co {Guid.NewGuid():N}"[..16], email, "Active"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await first.Content.ReadFromJsonAsync<PlatformCompanyDetailDto>())!.Company.Status.ShouldBe("Active");

        (await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request("Second Co", email))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request("Bad Status Co", $"x-{Guid.NewGuid():N}@newco.test", "Suspended"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request("Bad Plan Co", $"y-{Guid.NewGuid():N}@newco.test") with { PlanKey = "nope" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request("Short Password Co", $"z-{Guid.NewGuid():N}@newco.test") with { Password = "abc" })).IsSuccessStatusCode.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_platform_owner_can_onboard_a_company()
    {
        var company = await Workspaces.SignupAsync(factory);
        (await company.Client.PostAsJsonAsync("/api/v1/platform/companies", Request("Sneaky Co", $"s-{Guid.NewGuid():N}@newco.test"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_list_separates_companies_that_signed_in_lately_from_those_that_did_not()
    {
        var platform = await PlatformAsync();
        var company = await Workspaces.SignupAsync(factory, $"Lively Co {Guid.NewGuid():N}"[..22]);
        var id = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;
        await platform.Client.PostAsJsonAsync("/api/v1/platform/companies", Request($"Quiet Co {Guid.NewGuid():N}"[..20], $"q-{Guid.NewGuid():N}@newco.test"));

        var active = (await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?activity=active&pageSize=200"))!;
        var inactive = (await platform.Client.GetFromJsonAsync<PlatformCompanyListDto>("/api/v1/platform/companies?activity=inactive&pageSize=200"))!;

        active.Items.ShouldContain(c => c.Id == id);
        inactive.Items.ShouldNotContain(c => c.Id == id);
        inactive.Items.ShouldNotContain(c => c.Name.StartsWith("Quiet Co")); // a company onboarded today counts as active until 30 days pass
        active.ActiveCount.ShouldBe(active.Total);
        (active.ActiveCount + active.InactiveCount).ShouldBe(active.Statuses.Sum(s => s.Value));
    }
}
