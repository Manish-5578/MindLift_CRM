using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Dashboard;

public sealed class CustomDashboardWidgetTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveAccountRequest NewAccount(string name)
        => new(name, null, null, null, null, null, null, null);

    private static SaveDealRequest NewDeal(string name, decimal amount, Guid? accountId = null)
        => new(name, null, null, amount, null, 50, DateTime.UtcNow.AddMonths(1), accountId, null, "Custom widget test", null, null);

    [Fact]
    public async Task Feature_flag_gates_custom_dashboard_widget_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync("/api/v1/custom-dashboard-widgets");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/dashboard-widgets", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var getOn = await admin.Client.GetAsync("/api/v1/custom-dashboard-widgets");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Custom_widget_crud_lifecycle()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/dashboard-widgets", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Widget
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets", new SaveCustomDashboardWidgetRequest(
            Title: "Active Deals Value",
            WidgetType: "kpi_metric",
            Source: "deals",
            MetricAggregation: "sum",
            TargetField: "amount",
            Size: "lg",
            IsShared: true));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var widget = await createResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        widget.ShouldNotBeNull();
        widget.Title.ShouldBe("Active Deals Value");
        widget.WidgetType.ShouldBe("kpi_metric");

        // 2. Get by Id
        var getResp = await admin.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{widget.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readWidget = await getResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        readWidget.ShouldNotBeNull();
        readWidget.Id.ShouldBe(widget.Id);

        // 3. Update Widget
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/custom-dashboard-widgets/{widget.Id}", new SaveCustomDashboardWidgetRequest(
            Title: "Pipeline Total",
            WidgetType: "kpi_metric",
            Source: "deals",
            MetricAggregation: "sum",
            TargetField: "amount",
            Size: "xl",
            IsShared: true));
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        updated.ShouldNotBeNull();
        updated.Title.ShouldBe("Pipeline Total");
        updated.Size.ShouldBe("xl");

        // 4. List widgets
        var listResp = await admin.Client.GetFromJsonAsync<List<CustomDashboardWidgetDto>>("/api/v1/custom-dashboard-widgets");
        listResp.ShouldNotBeNull();
        listResp.Any(w => w.Id == widget.Id).ShouldBeTrue();

        // 5. Delete Widget
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/custom-dashboard-widgets/{widget.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var verifyDeleted = await admin.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{widget.Id}");
        verifyDeleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dynamic_data_evaluation_for_kpi_breakdown_and_records()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/dashboard-widgets", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Create 2 deals: $100,000 and $50,000 (Total = $150,000)
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Deal A", 100000m))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Deal B", 50000m))).EnsureSuccessStatusCode();

        // 1. KPI Metric Evaluation
        var kpiResp = await admin.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets", new SaveCustomDashboardWidgetRequest(
            Title: "Total Pipeline Value",
            WidgetType: "kpi_metric",
            Source: "deals",
            MetricAggregation: "sum",
            TargetField: "amount"));
        var kpiWidget = await kpiResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        kpiWidget.ShouldNotBeNull();

        var kpiDataResp = await admin.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{kpiWidget.Id}/data");
        kpiDataResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var kpiData = await kpiDataResp.Content.ReadFromJsonAsync<CustomWidgetDataDto>();

        kpiData.ShouldNotBeNull();
        kpiData.MetricValue.ShouldBe(150000m);
        kpiData.MetricFormatted.ShouldNotBeNull();
        kpiData.MetricFormatted.ShouldContain("150,000");

        // 2. Chart Breakdown Evaluation
        var chartResp = await admin.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets", new SaveCustomDashboardWidgetRequest(
            Title: "Deals by Status",
            WidgetType: "chart_breakdown",
            Source: "deals",
            GroupByField: "status"));
        var chartWidget = await chartResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        chartWidget.ShouldNotBeNull();

        var chartDataResp = await admin.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{chartWidget.Id}/data");
        chartDataResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var chartData = await chartDataResp.Content.ReadFromJsonAsync<CustomWidgetDataDto>();

        chartData.ShouldNotBeNull();
        chartData.ChartBuckets.ShouldNotBeNull();
        chartData.ChartBuckets.Count.ShouldBeGreaterThanOrEqualTo(1);

        // 3. Record List Evaluation
        var listWidgetResp = await admin.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets", new SaveCustomDashboardWidgetRequest(
            Title: "Recent Deals",
            WidgetType: "record_list",
            Source: "deals"));
        var listWidget = await listWidgetResp.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        listWidget.ShouldNotBeNull();

        var listDataResp = await admin.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{listWidget.Id}/data");
        listDataResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listData = await listDataResp.Content.ReadFromJsonAsync<CustomWidgetDataDto>();

        listData.ShouldNotBeNull();
        listData.RecordRows.ShouldNotBeNull();
        listData.RecordRows.Count.ShouldBe(2);

        // 4. Preview unsaved widget configuration
        var previewResp = await admin.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets/preview", new SaveCustomDashboardWidgetRequest(
            Title: "Preview Deals Count",
            WidgetType: "kpi_metric",
            Source: "deals",
            MetricAggregation: "count"));
        previewResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var previewData = await previewResp.Content.ReadFromJsonAsync<CustomWidgetDataDto>();

        previewData.ShouldNotBeNull();
        previewData.MetricValue.ShouldBe(2m);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_widget_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/dashboard-widgets", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/dashboard-widgets", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var widgetRespA = await tenantA.Client.PostAsJsonAsync("/api/v1/custom-dashboard-widgets", new SaveCustomDashboardWidgetRequest(
            Title: "Tenant A Secret Widget",
            WidgetType: "kpi_metric",
            Source: "deals",
            MetricAggregation: "count"));
        var widgetA = await widgetRespA.Content.ReadFromJsonAsync<CustomDashboardWidgetDto>();
        widgetA.ShouldNotBeNull();

        // Tenant B attempts to read Tenant A's widget -> 404
        var crossGet = await tenantB.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{widgetA.Id}");
        crossGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to evaluate data of Tenant A's widget -> 404
        var crossData = await tenantB.Client.GetAsync($"/api/v1/custom-dashboard-widgets/{widgetA.Id}/data");
        crossData.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to update Tenant A's widget -> 404
        var crossUpdate = await tenantB.Client.PutAsJsonAsync($"/api/v1/custom-dashboard-widgets/{widgetA.Id}", new SaveCustomDashboardWidgetRequest(
            Title: "Hacked",
            WidgetType: "kpi_metric",
            Source: "deals"));
        crossUpdate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to delete Tenant A's widget -> 404
        var crossDelete = await tenantB.Client.DeleteAsync($"/api/v1/custom-dashboard-widgets/{widgetA.Id}");
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
