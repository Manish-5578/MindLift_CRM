using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Platform;

/// <summary>A company takes all its data with it in one download, and the platform owner can remove a company for good.</summary>
public sealed class CompanyDataTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
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

    private static Task<HttpResponseMessage> LeadAsync(Session s, string first)
        => s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(first, "Export", $"{first.ToLowerInvariant()}@example.test", null, null, null, null, null, null, null, null, null));

    [Fact]
    public async Task A_company_downloads_all_its_data_and_nothing_of_anyone_else()
    {
        var mine = await Workspaces.SignupAsync(factory);
        var theirs = await Workspaces.SignupAsync(factory);
        (await LeadAsync(mine, "Mine")).EnsureSuccessStatusCode();
        (await LeadAsync(theirs, "Theirs")).EnsureSuccessStatusCode();

        var requested = await mine.Client.PostAsync("/api/v1/settings/data/exports", null);
        requested.StatusCode.ShouldBe(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync());
        var exports = (await mine.Client.GetFromJsonAsync<List<DataExportDto>>("/api/v1/settings/data/exports"))!;
        var ready = exports.Single();
        ready.Status.ShouldBe("ready");   // the test host runs background jobs at once
        ready.SizeBytes!.Value.ShouldBeGreaterThan(0);

        var download = await mine.Client.GetAsync($"/api/v1/settings/data/exports/{ready.Id}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");

        await using var stream = await download.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("README.txt").ShouldNotBeNull();
        var leads = JsonDocument.Parse(await new StreamReader(zip.GetEntry("data/leads.json")!.Open()).ReadToEndAsync()).RootElement;
        leads.EnumerateArray().ShouldContain(l => l.GetProperty("first_name").GetString() == "Mine");
        leads.EnumerateArray().ShouldNotContain(l => l.GetProperty("first_name").GetString() == "Theirs");

        // No secrets: password hashes and security stamps are left out of the users table.
        var users = await new StreamReader(zip.GetEntry("data/users.json")!.Open()).ReadToEndAsync();
        users.ShouldNotContain("password_hash");
        users.ShouldNotContain("security_stamp");
        users.ShouldContain(mine.Email);

        // A signed link downloads it without signing in; a changed link does not.
        var link = (await (await mine.Client.PostAsync($"/api/v1/settings/data/exports/{ready.Id}/link", null)).Content.ReadFromJsonAsync<DownloadLinkDto>())!.Url;
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(link)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync(link + "x")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Another company cannot download it.
        (await theirs.Client.GetAsync($"/api/v1/settings/data/exports/{ready.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_platform_owner_removes_a_company_for_good_and_other_companies_are_untouched()
    {
        var platform = await PlatformAsync();
        var gone = await Workspaces.SignupAsync(factory);
        var stays = await Workspaces.SignupAsync(factory);
        (await LeadAsync(gone, "Gone")).EnsureSuccessStatusCode();
        (await LeadAsync(stays, "Stays")).EnsureSuccessStatusCode();
        var goneId = await TenantIdAsync(gone);
        var slug = (await platform.Client.GetFromJsonAsync<PlatformCompanyDetailDto>($"/api/v1/platform/companies/{goneId}"))!.Company.Slug;

        // The company itself cannot do it, and the owner must type the right address.
        (await gone.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/platform/companies/{goneId}") { Content = JsonContent.Create(new PurgeCompanyRequest(slug)) }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await platform.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/platform/companies/{goneId}") { Content = JsonContent.Create(new PurgeCompanyRequest("wrong")) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var purge = await platform.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/platform/companies/{goneId}") { Content = JsonContent.Create(new PurgeCompanyRequest(slug)) });
        purge.StatusCode.ShouldBe(HttpStatusCode.Accepted, await purge.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Tenants.AnyAsync(t => t.Id == goneId)).ShouldBeFalse();
            (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == goneId)).ShouldBeFalse();
            (await db.Leads.IgnoreQueryFilters().AnyAsync(l => l.TenantId == goneId)).ShouldBeFalse();
        }

        (await gone.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var left = await (await stays.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(null, null, null, null)))
            .Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        left!.Items.ShouldContain(l => l.FirstName == "Stays");

        (await platform.Client.GetFromJsonAsync<PagedResult<PlatformAuditDto>>($"/api/v1/platform/activity?company={goneId}"))!
            .Items.ShouldContain(a => a.Action == "company.purged");
    }
}
