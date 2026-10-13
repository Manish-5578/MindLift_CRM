using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Trial;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;

namespace MindLift.IntegrationTests.Onboarding;

/// <summary>Plan 6.3–6.9 and the M4 definition of done: sign-up → company → industry → sample data, plus the rules around switching and trials.</summary>
public sealed class OnboardingFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static OnboardingCompanyRequest Company(string name) => new(name, "AE", "AED", "Asia/Dubai", "en", true, "VAT", 5, "100123456700003");

    [Fact]
    public async Task Full_flow_from_signup_to_sample_data_renames_modules_and_builds_the_pipeline()
    {
        var owner = await Workspaces.SignupAsync(factory, "Al Noor Properties");

        var start = await owner.Client.GetFromJsonAsync<OnboardingDto>("/api/v1/onboarding");
        start!.CurrentStep.ShouldBe(1);

        var afterCompany = await Post(owner.Client.PutAsJsonAsync("/api/v1/onboarding/company", Company("Al Noor Properties")));
        afterCompany.CompletedSteps.ShouldContain(1);
        afterCompany.CurrentStep.ShouldBe(2);

        var industries = await owner.Client.GetFromJsonAsync<List<IndustrySummaryDto>>("/api/v1/onboarding/industries");
        industries!.Select(i => i.Key).ShouldContain("real_estate");
        industries.Single(i => i.Key == "real_estate").DealLabel.ShouldBe("Property Deal");

        var applied = await Post(owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("real_estate")));
        applied.Tenant.IndustryKey.ShouldBe("real_estate");
        applied.CurrentStep.ShouldBe(3);

        var modules = await owner.Client.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules");
        modules!.Single(m => m.Module == "Deals").SingularLabel.ShouldBe("Property Deal");

        var fields = await owner.Client.GetFromJsonAsync<List<FieldDto>>("/api/v1/settings/fields/leads");
        fields!.Select(f => f.Key).ShouldContain("cf_property_type");

        var pipelines = await owner.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines");
        var sales = pipelines!.Single(p => p.IsDefault);
        sales.Stages.Select(s => s.Name).ShouldBe(["Enquiry", "Viewing", "Offer", "Agreement", "Closed Won", "Closed Lost"]);

        var sample = await Post(owner.Client.PostAsJsonAsync("/api/v1/onboarding/data", new OnboardingDataRequest("sample")));
        sample.Tenant.SampleDataLoaded.ShouldBeTrue();

        await using var scope = factory.Services.CreateAsyncScope();
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId, me.UserId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Leads.CountAsync()).ShouldBe(30);
        (await db.Deals.CountAsync()).ShouldBe(25);
        (await db.Deals.CountAsync(d => d.Status == "Won")).ShouldBe(8);
        (await db.Accounts.CountAsync()).ShouldBe(8);
        (await db.Contacts.CountAsync()).ShouldBe(20);
        (await db.Activities.CountAsync()).ShouldBe(60);

        var done = await Post(owner.Client.PostAsJsonAsync("/api/v1/onboarding/complete", new { }));
        done.CurrentStep.ShouldBe(OnboardingSteps.Completed);
    }

    [Fact]
    public async Task Switching_industry_after_sample_data_replaces_the_template_rows_and_removes_the_samples()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PutAsJsonAsync("/api/v1/onboarding/company", Company("Switch Co"));
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("real_estate"));
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/data", new OnboardingDataRequest("sample"));

        var switched = await Post(owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("healthcare")));

        switched.Tenant.IndustryKey.ShouldBe("healthcare");
        switched.Tenant.SampleDataLoaded.ShouldBeFalse();

        var modules = await owner.Client.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules");
        modules!.Single(m => m.Module == "Deals").SingularLabel.ShouldBe("Patient case");

        var fields = await owner.Client.GetFromJsonAsync<List<FieldDto>>("/api/v1/settings/fields/leads");
        fields!.Select(f => f.Key).ShouldNotContain("cf_property_type");

        var custom = await owner.Client.GetFromJsonAsync<List<CustomFieldDto>>("/api/v1/settings/custom-fields/deals");
        custom!.Select(f => f.Key).ShouldContain("cf_insurance_provider");
        custom.Select(f => f.Key).ShouldNotContain("cf_budget");

        var sources = await owner.Client.GetFromJsonAsync<List<PicklistDto>>("/api/v1/settings/picklists");
        sources!.Single(p => p.Key == "lead_source").Values.Select(v => v.Value).ShouldContain("insurance_referral");
    }

    [Fact]
    public async Task Once_real_records_exist_the_industry_is_locked()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("real_estate"));

        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId, me.UserId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Leads.Add(new Lead { FirstName = "Real", OwnerId = me.UserId });
            await db.SaveChangesAsync();
        }

        var locked = await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("healthcare"));

        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(locked)).ShouldBe("template_locked");
    }

    [Fact]
    public async Task Templates_only_affect_their_own_workspace()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await a.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("real_estate"));

        var modulesB = await b.Client.GetFromJsonAsync<List<ModuleSettingDto>>("/api/v1/settings/modules");

        modulesB!.Single(m => m.Module == "Deals").SingularLabel.ShouldBe("Deal");
    }

    [Fact]
    public async Task Steps_can_be_skipped_only_from_three_to_six_and_sample_data_is_loaded_once()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));

        var refused = await owner.Client.PostAsync("/api/v1/onboarding/skip/1", null);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("not_skippable");

        var skipped = await Post(owner.Client.PostAsync("/api/v1/onboarding/skip/3", null));
        skipped.SkippedSteps.ShouldContain(3);
        skipped.CurrentStep.ShouldBe(4);

        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/data", new OnboardingDataRequest("sample"));
        var second = await owner.Client.PostAsJsonAsync("/api/v1/onboarding/data", new OnboardingDataRequest("sample"));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(second)).ShouldBe("sample_loaded");
    }

    [Fact]
    public async Task Removing_sample_data_leaves_real_records_alone()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/data", new OnboardingDataRequest("sample"));
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId, me.UserId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Leads.Add(new Lead { FirstName = "Keep me", OwnerId = me.UserId });
            await db.SaveChangesAsync();
        }

        var removed = await owner.Client.DeleteAsync("/api/v1/onboarding/sample-data");

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var check = factory.Services.CreateAsyncScope();
        check.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId, me.UserId);
        var db2 = check.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db2.Leads.CountAsync()).ShouldBe(1);
        (await db2.Leads.SingleAsync()).FirstName.ShouldBe("Keep me");
        (await db2.Deals.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task An_expired_trial_turns_into_past_due_and_blocks_writes_but_not_reads()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await SetTrialAsync(factory, me!.TenantId, DateTime.UtcNow.AddDays(-1), reminderSent: true);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<TrialExpiryJob>().RunAsync();
            result.Expired.ShouldBeGreaterThanOrEqualTo(1);
        }

        var write = await owner.Client.PostAsJsonAsync("/api/v1/settings/picklists/lead_source/values",
            new SavePicklistValueRequest("expired_test", "Expired test", null, null, null, 1, false, true));
        write.StatusCode.ShouldBe((HttpStatusCode)402);
        (await Workspaces.ErrorCodeAsync(write)).ShouldBe("subscription_required");

        (await owner.Client.GetAsync("/api/v1/settings/company")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_trial_reminder_is_sent_three_days_before_the_end_once()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await SetTrialAsync(factory, me!.TenantId, DateTime.UtcNow.AddDays(2), reminderSent: false);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<TrialExpiryJob>().RunAsync()).RemindersSent.ShouldBeGreaterThanOrEqualTo(1);
        }

        factory.Emails.Sent.Any(m => m.To == owner.Email && m.Subject.Contains("trial ends", StringComparison.Ordinal)).ShouldBeTrue();

        await using var scope2 = factory.Services.CreateAsyncScope();
        (await scope2.ServiceProvider.GetRequiredService<TrialExpiryJob>().RunAsync()).RemindersSent.ShouldBe(0);
    }

    private static async Task SetTrialAsync(MindLiftApiFactory factory, Guid tenantId, DateTime endsAt, bool reminderSent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workspace = await db.Tenants.SingleAsync(t => t.Id == tenantId);
        workspace.TrialEndsAt = endsAt;
        workspace.TrialReminderSent = reminderSent;
        await db.SaveChangesAsync();
    }

    private static async Task<OnboardingDto> Post(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OnboardingDto>())!;
    }
}
