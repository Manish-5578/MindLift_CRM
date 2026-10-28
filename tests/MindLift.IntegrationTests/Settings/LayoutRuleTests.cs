using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Settings;

/// <summary>
/// Phase 2 / #10: Conditional Field Visibility / Layout Rules (layout-rules).
/// Tests CRUD operations, dynamic evaluation based on rule nodes, validation of mandatory fields,
/// and multi-tenant isolation.
/// </summary>
public sealed class LayoutRuleTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_layout_rule_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // List endpoint blocked (404)
        var listRes = await s.Client.GetAsync("/api/v1/layout-rules");
        listRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Create endpoint blocked (404)
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules", new SaveLayoutRuleRequest(
            "deals", "Lost Deal Layout", null, true, 1, null
        ));
        createRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Evaluate endpoint blocked (404)
        var evalRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/evaluate", new EvaluateLayoutRulesRequest(
            "deals", new Dictionary<string, object?>()
        ));
        evalRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Validate endpoint blocked (404)
        var validateRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/validate", new ValidateLayoutRulesRequest(
            "deals", new Dictionary<string, object?>()
        ));
        validateRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Layout_rule_crud_and_evaluation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "layout-rules");

        // 1. Create rule: when deal stage is 'Closed Lost', show 'lost_reason', 'lost_notes', hide 'next_step', require 'lost_reason', and set 'amount' read-only
        var condition = RuleNode.When("stage", RuleOperators.Eq, "Closed Lost");

        var createReq = new SaveLayoutRuleRequest(
            EntityType: "deals",
            Name: "Closed Lost Details",
            Description: "Displays and requires lost reasons when deal is lost",
            IsActive: true,
            Priority: 10,
            Condition: condition,
            ShowFields: ["lost_reason", "lost_notes"],
            HideFields: ["next_step"],
            MandatoryFields: ["lost_reason"],
            ReadOnlyFields: ["amount"]
        );

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules", createReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<LayoutRuleDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Closed Lost Details");
        created.EntityType.ShouldBe("deals");
        created.ShowFields.ShouldBe(["lost_reason", "lost_notes"]);
        created.HideFields.ShouldBe(["next_step"]);
        created.MandatoryFields.ShouldBe(["lost_reason"]);
        created.ReadOnlyFields.ShouldBe(["amount"]);

        // 2. List rules
        var list = await s.Client.GetFromJsonAsync<List<LayoutRuleDto>>("/api/v1/layout-rules?entityType=deals");
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Id.ShouldBe(created.Id);

        // 3. Evaluate when condition does not match (stage = "Qualification")
        var nonMatchRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/evaluate", new EvaluateLayoutRulesRequest(
            "deals",
            new Dictionary<string, object?> { ["stage"] = "Qualification" }
        ));
        nonMatchRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var nonMatchEval = await nonMatchRes.Content.ReadFromJsonAsync<LayoutRuleEvaluationResult>();
        nonMatchEval.ShouldNotBeNull();
        nonMatchEval.AppliedRules.ShouldBeEmpty();
        nonMatchEval.VisibleFields.ShouldBeEmpty();
        nonMatchEval.HiddenFields.ShouldBeEmpty();
        nonMatchEval.MandatoryFields.ShouldBeEmpty();
        nonMatchEval.ReadOnlyFields.ShouldBeEmpty();

        // 4. Evaluate when condition matches (stage = "Closed Lost")
        var matchRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/evaluate", new EvaluateLayoutRulesRequest(
            "deals",
            new Dictionary<string, object?> { ["stage"] = "Closed Lost" }
        ));
        matchRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var matchEval = await matchRes.Content.ReadFromJsonAsync<LayoutRuleEvaluationResult>();
        matchEval.ShouldNotBeNull();
        matchEval.AppliedRules.ShouldBe(["Closed Lost Details"]);
        matchEval.VisibleFields.ShouldContain("lost_reason");
        matchEval.VisibleFields.ShouldContain("lost_notes");
        matchEval.HiddenFields.ShouldContain("next_step");
        matchEval.MandatoryFields.ShouldContain("lost_reason");
        matchEval.ReadOnlyFields.ShouldContain("amount");

        // 5. Update rule
        var updateReq = createReq with
        {
            Name = "Updated Lost Details",
            ShowFields = ["lost_reason", "lost_notes", "competitor"]
        };
        var updateRes = await s.Client.PutAsJsonAsync($"/api/v1/layout-rules/{created.Id}", updateReq);
        updateRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateRes.Content.ReadFromJsonAsync<LayoutRuleDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Updated Lost Details");
        updated.ShowFields.ShouldContain("competitor");

        // 6. Delete rule
        var delRes = await s.Client.DeleteAsync($"/api/v1/layout-rules/{created.Id}");
        delRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var listAfterDel = await s.Client.GetFromJsonAsync<List<LayoutRuleDto>>("/api/v1/layout-rules?entityType=deals");
        listAfterDel.ShouldNotBeNull();
        listAfterDel.ShouldBeEmpty();
    }

    [Fact]
    public async Task Layout_rule_payload_validation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "layout-rules");

        // Create rule requiring 'rejection_reason' and 'notes' when status is 'Rejected'
        var condition = RuleNode.When("status", RuleOperators.Eq, "Rejected");
        var createReq = new SaveLayoutRuleRequest(
            EntityType: "leads",
            Name: "Lead Rejection Rule",
            Description: null,
            IsActive: true,
            Priority: 1,
            Condition: condition,
            MandatoryFields: ["rejection_reason", "notes"]
        );

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules", createReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Case A: status is 'Open' -> validation succeeds because rule condition doesn't match
        var openValRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/validate", new ValidateLayoutRulesRequest(
            "leads",
            new Dictionary<string, object?> { ["status"] = "Open" }
        ));
        openValRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var openVal = await openValRes.Content.ReadFromJsonAsync<LayoutRuleValidationResult>();
        openVal.ShouldNotBeNull();
        openVal.IsValid.ShouldBeTrue();
        openVal.Errors.ShouldBeEmpty();

        // Case B: status is 'Rejected' with missing fields -> validation fails
        var failValRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/validate", new ValidateLayoutRulesRequest(
            "leads",
            new Dictionary<string, object?> { ["status"] = "Rejected" }
        ));
        failValRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var failVal = await failValRes.Content.ReadFromJsonAsync<LayoutRuleValidationResult>();
        failVal.ShouldNotBeNull();
        failVal.IsValid.ShouldBeFalse();
        failVal.Errors.Count.ShouldBe(2);
        failVal.Errors.ShouldContain("Field 'rejection_reason' is required by layout rules.");
        failVal.Errors.ShouldContain("Field 'notes' is required by layout rules.");

        // Case C: status is 'Rejected' with fields present -> validation succeeds
        var successValRes = await s.Client.PostAsJsonAsync("/api/v1/layout-rules/validate", new ValidateLayoutRulesRequest(
            "leads",
            new Dictionary<string, object?>
            {
                ["status"] = "Rejected",
                ["rejection_reason"] = "Budget constraints",
                ["notes"] = "Client postponed until Q3"
            }
        ));
        successValRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var successVal = await successValRes.Content.ReadFromJsonAsync<LayoutRuleValidationResult>();
        successVal.ShouldNotBeNull();
        successVal.IsValid.ShouldBeTrue();
        successVal.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_layout_rules()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA, "layout-rules");
        await EnableFeatureAsync(tenantB, "layout-rules");

        // Tenant A creates a rule
        var createReq = new SaveLayoutRuleRequest(
            EntityType: "contacts",
            Name: "VIP Contact Rule",
            Description: null,
            IsActive: true,
            Priority: 1,
            Condition: RuleNode.When("type", RuleOperators.Eq, "VIP"),
            ShowFields: ["direct_phone", "assistant_name"]
        );
        var createRes = await tenantA.Client.PostAsJsonAsync("/api/v1/layout-rules", createReq);
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var ruleA = await createRes.Content.ReadFromJsonAsync<LayoutRuleDto>();
        ruleA.ShouldNotBeNull();

        // Tenant B lists rules -> empty
        var listB = await tenantB.Client.GetFromJsonAsync<List<LayoutRuleDto>>("/api/v1/layout-rules?entityType=contacts");
        listB.ShouldNotBeNull();
        listB.ShouldBeEmpty();

        // Tenant B attempts to get Tenant A's rule -> 404
        var getBRes = await tenantB.Client.GetAsync($"/api/v1/layout-rules/{ruleA.Id}");
        getBRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B evaluates against contacts -> rule is NOT applied
        var evalBRes = await tenantB.Client.PostAsJsonAsync("/api/v1/layout-rules/evaluate", new EvaluateLayoutRulesRequest(
            "contacts",
            new Dictionary<string, object?> { ["type"] = "VIP" }
        ));
        evalBRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var evalB = await evalBRes.Content.ReadFromJsonAsync<LayoutRuleEvaluationResult>();
        evalB.ShouldNotBeNull();
        evalB.AppliedRules.ShouldBeEmpty();
        evalB.VisibleFields.ShouldBeEmpty();

        // Tenant B attempts to delete Tenant A's rule -> 404
        var delBRes = await tenantB.Client.DeleteAsync($"/api/v1/layout-rules/{ruleA.Id}");
        delBRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
