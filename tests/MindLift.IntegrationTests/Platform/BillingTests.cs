using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Billing;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>
/// Billing without a gateway: a company chooses a plan and gets an invoice, the platform owner records the payment, and the company's
/// access follows; renewals, past-due and cancellation run from the daily job.
/// </summary>
public sealed class BillingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
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

    private static async Task<Guid> TenantIdAsync(Session s) => (await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;

    private async Task UpdateAsync(Guid tenantId, Action<MindLift.Domain.Entities.Subscription, MindLift.Domain.Entities.Tenant> change)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var s = await db.Subscriptions.SingleAsync(x => x.TenantId == tenantId);
        var t = await db.Tenants.SingleAsync(x => x.Id == tenantId);
        change(s, t);
        await db.SaveChangesAsync();
    }

    private async Task RunRenewalsAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingService>().RunRenewalsAsync();
    }

    [Fact]
    public async Task A_company_on_trial_chooses_a_plan_gets_an_invoice_and_becomes_active_when_the_payment_is_recorded()
    {
        var company = await Workspaces.SignupAsync(factory);
        var platform = await PlatformAsync();
        var id = await TenantIdAsync(company);

        var overview = (await company.Client.GetFromJsonAsync<BillingOverviewDto>("/api/v1/billing"))!;
        overview.Status.ShouldBe("trialing");
        overview.TrialEndsAt.ShouldNotBeNull();
        overview.OnlinePayment.ShouldBeFalse();
        overview.Plans.ShouldContain(p => p.Key == "starter");

        var checkout = await company.Client.PostAsJsonAsync("/api/v1/billing/checkout", new StartCheckoutRequest("starter", "yearly"));
        checkout.StatusCode.ShouldBe(HttpStatusCode.OK, await checkout.Content.ReadAsStringAsync());
        var result = (await checkout.Content.ReadFromJsonAsync<CheckoutResultDto>())!;
        result.Mode.ShouldBe("invoice");
        result.Invoice!.Status.ShouldBe("open");
        result.Invoice.Interval.ShouldBe("yearly");
        result.Invoice.Number.ShouldStartWith("ML-");

        overview = (await company.Client.GetFromJsonAsync<BillingOverviewDto>("/api/v1/billing"))!;
        overview.PendingPlanName.ShouldBe("Starter");
        overview.Status.ShouldBe("trialing");   // nothing changes until the money arrives

        // The owner is told there is a company waiting to pay.
        (await platform.Client.GetFromJsonAsync<List<PlatformNotificationDto>>("/api/v1/platform/notifications"))!
            .ShouldContain(n => n.Kind == "billing-request" && n.TenantId == id);

        var paid = await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/billing/payments",
            new RecordBillingPaymentRequest(result.Invoice.Id, null, null, null, "Bank transfer ref 4471", null));
        paid.StatusCode.ShouldBe(HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        var view = (await paid.Content.ReadFromJsonAsync<PlatformBillingDto>())!;
        view.Status.ShouldBe("active");
        view.PlanName.ShouldBe("Starter");
        view.CurrentPeriodEnd!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddDays(360));
        view.Invoices.Single(i => i.Id == result.Invoice.Id).Status.ShouldBe("paid");
        view.LifetimePaid.ShouldBe(result.Invoice.Amount);

        overview = (await company.Client.GetFromJsonAsync<BillingOverviewDto>("/api/v1/billing"))!;
        overview.Status.ShouldBe("active");
        overview.PlanKey.ShouldBe("starter");
        overview.PendingPlanName.ShouldBeNull();
        (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantStatus.ShouldBe("Active");

        var trail = (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={id}"))!;
        trail.Items.ShouldContain(a => a.Action == "billing.payment" && a.Summary.Contains("ref 4471") == false && a.Summary.Contains(result.Invoice.Number));
    }

    [Fact]
    public async Task A_company_whose_subscription_lapsed_can_still_reach_billing_but_cannot_make_other_changes()
    {
        var company = await Workspaces.SignupAsync(factory);
        var id = await TenantIdAsync(company);
        await company.Client.GetAsync("/api/v1/billing");   // creates the subscription
        await UpdateAsync(id, (s, t) => { t.Status = "PastDue"; s.Status = "past_due"; });

        (await company.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Blocked", "Lead", null, null, null, null, null, null, null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await company.Client.PostAsJsonAsync("/api/v1/billing/checkout", new StartCheckoutRequest("pro", "monthly"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_daily_job_issues_renewal_invoices_a_week_ahead_and_marks_unpaid_companies_past_due()
    {
        var company = await Workspaces.SignupAsync(factory);
        var platform = await PlatformAsync();
        var id = await TenantIdAsync(company);
        await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/billing/payments", new RecordBillingPaymentRequest(null, "pro", "monthly", null, null, null));

        // Five days before the end: one renewal invoice, not two.
        await UpdateAsync(id, (s, _) => s.CurrentPeriodEnd = DateTime.UtcNow.AddDays(5));
        await RunRenewalsAsync();
        await RunRenewalsAsync();
        var view = (await platform.Client.GetFromJsonAsync<PlatformBillingDto>($"/api/v1/platform/companies/{id}/billing"))!;
        view.Invoices.Count(i => i.Status == "open").ShouldBe(1);

        // Past the end and the grace period without paying: past due, so changes stop until it is paid.
        await UpdateAsync(id, (s, _) => s.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-10));
        await RunRenewalsAsync();
        (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantStatus.ShouldBe("PastDue");

        // Paying the open renewal invoice brings it back.
        var open = view.Invoices.Single(i => i.Status == "open");
        (await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/billing/payments", new RecordBillingPaymentRequest(open.Id, null, null, null, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantStatus.ShouldBe("Active");
    }

    [Fact]
    public async Task A_cancelled_subscription_keeps_working_until_the_period_ends_then_stops()
    {
        var company = await Workspaces.SignupAsync(factory);
        var platform = await PlatformAsync();
        var id = await TenantIdAsync(company);
        await platform.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/billing/payments", new RecordBillingPaymentRequest(null, "starter", "monthly", null, null, null));

        var cancelled = (await (await company.Client.PostAsync("/api/v1/billing/cancel", null)).Content.ReadFromJsonAsync<BillingOverviewDto>())!;
        cancelled.CancelAtPeriodEnd.ShouldBeTrue();
        cancelled.Status.ShouldBe("active");
        await RunRenewalsAsync();
        (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantStatus.ShouldBe("Active");

        await UpdateAsync(id, (s, _) => s.CurrentPeriodEnd = DateTime.UtcNow.AddMinutes(-1));
        await RunRenewalsAsync();
        (await company.Client.GetFromJsonAsync<BillingOverviewDto>("/api/v1/billing"))!.Status.ShouldBe("cancelled");
    }

    [Fact]
    public async Task Bad_requests_are_refused_and_only_the_platform_owner_records_payments()
    {
        var company = await Workspaces.SignupAsync(factory);
        var id = await TenantIdAsync(company);
        (await company.Client.PostAsJsonAsync("/api/v1/billing/checkout", new StartCheckoutRequest("nope", "monthly"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await company.Client.PostAsJsonAsync("/api/v1/billing/checkout", new StartCheckoutRequest("pro", "weekly"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await company.Client.PostAsJsonAsync($"/api/v1/platform/companies/{id}/billing/payments", new RecordBillingPaymentRequest(null, "pro", "monthly", 1, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await company.Client.PostAsync("/api/v1/billing/webhooks/manual", new StringContent("{}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_plan_can_have_a_yearly_price_and_its_own_currency()
    {
        var platform = await PlatformAsync();
        var plan = (await (await platform.Client.PostAsJsonAsync("/api/v1/platform/plans",
            new SavePlanRequest($"Gulf {Guid.NewGuid():N}"[..12], 5, 1000, 99, true, null, null, 990, "aed"))).Content.ReadFromJsonAsync<PlatformPlanDto>())!;
        plan.PriceYearly.ShouldBe(990);
        plan.Currency.ShouldBe("AED");
        (await platform.Client.PostAsJsonAsync("/api/v1/platform/plans", new SavePlanRequest("Bad", 5, 1000, 9, true, null, null, null, "dollars")))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
