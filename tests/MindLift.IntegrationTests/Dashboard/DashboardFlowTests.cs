using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;

namespace MindLift.IntegrationTests.Dashboard;

public sealed class DashboardFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Staff_sees_only_own_view_and_cannot_escalate_to_admin()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff");

        // Staff requests dashboard without view parameter -> gets Staff view
        var resp = await staff.Client.GetAsync("/api/v1/dashboard");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await resp.Content.ReadFromJsonAsync<DashboardResponse>();
        data.ShouldNotBeNull();
        data.View.ShouldBe(DashboardView.Staff);
        data.AvailableViews.ShouldBe([DashboardView.Staff]);

        // Staff tries to request Admin view -> downgraded to Staff view
        var adminViewResp = await staff.Client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard?view=Admin");
        adminViewResp.ShouldNotBeNull();
        adminViewResp.View.ShouldBe(DashboardView.Staff);

        // Staff dashboard contains Staff widgets (kpi_strip, my_day) and NOT admin-only widgets
        data.Widgets.Any(w => w.Key == "kpi_strip").ShouldBeTrue();
        data.Widgets.Any(w => w.Key == "my_day").ShouldBeTrue();
        data.Widgets.Any(w => w.Key == "system_overview").ShouldBeFalse();
    }

    [Fact]
    public async Task Admin_can_switch_views_and_filter_by_period()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Admin default view is Admin
        var adminDash = await admin.Client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard");
        adminDash.ShouldNotBeNull();
        adminDash.View.ShouldBe(DashboardView.Admin);
        adminDash.AvailableViews.ShouldContain(DashboardView.Staff);
        adminDash.AvailableViews.ShouldContain(DashboardView.Manager);
        adminDash.AvailableViews.ShouldContain(DashboardView.Admin);

        // Contains Admin widgets
        adminDash.Widgets.Any(w => w.Key == "sales_trend").ShouldBeTrue();
        adminDash.Widgets.Any(w => w.Key == "system_overview").ShouldBeTrue();

        // Admin can switch to Staff view
        var staffDash = await admin.Client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard?view=Staff");
        staffDash.ShouldNotBeNull();
        staffDash.View.ShouldBe(DashboardView.Staff);
        staffDash.Widgets.Any(w => w.Key == "my_day").ShouldBeTrue();

        // Admin can filter by period preset
        var quarterDash = await admin.Client.GetFromJsonAsync<DashboardResponse>("/api/v1/dashboard?period=this_quarter");
        quarterDash.ShouldNotBeNull();
        quarterDash.Widgets.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Dashboard_layout_can_be_customized_saved_and_reset()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Get default layout
        var defaultLayout = await admin.Client.GetFromJsonAsync<DashboardLayoutDto>("/api/v1/dashboard/layout?view=Staff");
        defaultLayout.ShouldNotBeNull();
        defaultLayout.Widgets.Count.ShouldBeGreaterThan(0);

        // Save customized layout with hidden widget
        var customItems = defaultLayout.Widgets.Select(w =>
            w.Key == "my_day" ? new DashboardWidgetLayoutItem(w.Key, w.Order, w.Size, true) : w
        ).ToList();

        var saveReq = new SaveDashboardLayoutRequest(DashboardView.Staff, customItems);
        var saveResp = await admin.Client.PutAsJsonAsync("/api/v1/dashboard/layout", saveReq);
        saveResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var savedLayout = await saveResp.Content.ReadFromJsonAsync<DashboardLayoutDto>();
        savedLayout!.Widgets.First(w => w.Key == "my_day").IsHidden.ShouldBeTrue();

        // Reset layout
        var resetResp = await admin.Client.DeleteAsync("/api/v1/dashboard/layout?view=Staff");
        resetResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resetLayout = await resetResp.Content.ReadFromJsonAsync<DashboardLayoutDto>();
        resetLayout!.Widgets.First(w => w.Key == "my_day").IsHidden.ShouldBeFalse();
    }

    [Fact]
    public async Task Sales_targets_crud_and_progress_flow()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startOfMonth = new DateOnly(today.Year, today.Month, 1);
        var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

        // 1. Create sales target
        var me = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var targetReq = new SaveSalesTargetRequest(
            UserId: me!.UserId,
            TeamId: null,
            Metric: SalesTargetMetric.WonAmount,
            PeriodType: SalesTargetPeriod.Month,
            StartDate: startOfMonth,
            EndDate: endOfMonth,
            TargetValue: 75000m
        );

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/sales-targets", targetReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<SalesTargetDto>();
        created.ShouldNotBeNull();
        created.TargetValue.ShouldBe(75000m);
        created.Metric.ShouldBe(SalesTargetMetric.WonAmount);

        // 2. List targets
        var list = await admin.Client.GetFromJsonAsync<List<SalesTargetDto>>("/api/v1/sales-targets");
        list.ShouldNotBeNull();
        list.Any(t => t.Id == created.Id).ShouldBeTrue();

        // 3. Target progress widget computes with target
        var widgetResp = await admin.Client.GetFromJsonAsync<DashboardWidgetDto>("/api/v1/dashboard/widgets/target_progress?view=Admin");
        widgetResp.ShouldNotBeNull();
        widgetResp.Key.ShouldBe("target_progress");
        widgetResp.Error.ShouldBeNull();

        // 4. Delete target
        var delResp = await admin.Client.DeleteAsync($"/api/v1/sales-targets/{created.Id}");
        delResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
