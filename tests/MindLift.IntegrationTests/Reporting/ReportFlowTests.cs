using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Reporting;

public sealed class ReportFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Can_query_available_data_sources_and_fields()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var resp = await admin.Client.GetAsync("/api/v1/reports/sources");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var sources = await resp.Content.ReadFromJsonAsync<List<ReportDataSourceDto>>();
        sources.ShouldNotBeNull();
        sources.ShouldContain(s => s.Key == "leads");
        sources.ShouldContain(s => s.Key == "deals");
        sources.ShouldContain(s => s.Key == "contacts");
        sources.ShouldContain(s => s.Key == "accounts");
        sources.ShouldContain(s => s.Key == "activities");
        sources.ShouldContain(s => s.Key == "deal_stage_history");

        var dealsSource = sources.First(s => s.Key == "deals");
        dealsSource.Fields.ShouldContain(f => f.Key == "amount_in_base" && f.Aggregatable);
        dealsSource.Fields.ShouldContain(f => f.Key == "stage_name" && f.Groupable);
    }

    [Fact]
    public async Task Can_run_ad_hoc_summary_report_with_grouping_and_aggregates()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var query = new ReportQuery
        {
            Source = "deals",
            Type = ReportType.Summary,
            GroupBy = [new ReportGroup("stage_name")],
            Aggregates = [
                new ReportAggregate("amount_in_base", AggregateFunc.Sum, "total_amount"),
                new ReportAggregate("id", AggregateFunc.Count, "deal_count")
            ],
            Limit = 100
        };

        var resp = await admin.Client.PostAsJsonAsync("/api/v1/reports/run", query);
        var err = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, err);

        var result = await resp.Content.ReadFromJsonAsync<ReportResult>();
        result.ShouldNotBeNull();
        result.Columns.ShouldContain(c => c.Key == "stage_name" && c.IsGroup);
        result.Columns.ShouldContain(c => c.Key == "total_amount" && c.IsAggregate);
        result.Columns.ShouldContain(c => c.Key == "deal_count" && c.IsAggregate);
        result.ExecutionMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Rejects_sql_injection_attempts_in_fields()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var maliciousQuery = new ReportQuery
        {
            Source = "deals",
            Type = ReportType.Tabular,
            Columns = ["name; DROP TABLE deals;--"],
            Limit = 10
        };

        var resp = await admin.Client.PostAsJsonAsync("/api/v1/reports/run", maliciousQuery);
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Can_execute_system_reports()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Sales by user
        var salesByUserResp = await admin.Client.PostAsJsonAsync("/api/v1/reports/system/sales_by_user/run", new ReportRunRequest());
        salesByUserResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var salesByUser = await salesByUserResp.Content.ReadFromJsonAsync<ReportResult>();
        salesByUser.ShouldNotBeNull();
        salesByUser.Columns.ShouldContain(c => c.Key == "owner_name");
        salesByUser.Columns.ShouldContain(c => c.Key == "won_amount");

        // 2. Lead sources
        var leadSourcesResp = await admin.Client.PostAsJsonAsync("/api/v1/reports/system/lead_sources/run", new ReportRunRequest());
        leadSourcesResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var leadSources = await leadSourcesResp.Content.ReadFromJsonAsync<ReportResult>();
        leadSources.ShouldNotBeNull();
        leadSources.Columns.ShouldContain(c => c.Key == "source_name");

        // 3. Revenue by month
        var revenueResp = await admin.Client.PostAsJsonAsync("/api/v1/reports/system/revenue_by_month/run", new ReportRunRequest());
        revenueResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revenue = await revenueResp.Content.ReadFromJsonAsync<ReportResult>();
        revenue.ShouldNotBeNull();
        revenue.Columns.ShouldContain(c => c.Key == "closed_month");

        // 4. Overdue tasks
        var overdueResp = await admin.Client.PostAsJsonAsync("/api/v1/reports/system/overdue_tasks/run", new ReportRunRequest());
        overdueResp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Can_create_save_favorite_and_export_custom_report()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create a folder
        var folderResp = await admin.Client.PostAsJsonAsync("/api/v1/report-folders", new SaveReportFolderRequest { Name = "Executive Reports" });
        folderResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var folder = await folderResp.Content.ReadFromJsonAsync<ReportFolderDto>();
        folder.ShouldNotBeNull();

        // 2. Save a custom report
        var saveReq = new SaveReportDefinitionRequest
        {
            Name = "Open Deals Pipeline",
            Description = "All active pipeline deals",
            FolderId = folder.Id,
            Visibility = ReportVisibility.Everyone,
            Query = new ReportQuery
            {
                Source = "deals",
                Type = ReportType.Tabular,
                Columns = ["name", "amount_in_base", "stage_name", "owner_name"],
                Limit = 50
            }
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/reports", saveReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var report = await createResp.Content.ReadFromJsonAsync<ReportDefinitionDto>();
        report.ShouldNotBeNull();
        report.Name.ShouldBe("Open Deals Pipeline");

        // 3. Toggle favorite
        var favResp = await admin.Client.PostAsync($"/api/v1/reports/{report.Id}/favorite", null);
        favResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var isFav = await favResp.Content.ReadFromJsonAsync<bool>();
        isFav.ShouldBeTrue();

        // 4. Run report by ID
        var runResp = await admin.Client.PostAsJsonAsync($"/api/v1/reports/{report.Id}/run", new ReportRunRequest());
        runResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var runResult = await runResp.Content.ReadFromJsonAsync<ReportResult>();
        runResult.ShouldNotBeNull();
        runResult.Columns.ShouldContain(c => c.Key == "name");
        runResult.Columns.ShouldContain(c => c.Key == "amount_in_base");

        // 5. Export to CSV
        var csvResp = await admin.Client.PostAsync($"/api/v1/reports/{report.Id}/export?format=csv", null);
        csvResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var csvBytes = await csvResp.Content.ReadAsByteArrayAsync();
        csvBytes.Length.ShouldBeGreaterThan(0);
        // Verify UTF-8 BOM
        csvBytes[0].ShouldBe((byte)0xEF);
        csvBytes[1].ShouldBe((byte)0xBB);
        csvBytes[2].ShouldBe((byte)0xBF);

        // 6. Export to Excel (XML)
        var xlsxResp = await admin.Client.PostAsync($"/api/v1/reports/{report.Id}/export?format=xlsx", null);
        xlsxResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var xlsxBytes = await xlsxResp.Content.ReadAsByteArrayAsync();
        xlsxBytes.Length.ShouldBeGreaterThan(0);

        // 7. Export to PDF (both GET and POST supported)
        var pdfResp = await admin.Client.GetAsync($"/api/v1/reports/{report.Id}/export?format=pdf");
        pdfResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdfResp.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.ShouldBeGreaterThan(0);
        pdfBytes[..5].ShouldBe("%PDF-"u8.ToArray());

        // 8. Schedule Management for Report
        var schedReq = new SaveReportScheduleRequest
        {
            CronExpression = "0 8 * * 1",
            Format = "pdf",
            Recipients = ["exec@mindlift.com", "ops@mindlift.com"],
            IsActive = true
        };
        var schedCreateResp = await admin.Client.PostAsJsonAsync($"/api/v1/reports/{report.Id}/schedules", schedReq);
        schedCreateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var schedDto = await schedCreateResp.Content.ReadFromJsonAsync<ReportScheduleDto>();
        schedDto.ShouldNotBeNull();
        schedDto.CronExpression.ShouldBe("0 8 * * 1");
        schedDto.Format.ShouldBe("pdf");
        schedDto.Recipients.Count.ShouldBe(2);

        var schedListResp = await admin.Client.GetFromJsonAsync<List<ReportScheduleDto>>($"/api/v1/reports/{report.Id}/schedules");
        schedListResp.ShouldNotBeNull();
        schedListResp.ShouldContain(s => s.Id == schedDto.Id);

        var schedDelResp = await admin.Client.DeleteAsync($"/api/v1/reports/schedules/{schedDto.Id}");
        schedDelResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 9. Delete report
        var delResp = await admin.Client.DeleteAsync($"/api/v1/reports/{report.Id}");
        delResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Can_export_system_report_to_pdf()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var pdfResp = await admin.Client.GetAsync("/api/v1/reports/system/revenue_by_month/export?format=pdf");
        pdfResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdfResp.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        var pdfBytes = await pdfResp.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.ShouldBeGreaterThan(0);
        pdfBytes[..5].ShouldBe("%PDF-"u8.ToArray());
    }

    [Fact]
    public async Task Can_create_update_and_delete_sales_targets()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Create target
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var req = new SaveSalesTargetRequest(
            null,
            null,
            SalesTargetMetric.WonAmount,
            SalesTargetPeriod.Month,
            today,
            today.AddMonths(1),
            75000m
        );

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/sales-targets", req);
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = await createResp.Content.ReadFromJsonAsync<SalesTargetDto>();
        target.ShouldNotBeNull();
        target.TargetValue.ShouldBe(75000m);
        target.Metric.ShouldBe(SalesTargetMetric.WonAmount);

        // 2. List targets
        var listResp = await admin.Client.GetFromJsonAsync<List<SalesTargetDto>>("/api/v1/sales-targets");
        listResp.ShouldNotBeNull();
        listResp.ShouldContain(t => t.Id == target.Id);

        // 3. Update target
        var updateReq = req with { TargetValue = 90000m };
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/sales-targets/{target.Id}", updateReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<SalesTargetDto>();
        updated.ShouldNotBeNull();
        updated.TargetValue.ShouldBe(90000m);

        // 4. Delete target
        var delResp = await admin.Client.DeleteAsync($"/api/v1/sales-targets/{target.Id}");
        delResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var finalList = await admin.Client.GetFromJsonAsync<List<SalesTargetDto>>("/api/v1/sales-targets");
        finalList.ShouldNotBeNull();
        finalList.ShouldNotContain(t => t.Id == target.Id);
    }
}
