using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 1 / #5 & #6: Deal stage gating and validation rules (F2 rules engine).
/// Validates that stage moves cannot bypass gating criteria via API or Kanban drag.
/// </summary>
public sealed class DealStageGatingTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private sealed record SetupResult(Session Session, PipelineDto Pipeline, PipelineStageDto FirstStage, PipelineStageDto SecondStage);

    private static async Task<SetupResult> SetupWorkspaceAsync(Session s)
    {
        (await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var pipelines = (await s.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!;
        var pipeline = pipelines.First(p => p.IsDefault);
        var openStages = pipeline.Stages.Where(st => st.StageType == "Open").OrderBy(st => st.SortOrder).ToList();
        return new SetupResult(s, pipeline, openStages[0], openStages[1]);
    }

    private static async Task<DealDto> CreateDealAsync(Session s, decimal amount = 0m, Guid? accountId = null, Guid? contactId = null)
    {
        IReadOnlyList<DealContactInput>? contacts = contactId.HasValue
            ? [new DealContactInput(contactId.Value, "Signer", true)]
            : null;

        var r = await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            "Acme Upgrade",
            null,
            null,
            amount,
            null,
            20,
            DateTime.UtcNow.AddMonths(1),
            accountId,
            contacts,
            null,
            null,
            null
        ));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<DealDto>())!;
    }

    [Fact]
    public async Task Endpoints_return_not_found_when_feature_flag_is_off()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var setup = await SetupWorkspaceAsync(owner);

        var r = await owner.Client.GetAsync($"/api/v1/settings/pipelines/{setup.Pipeline.Id}/stages/{setup.SecondStage.Id}/gates");
        r.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_flag_is_off_deals_advance_without_gating_checks()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var setup = await SetupWorkspaceAsync(owner);
        var deal = await CreateDealAsync(owner, amount: 10m);

        // Advance stage without gating rules
        var move = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        move.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Gating_blocks_stage_advancement_when_min_amount_or_line_items_or_fields_missing()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner, "deal-stage-gating");
        await EnableFeatureAsync(owner, "deal-line-items");
        var setup = await SetupWorkspaceAsync(owner);

        // Create a gate on SecondStage requiring Amount >= 1000, line items, and account_id
        var gateRes = await owner.Client.PostAsJsonAsync(
            $"/api/v1/settings/pipelines/{setup.Pipeline.Id}/stages/{setup.SecondStage.Id}/gates",
            new SaveDealStageGateRequest(
                "Qualification Gate",
                "Requires qualified deal info",
                true,
                RequiredFields: ["account_id"],
                MinAmount: 1000m,
                RequireLineItems: true,
                Condition: null,
                AllowedRoles: null,
                FailureMessage: "Deal must have at least 1,000 amount, line items, and an account attached."
            )
        );
        gateRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deal 1: Amount = 100, no line items, no account
        var deal = await CreateDealAsync(owner, amount: 100m);

        // 1. Blocked because Amount < 1000
        var move1 = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        move1.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // 2. Update amount to 1500, but still no line items
        await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}", new SaveDealRequest(
            deal.Name, null, null, 1500m, null, null, null, null, null, null, null, null));

        var move2 = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        move2.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // 3. Add a line item, but still no account
        await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/line-items", new SaveDealLineItemRequest(
            null, null, "Custom Service", null, Quantity: 1m, UnitPrice: 1500m, null, null, null));

        var move3 = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        move3.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // 4. Create an account and attach to deal
        var accRes = await owner.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Acme Corp", null, null, null, null, null, null, null));
        var account = (await accRes.Content.ReadFromJsonAsync<AccountDto>())!;

        await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}", new SaveDealRequest(
            deal.Name, null, null, 1500m, null, null, null, account.Id, null, null, null, null));

        // 5. Now all gate conditions are satisfied -> stage advancement succeeds!
        var moveFinal = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{deal.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        moveFinal.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await moveFinal.Content.ReadFromJsonAsync<DealDto>())!;
        updated.StageId.ShouldBe(setup.SecondStage.Id);
    }

    [Fact]
    public async Task Gating_evaluates_f2_rule_node_conditions()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner, "deal-stage-gating");
        var setup = await SetupWorkspaceAsync(owner);

        // Configure a rule node gate: amount >= 5000
        var condition = new RuleNode(
            Kind: RuleNode.Condition,
            Field: "amount",
            Operator: RuleOperators.GreaterOrEqual,
            Value: "5000"
        );

        var gateRes = await owner.Client.PostAsJsonAsync(
            $"/api/v1/settings/pipelines/{setup.Pipeline.Id}/stages/{setup.SecondStage.Id}/gates",
            new SaveDealStageGateRequest(
                "High Value Gate",
                null,
                true,
                null,
                null,
                false,
                condition,
                null,
                "Requires deal amount of at least 5,000."
            )
        );
        gateRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deal with amount = 2000 -> blocked
        var dealLow = await CreateDealAsync(owner, amount: 2000m);
        var moveLow = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{dealLow.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        moveLow.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Deal with amount = 6000 -> allowed
        var dealHigh = await CreateDealAsync(owner, amount: 6000m);
        var moveHigh = await owner.Client.PatchAsJsonAsync($"/api/v1/deals/{dealHigh.Id}/stage", new ChangeDealStageRequest(setup.SecondStage.Id, null, null, null, null));
        moveHigh.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_company_cannot_access_or_modify_stage_gates()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(a, "deal-stage-gating");
        await EnableFeatureAsync(b, "deal-stage-gating");

        var aSetup = await SetupWorkspaceAsync(a);
        var bSetup = await SetupWorkspaceAsync(b);

        // Company A creates gate
        var createRes = await a.Client.PostAsJsonAsync(
            $"/api/v1/settings/pipelines/{aSetup.Pipeline.Id}/stages/{aSetup.SecondStage.Id}/gates",
            new SaveDealStageGateRequest("A Gate", null, true, null, 100m, false, null, null, null));
        createRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var gate = (await createRes.Content.ReadFromJsonAsync<DealStageGateDto>())!;

        // Company B cannot read Company A's gates
        var listB = (await b.Client.GetFromJsonAsync<List<DealStageGateDto>>(
            $"/api/v1/settings/pipelines/{aSetup.Pipeline.Id}/stages/{aSetup.SecondStage.Id}/gates"))!;
        listB.ShouldBeEmpty();

        // Company B cannot delete Company A's gate
        var delB = await b.Client.DeleteAsync(
            $"/api/v1/settings/pipelines/{aSetup.Pipeline.Id}/stages/{aSetup.SecondStage.Id}/gates/{gate.Id}");
        delB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
