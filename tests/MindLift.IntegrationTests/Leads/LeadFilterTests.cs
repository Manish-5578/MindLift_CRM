using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>
/// The filter builder offers these operators (see FilterOperators). Each one must run against PostgreSQL, not only in memory.
/// </summary>
public sealed class LeadFilterTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Workspace(Session Owner, Guid OwnerId, string Tag);

    private async Task<Workspace> SeedAsync()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var tag = Guid.NewGuid().ToString("N")[..6];

        // Alice has an email, Bob has none, Alicia has an email and a different status.
        await CreateAsync(owner, $"Alice{tag}", $"alice{tag}@example.test", "new", me.UserId);
        await CreateAsync(owner, $"Bob{tag}", null, "working", me.UserId);
        await CreateAsync(owner, $"Alicia{tag}", $"alicia{tag}@example.test", "qualified", me.UserId);
        return new Workspace(owner, me.UserId, tag);
    }

    private static async Task CreateAsync(Session owner, string first, string? email, string status, Guid ownerId)
    {
        var request = new SaveLeadRequest(first, "Tester", email, null, null, status, "website", "warm", null, null, ownerId, null);
        var response = await owner.Client.PostAsJsonAsync("/api/v1/leads", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static FilterGroup Group(string field, FilterOperator op, object? value = null) => new()
    {
        Conditions = [new FilterCondition { Field = field, Op = op, Value = value is null ? null : JsonSerializer.SerializeToElement(value) }],
    };

    private static async Task<List<string>> NamesAsync(Workspace w, FilterGroup? filter, string? search = null)
    {
        var response = await w.Owner.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(search, filter, "first_name", "asc", 1, 100));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>())!;
        return page.Items.Select(i => i.FullName.Split(' ')[0]).ToList();
    }

    [Fact]
    public async Task Contains_is_case_insensitive_and_matches_part_of_a_word()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("first_name", FilterOperator.Contains, $"LICI"))).ShouldBe([$"Alicia{w.Tag}"]);
        (await NamesAsync(w, Group("first_name", FilterOperator.Contains, $"ali"))).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);
    }

    [Fact]
    public async Task Does_not_contain_leaves_out_the_matches()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("first_name", FilterOperator.NotContains, "ali"))).ShouldBe([$"Bob{w.Tag}"]);
    }

    [Fact]
    public async Task Starts_with_matches_only_the_beginning()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("first_name", FilterOperator.StartsWith, "alic"))).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);
        (await NamesAsync(w, Group("first_name", FilterOperator.StartsWith, "ob"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Is_and_is_not_compare_the_whole_value()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("first_name", FilterOperator.Eq, $"Bob{w.Tag}"))).ShouldBe([$"Bob{w.Tag}"]);
        (await NamesAsync(w, Group("first_name", FilterOperator.Neq, $"Bob{w.Tag}"))).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);
    }

    [Fact]
    public async Task Is_empty_and_is_not_empty_look_at_missing_values()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("email", FilterOperator.IsEmpty))).ShouldBe([$"Bob{w.Tag}"]);
        (await NamesAsync(w, Group("email", FilterOperator.IsNotEmpty))).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);
    }

    [Fact]
    public async Task Is_empty_on_a_field_that_is_never_empty_matches_nothing()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("owner_id", FilterOperator.IsEmpty))).ShouldBeEmpty();
        (await NamesAsync(w, Group("owner_id", FilterOperator.IsNotEmpty))).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Any_of_and_none_of_work_on_a_picklist()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("status", FilterOperator.In, new[] { "new", "qualified" }))).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);
        (await NamesAsync(w, Group("status", FilterOperator.NotIn, new[] { "new", "qualified" }))).ShouldBe([$"Bob{w.Tag}"]);
    }

    [Fact]
    public async Task Owner_can_be_matched_as_me_and_by_id()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("owner_id", FilterOperator.Eq, "@me"))).Count.ShouldBe(3);
        (await NamesAsync(w, Group("owner_id", FilterOperator.Eq, w.OwnerId.ToString()))).Count.ShouldBe(3);
        (await NamesAsync(w, Group("owner_id", FilterOperator.Neq, "@me"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Date_presets_and_ranges_work_on_the_created_date()
    {
        var w = await SeedAsync();
        var today = DateTime.UtcNow.Date;

        (await NamesAsync(w, Group("created_at", FilterOperator.DatePreset, "ThisYear"))).Count.ShouldBe(3);
        (await NamesAsync(w, Group("created_at", FilterOperator.DatePreset, "LastYear"))).ShouldBeEmpty();
        (await NamesAsync(w, Group("created_at", FilterOperator.Gte, today.AddDays(-1).ToString("yyyy-MM-dd")))).Count.ShouldBe(3);
        (await NamesAsync(w, Group("created_at", FilterOperator.Lte, today.AddDays(-2).ToString("yyyy-MM-dd")))).ShouldBeEmpty();
        (await NamesAsync(w, Group("created_at", FilterOperator.Between, new { from = today.AddDays(-1).ToString("yyyy-MM-dd"), to = today.AddDays(1).ToString("yyyy-MM-dd") }))).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Conditions_combine_with_and_or_and_nested_groups()
    {
        var w = await SeedAsync();
        var hasEmail = Group("email", FilterOperator.IsNotEmpty);
        var startsAli = Group("first_name", FilterOperator.StartsWith, "alic");
        var isBob = Group("first_name", FilterOperator.Eq, $"Bob{w.Tag}");

        // The list page sends the view's filter and the user's filters as two groups joined with AND.
        var both = new FilterGroup { Logic = "and", Groups = [hasEmail, startsAli] };
        (await NamesAsync(w, both)).ShouldBe([$"Alice{w.Tag}", $"Alicia{w.Tag}"]);

        var either = new FilterGroup { Logic = "or", Groups = [startsAli, isBob] };
        (await NamesAsync(w, either)).Count.ShouldBe(3);

        var nested = new FilterGroup { Logic = "and", Groups = [hasEmail, new FilterGroup { Logic = "or", Groups = [isBob, Group("first_name", FilterOperator.Eq, $"Alicia{w.Tag}")] }] };
        (await NamesAsync(w, nested)).ShouldBe([$"Alicia{w.Tag}"]);
    }

    [Fact]
    public async Task A_filter_and_a_search_apply_together()
    {
        var w = await SeedAsync();

        (await NamesAsync(w, Group("status", FilterOperator.In, new[] { "new", "qualified" }), search: "alicia")).ShouldBe([$"Alicia{w.Tag}"]);
    }

    [Fact]
    public async Task A_condition_on_a_field_that_does_not_exist_is_a_client_error_not_a_server_error()
    {
        var w = await SeedAsync();

        var response = await w.Owner.Client.PostAsJsonAsync("/api/v1/leads/query",
            new ListQuery(null, Group("not_a_field", FilterOperator.Eq, "x"), null, null, 1, 25));

        ((int)response.StatusCode).ShouldBeInRange(400, 499);
    }
}
