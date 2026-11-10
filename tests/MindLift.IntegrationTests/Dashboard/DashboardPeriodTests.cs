using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Dashboard;

/// <summary>The dashboard period: rolling windows (last 7, 30, 90 days) and a custom date range, and what they count.</summary>
public sealed class DashboardPeriodTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static decimal NewLeads(DashboardResponse dashboard)
    {
        var data = dashboard.Widgets.Single(w => w.Key == "kpi_strip").Data!.Value;
        var tiles = data.ValueKind == JsonValueKind.Array ? data : data.EnumerateObject().First(p => p.Value.ValueKind == JsonValueKind.Array).Value;
        var tile = tiles.EnumerateArray().Single(t => t.EnumerateObject().Any(p => p.Name.Equals("key", StringComparison.OrdinalIgnoreCase) && p.Value.GetString() == "new_leads"));
        return tile.EnumerateObject().First(p => p.Name.Equals("value", StringComparison.OrdinalIgnoreCase)).Value.GetDecimal();
    }

    private static async Task<HttpResponseMessage> GetAsync(Session s, string query) => await s.Client.GetAsync("/api/v1/dashboard" + query);

    private static async Task<DashboardResponse> DashboardAsync(Session s, string query)
    {
        var response = await GetAsync(s, query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DashboardResponse>())!;
    }

    [Fact]
    public async Task Rolling_windows_count_what_happened_inside_them()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Fresh", "Lead", "fresh@example.test", null, "Acme", "new", "website", null, null, null, null, null))).EnsureSuccessStatusCode();

        foreach (var preset in new[] { "last_7_days", "last_30_days", "last_90_days" })
        {
            NewLeads(await DashboardAsync(s, $"?period={preset}&refresh=true")).ShouldBe(1m, preset);
        }
    }

    [Fact]
    public async Task A_custom_range_counts_only_the_days_chosen_and_includes_both_end_dates()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Fresh", "Lead", "fresh@example.test", null, "Acme", "new", "website", null, null, null, null, null))).EnsureSuccessStatusCode();
        var today = DateTime.UtcNow.Date;

        NewLeads(await DashboardAsync(s, "?from=2020-01-01&to=2020-01-31&refresh=true")).ShouldBe(0m);
        NewLeads(await DashboardAsync(s, $"?from={today.AddDays(-3):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}&refresh=true")).ShouldBe(1m);
        // A single day, today to today, still includes today.
        NewLeads(await DashboardAsync(s, $"?from={today:yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}&refresh=true")).ShouldBe(1m);
    }

    [Fact]
    public async Task A_backwards_or_too_long_custom_range_is_refused()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await GetAsync(s, "?from=2026-03-10&to=2026-03-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(s, "?from=2024-01-01&to=2026-01-01")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetAsync(s, "?from=2026-01-01&to=2026-12-31")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_range_with_only_one_end_and_an_unknown_preset_fall_back_to_this_month()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await GetAsync(s, "?from=2026-01-01")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(s, "?period=nonsense")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_dashboard_needs_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/dashboard?period=last_7_days")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
