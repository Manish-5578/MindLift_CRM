using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>Plan 7.3 Assignment: who owns a new lead when nobody is named.</summary>
public sealed class AssignmentRuleTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Team(Session Admin, Guid AdminId, Session StaffA, Guid A, Session StaffB, Guid B);

    private async Task<Team> TeamAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staffA = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rule-a");
        var staffB = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rule-b");
        return new Team(
            admin, await IdAsync(admin),
            staffA, await IdAsync(staffA),
            staffB, await IdAsync(staffB));
    }

    private static async Task<Guid> IdAsync(Session session)
        => (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

    private static async Task<AssignmentRuleDto> RuleAsync(Session admin, string name, string method, Guid[] users, int priority = 1, bool active = true, FilterGroup? conditions = null)
    {
        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules",
            new SaveAssignmentRuleRequest(name, active, priority, method, users, conditions));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AssignmentRuleDto>())!;
    }

    /// <summary>Creates a lead with no owner named, so the rules decide, and returns who owns it.</summary>
    private static async Task<Guid> NewLeadOwnerAsync(Session session, string source = "website")
    {
        var request = new SaveLeadRequest($"Lead{Guid.NewGuid():N}"[..12], "Tester", null, null, null, "new", source, null, null, null, null, null);
        var response = await session.Client.PostAsJsonAsync("/api/v1/leads", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!.OwnerId;
    }

    private static async Task GiveLeadsToAsync(Session admin, Guid owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var request = new SaveLeadRequest($"Held{Guid.NewGuid():N}"[..12], "Tester", null, null, null, "new", "website", null, null, null, owner, null);
            (await admin.Client.PostAsJsonAsync("/api/v1/leads", request)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static FilterGroup SourceIs(string source) => new()
    {
        Conditions = [new FilterCondition { Field = "source", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement(source) }],
    };

    [Fact]
    public async Task With_no_rules_the_workspace_owner_gets_the_lead()
    {
        var t = await TeamAsync();

        (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.AdminId);
    }

    [Fact]
    public async Task Round_robin_takes_turns_in_the_order_of_the_rule()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Turns", "RoundRobin", [t.A, t.B]);

        var owners = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            owners.Add(await NewLeadOwnerAsync(t.Admin));
        }

        // The first turn goes to the person after the one last used, and then it alternates.
        owners.Distinct().Count().ShouldBe(2);
        owners.Zip(owners.Skip(1)).ShouldAllBe(pair => pair.First != pair.Second);
        owners.ShouldAllBe(o => o == t.A || o == t.B);
    }

    [Fact]
    public async Task Round_robin_gives_each_person_a_fair_share()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Turns", "RoundRobin", [t.A, t.B]);

        var owners = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            owners.Add(await NewLeadOwnerAsync(t.Admin));
        }

        owners.Count(o => o == t.A).ShouldBe(3);
        owners.Count(o => o == t.B).ShouldBe(3);
    }

    [Fact]
    public async Task Fewest_open_leads_picks_the_person_with_the_lightest_load()
    {
        var t = await TeamAsync();
        await GiveLeadsToAsync(t.Admin, t.A, 2);
        await RuleAsync(t.Admin, "Lightest", "LoadBalanced", [t.A, t.B]);

        var owners = new List<Guid>
        {
            await NewLeadOwnerAsync(t.Admin),
            await NewLeadOwnerAsync(t.Admin),
            await NewLeadOwnerAsync(t.Admin),
        };

        // B has 0 and A has 2, so B takes the first two. Then both hold 2 and the tie goes to the first person in the rule.
        owners.ShouldBe([t.B, t.B, t.A]);
    }

    [Fact]
    public async Task A_specific_person_gets_every_matching_lead()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Always B", "SpecificUser", [t.B]);

        for (var i = 0; i < 3; i++)
        {
            (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.B);
        }
    }

    [Fact]
    public async Task A_rule_with_a_condition_only_applies_to_leads_that_match_it()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Web leads to A", "SpecificUser", [t.A], conditions: SourceIs("website"));

        (await NewLeadOwnerAsync(t.Admin, "website")).ShouldBe(t.A);
        (await NewLeadOwnerAsync(t.Admin, "referral")).ShouldBe(t.AdminId);
    }

    [Fact]
    public async Task The_lowest_priority_number_wins_when_two_rules_match()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Second", "SpecificUser", [t.B], priority: 2);
        await RuleAsync(t.Admin, "First", "SpecificUser", [t.A], priority: 1);

        (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.A);
    }

    [Fact]
    public async Task A_rule_that_is_switched_off_is_ignored()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Off", "SpecificUser", [t.A], priority: 1, active: false);
        await RuleAsync(t.Admin, "On", "SpecificUser", [t.B], priority: 2);

        (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.B);
    }

    [Fact]
    public async Task A_rule_whose_people_are_all_deactivated_falls_through_to_the_next_rule()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "A only", "SpecificUser", [t.A], priority: 1);
        await RuleAsync(t.Admin, "B", "SpecificUser", [t.B], priority: 2);
        (await t.Admin.Client.PostAsJsonAsync($"/api/v1/users/{t.A}/deactivate", new DeactivateUserRequest(null))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.B);
    }

    [Fact]
    public async Task A_deactivated_person_is_skipped_inside_a_round_robin()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Turns", "RoundRobin", [t.A, t.B]);
        (await t.Admin.Client.PostAsJsonAsync($"/api/v1/users/{t.A}/deactivate", new DeactivateUserRequest(null))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        for (var i = 0; i < 3; i++)
        {
            (await NewLeadOwnerAsync(t.Admin)).ShouldBe(t.B);
        }
    }

    [Fact]
    public async Task A_lead_that_names_an_owner_ignores_the_rules()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Always B", "SpecificUser", [t.B]);

        var request = new SaveLeadRequest("Named", "Owner", null, null, null, "new", "website", null, null, null, t.A, null);
        var lead = await (await t.Admin.Client.PostAsJsonAsync("/api/v1/leads", request)).Content.ReadFromJsonAsync<LeadDto>();

        lead!.OwnerId.ShouldBe(t.A);
    }

    [Fact]
    public async Task A_web_form_lead_is_assigned_by_the_rules_too()
    {
        var t = await TeamAsync();
        await RuleAsync(t.Admin, "Always B", "SpecificUser", [t.B]);
        var form = await (await t.Admin.Client.PostAsJsonAsync("/api/v1/settings/web-forms",
            new SaveWebFormRequest("Contact us", "website", "Thanks!", true, ["first_name", "email"]))).Content.ReadFromJsonAsync<WebFormDto>();
        var email = $"form-{Guid.NewGuid():N}@example.test";

        var submit = await Workspaces.MobileClient(factory).PostAsJsonAsync($"/api/v1/public/forms/{form!.PublicToken}",
            new PublicFormSubmission("Web", "Visitor", email, null, null, null, null));
        submit.StatusCode.ShouldBe(HttpStatusCode.OK);

        var page = await (await t.Admin.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(email, null, null, null, 1, 10))).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        page!.Items.ShouldHaveSingleItem().OwnerId.ShouldBe(t.B);
    }

    [Fact]
    public async Task Staff_cannot_create_change_or_delete_rules()
    {
        var t = await TeamAsync();
        var rule = await RuleAsync(t.Admin, "Rule", "SpecificUser", [t.B]);
        var body = new SaveAssignmentRuleRequest("Hacked", true, 1, "SpecificUser", [t.A], null);

        (await t.StaffA.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await t.StaffA.Client.PutAsJsonAsync($"/api/v1/settings/assignment-rules/{rule.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await t.StaffA.Client.DeleteAsync($"/api/v1/settings/assignment-rules/{rule.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unknown_method_is_refused()
    {
        var t = await TeamAsync();

        var response = await t.Admin.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules",
            new SaveAssignmentRuleRequest("Odd", true, 1, "Random", [t.A], null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rules_belong_to_one_workspace()
    {
        var one = await TeamAsync();
        var two = await TeamAsync();
        await RuleAsync(one.Admin, "Mine", "SpecificUser", [one.A]);

        (await two.Admin.Client.GetFromJsonAsync<List<AssignmentRuleDto>>("/api/v1/settings/assignment-rules"))!.ShouldBeEmpty();
        (await NewLeadOwnerAsync(two.Admin)).ShouldBe(two.AdminId);
    }
}
