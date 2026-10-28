using System.Net;
using System.Net.Http.Json;
using System.Text;
using MindLift.Shared.Imports;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>CSV export and import of leads (plan 4.10): the file must be correct, complete, safe to open and importable again.</summary>
public sealed class LeadExportImportTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static ListQuery All() => new(null, null, "first_name", "asc", 1, 200);

    private static async Task<string> ExportAsync(Session session)
    {
        var response = await session.Client.PostAsJsonAsync("/api/v1/leads/export", All());
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        return (await response.Content.ReadAsStringAsync()).TrimStart('﻿');
    }

    private static async Task<ImportResultDto> ImportAsync(Session session, string csv)
    {
        var response = await session.Client.PostAsync("/api/v1/leads/import", new StringContent(csv, Encoding.UTF8, "text/csv"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
    }

    private static async Task CreateAsync(Session session, string first, string last, string? email, string? phone, string? company)
    {
        var request = new SaveLeadRequest(first, last, email, phone, company, "new", "website", "warm", null, null, null, null);
        (await session.Client.PostAsJsonAsync("/api/v1/leads", request)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_exported_value_sits_under_its_own_heading()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var email = $"ana-{Guid.NewGuid():N}@example.test";
        await CreateAsync(owner, "Ana", "Quest", email, "+971 50 123 4567", "Acme, Inc");

        var rows = CsvText.Parse(await ExportAsync(owner));

        var header = rows[0];
        header.ShouldBe(["id", "first_name", "last_name", "email", "phone", "company", "status", "source", "rating", "owner", "created_at"]);
        rows.Skip(1).ShouldAllBe(r => r.Count == header.Count);

        var row = rows.Skip(1).Single(r => r[header.IndexOf("email")] == email);
        row[header.IndexOf("first_name")].ShouldBe("Ana");
        row[header.IndexOf("last_name")].ShouldBe("Quest");
        row[header.IndexOf("phone")].ShouldBe("+971 50 123 4567");
        row[header.IndexOf("company")].ShouldBe("Acme, Inc");
        row[header.IndexOf("status")].ShouldBe("new");
        row[header.IndexOf("source")].ShouldBe("website");
        row[header.IndexOf("rating")].ShouldBe("warm");
        Guid.TryParse(row[header.IndexOf("id")], out _).ShouldBeTrue();
        DateTime.TryParse(row[header.IndexOf("created_at")], out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_name_that_a_spreadsheet_would_run_as_a_formula_is_exported_as_text()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await CreateAsync(owner, "=HYPERLINK(\"http://evil.test\",\"click\")", "Tester", $"f-{Guid.NewGuid():N}@example.test", "+971501234567", null);

        var rows = CsvText.Parse(await ExportAsync(owner));
        var header = rows[0];
        var row = rows.Skip(1).Single();

        row[header.IndexOf("first_name")].ShouldStartWith("'=");
        row[header.IndexOf("phone")].ShouldBe("+971501234567");
    }

    [Fact]
    public async Task An_export_of_more_than_one_page_contains_every_lead()
    {
        var owner = await Workspaces.SignupAsync(factory);
        owner.Client.Timeout = TimeSpan.FromMinutes(10);
        var csv = new StringBuilder("First name,Last name,Email\n");
        for (var i = 0; i < 450; i++)
        {
            csv.Append($"Person{i:D3},Test,person{i:D3}-{Guid.NewGuid():N}@example.test\n");
        }

        (await ImportAsync(owner, csv.ToString())).Created.ShouldBe(450);

        var rows = CsvText.Parse(await ExportAsync(owner));

        rows.Count.ShouldBe(451);
        rows.Skip(1).Select(r => r[0]).Distinct().Count().ShouldBe(450);
    }

    [Fact]
    public async Task An_export_respects_the_filter_it_was_asked_for()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await CreateAsync(owner, "Keep", "Me", $"k-{Guid.NewGuid():N}@example.test", null, null);
        await CreateAsync(owner, "Drop", "Me", $"d-{Guid.NewGuid():N}@example.test", null, null);

        var response = await owner.Client.PostAsJsonAsync("/api/v1/leads/export", new ListQuery("Keep", null, null, null, 1, 200));
        var rows = CsvText.Parse((await response.Content.ReadAsStringAsync()).TrimStart('﻿'));

        rows.Skip(1).Select(r => r[1]).ShouldBe(["Keep"]);
    }

    [Fact]
    public async Task An_exported_file_can_be_imported_into_another_workspace()
    {
        var source = await Workspaces.SignupAsync(factory);
        await CreateAsync(source, "Ana", "Quest", $"a-{Guid.NewGuid():N}@example.test", "+971 50 111 2222", "Acme, Inc");
        await CreateAsync(source, "Ben", "Zed", $"b-{Guid.NewGuid():N}@example.test", null, null);
        var csv = await ExportAsync(source);

        var target = await Workspaces.SignupAsync(factory);
        var result = await ImportAsync(target, csv);

        result.Total.ShouldBe(2);
        result.Created.ShouldBe(2);
        result.Failed.ShouldBe(0);
        var imported = CsvText.Parse(await ExportAsync(target));
        imported.Skip(1).Select(r => r[1]).ShouldBe(["Ana", "Ben"]);
        imported.Skip(1).Select(r => r[5]).ShouldContain("Acme, Inc");
    }

    [Fact]
    public async Task Importing_the_same_file_twice_creates_nothing_the_second_time()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var csv = $"First name,Last name,Email\nOne,A,one-{Guid.NewGuid():N}@example.test\nTwo,B,two-{Guid.NewGuid():N}@example.test\n";

        (await ImportAsync(owner, csv)).Created.ShouldBe(2);
        var again = await ImportAsync(owner, csv);

        again.Created.ShouldBe(0);
        again.Skipped.ShouldBe(2);
    }

    [Fact]
    public async Task Import_reports_rows_without_a_first_name_and_a_file_without_the_column()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var partial = await ImportAsync(owner, "First name,Email\n,nobody@example.test\nOk,ok-" + Guid.NewGuid().ToString("N") + "@example.test\n");
        partial.Failed.ShouldBe(1);
        partial.Created.ShouldBe(1);
        partial.Errors.ShouldNotBeEmpty();

        var noColumn = await owner.Client.PostAsync("/api/v1/leads/import", new StringContent("Email\nx@example.test\n", Encoding.UTF8, "text/csv"));
        noColumn.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_file_with_too_many_rows_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var csv = new StringBuilder("First name,Email\n");
        for (var i = 0; i < 5001; i++)
        {
            csv.Append($"P{i},p{i}@example.test\n");
        }

        var response = await owner.Client.PostAsync("/api/v1/leads/import", new StringContent(csv.ToString(), Encoding.UTF8, "text/csv"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Another_workspace_never_sees_these_leads_in_its_export()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await CreateAsync(a, "OnlyInA", "Test", $"a-{Guid.NewGuid():N}@example.test", null, null);

        var rows = CsvText.Parse(await ExportAsync(b));

        rows.Count.ShouldBe(1);
    }
}
