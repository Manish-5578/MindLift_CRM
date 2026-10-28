using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class LeadScoringTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(
        string first,
        string last,
        string? email = null,
        string? company = null,
        string status = "new",
        string? source = null,
        string? rating = null)
        => new(first, last, email, null, company, status, source, rating, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_lead_scoring_feature_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var ruleReq = new SaveLeadScoringRuleRequest
        {
            Name = "Disabled Rule",
            Points = 10,
            ConditionField = "Source",
            Operator = "Equals",
            ConditionValue = "website"
        };

        var postResp = await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", ruleReq);
        postResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var listResp = await admin.Client.GetAsync("/api/v1/scoring/rules");
        listResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var evalResp = await admin.Client.PostAsync($"/api/v1/scoring/evaluate/{Guid.NewGuid()}", null);
        evalResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task End_to_end_scoring_rule_crud()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create rule
        var createReq = new SaveLeadScoringRuleRequest
        {
            Name = "Tech Industry Bonus",
            Description = "Award 25 points for enterprise technology leads",
            RuleType = "Demographic",
            Points = 25,
            ConditionField = "Industry",
            Operator = "Equals",
            ConditionValue = "Technology",
            Order = 1
        };

        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", createReq);
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rule = await createResp.Content.ReadFromJsonAsync<LeadScoringRuleDto>();
        rule.ShouldNotBeNull();
        rule.Name.ShouldBe("Tech Industry Bonus");
        rule.Points.ShouldBe(25);

        // 2. Get by Id
        var getResp = await admin.Client.GetAsync($"/api/v1/scoring/rules/{rule.Id}");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var retrieved = await getResp.Content.ReadFromJsonAsync<LeadScoringRuleDto>();
        retrieved.ShouldNotBeNull();
        retrieved.Id.ShouldBe(rule.Id);

        // 3. Update rule
        createReq.Points = 30;
        createReq.Description = "Award 30 points for enterprise technology leads";
        var updateResp = await admin.Client.PutAsJsonAsync($"/api/v1/scoring/rules/{rule.Id}", createReq);
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<LeadScoringRuleDto>();
        updated.ShouldNotBeNull();
        updated.Points.ShouldBe(30);

        // 4. List rules
        var listResp = await admin.Client.GetAsync("/api/v1/scoring/rules");
        listResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rules = await listResp.Content.ReadFromJsonAsync<List<LeadScoringRuleDto>>();
        rules.ShouldNotBeNull();
        rules.Count.ShouldBeGreaterThanOrEqualTo(1);

        // 5. Delete rule
        var deleteResp = await admin.Client.DeleteAsync($"/api/v1/scoring/rules/{rule.Id}");
        deleteResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDelete = await admin.Client.GetAsync($"/api/v1/scoring/rules/{rule.Id}");
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lead_scoring_evaluates_demographic_and_behavioral_rules_into_hot_grade()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Setup 4 rules:
        // 1. HasEmail: +15 pts
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Email Provided",
            Points = 15,
            ConditionField = "HasEmail",
            Operator = "Occurred",
            Order = 1
        })).EnsureSuccessStatusCode();

        // 2. HasCompany: +15 pts
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Company Provided",
            Points = 15,
            ConditionField = "HasCompany",
            Operator = "Occurred",
            Order = 2
        })).EnsureSuccessStatusCode();

        // 3. Source = website: +20 pts
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Website Inbound",
            Points = 20,
            ConditionField = "Source",
            Operator = "Equals",
            ConditionValue = "website",
            Order = 3
        })).EnsureSuccessStatusCode();

        // 4. Rating = hot: +30 pts
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "High Intent Rating",
            Points = 30,
            ConditionField = "Rating",
            Operator = "Equals",
            ConditionValue = "hot",
            Order = 4
        })).EnsureSuccessStatusCode();

        // Create a lead that satisfies all 4 rules (Total = 15 + 15 + 20 + 30 = 80 pts -> Hot)
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Bruce", "Wayne", "bruce@wayne-enterprises.test", "Wayne Enterprises", "new", "website", "hot"));
        leadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        // Evaluate score
        var evalResp = await admin.Client.PostAsync($"/api/v1/scoring/evaluate/{lead.Id}", null);
        evalResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scoreResult = await evalResp.Content.ReadFromJsonAsync<LeadScoreResultDto>();

        scoreResult.ShouldNotBeNull();
        scoreResult.Score.ShouldBe(80);
        scoreResult.ScoreGrade.ShouldBe("Hot");
        scoreResult.AppliedRulesCount.ShouldBe(4);

        // Verify audit logs
        var logsResp = await admin.Client.GetAsync($"/api/v1/scoring/logs/{lead.Id}");
        logsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await logsResp.Content.ReadFromJsonAsync<List<LeadScoreLogDto>>();
        logs.ShouldNotBeNull();
        logs.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Lead_scoring_grades_warm_and_cold_correctly()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Rule 1: Email Provided (+15 pts)
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Has Email",
            Points = 15,
            ConditionField = "HasEmail",
            Operator = "Occurred",
            Order = 1
        })).EnsureSuccessStatusCode();

        // Rule 2: Company Provided (+25 pts)
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Has Company",
            Points = 25,
            ConditionField = "HasCompany",
            Operator = "Occurred",
            Order = 2
        })).EnsureSuccessStatusCode();

        // Cold Lead: Only has Email (15 pts < 30 -> Cold)
        var coldLeadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Clark", "Kent", "clark@dailyplanet.test", null, "new", null, null));
        var coldLead = (await coldLeadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var coldEval = await admin.Client.PostAsync($"/api/v1/scoring/evaluate/{coldLead.Id}", null);
        coldEval.StatusCode.ShouldBe(HttpStatusCode.OK);
        var coldResult = (await coldEval.Content.ReadFromJsonAsync<LeadScoreResultDto>())!;
        coldResult.Score.ShouldBe(15);
        coldResult.ScoreGrade.ShouldBe("Cold");

        // Warm Lead: Has Email + Company (15 + 25 = 40 pts, >= 30 and < 70 -> Warm)
        var warmLeadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Diana", "Prince", "diana@themyscira.test", "Themyscira Cultural", "new", null, null));
        var warmLead = (await warmLeadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        var warmEval = await admin.Client.PostAsync($"/api/v1/scoring/evaluate/{warmLead.Id}", null);
        warmEval.StatusCode.ShouldBe(HttpStatusCode.OK);
        var warmResult = (await warmEval.Content.ReadFromJsonAsync<LeadScoreResultDto>())!;
        warmResult.Score.ShouldBe(40);
        warmResult.ScoreGrade.ShouldBe("Warm");
    }

    [Fact]
    public async Task Negative_scoring_clamps_at_zero()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Rule: Disqualified penalty (-50 pts)
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Disqualified Penalty",
            Points = -50,
            ConditionField = "Status",
            Operator = "Equals",
            ConditionValue = "disqualified",
            Order = 1
        })).EnsureSuccessStatusCode();

        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads",
            NewLead("Lex", "Luthor", null, null, "new", null, null));
        leadResp.EnsureSuccessStatusCode();
        var lead = (await leadResp.Content.ReadFromJsonAsync<LeadDto>())!;

        (await admin.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/disqualify",
            new DisqualifyLeadRequest("Competitor", "Working for evil"))).EnsureSuccessStatusCode();

        var evalResp = await admin.Client.PostAsync($"/api/v1/scoring/evaluate/{lead.Id}", null);
        evalResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await evalResp.Content.ReadFromJsonAsync<LeadScoreResultDto>())!;

        // Negative score must clamp at 0 and be Cold
        result.Score.ShouldBe(0);
        result.ScoreGrade.ShouldBe("Cold");
        result.AppliedRulesCount.ShouldBe(1);
    }

    [Fact]
    public async Task Batch_evaluation_re_scores_all_active_leads()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Rule: HasEmail = +75 pts -> Hot
        (await admin.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Direct Email High Priority",
            Points = 75,
            ConditionField = "HasEmail",
            Operator = "Occurred",
            Order = 1
        })).EnsureSuccessStatusCode();

        // Create 2 leads with email (Hot) and 1 lead without email (Cold)
        await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Hot1", "User", "hot1@test.com"));
        await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Hot2", "User", "hot2@test.com"));
        await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Cold1", "User", null));

        var batchResp = await admin.Client.PostAsJsonAsync("/api/v1/scoring/batch-evaluate", new BatchScoreRequest());
        batchResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var batchResult = await batchResp.Content.ReadFromJsonAsync<BatchScoreResultDto>();

        batchResult.ShouldNotBeNull();
        batchResult.TotalEvaluated.ShouldBeGreaterThanOrEqualTo(3);
        batchResult.TotalHotLeads.ShouldBeGreaterThanOrEqualTo(2);
        batchResult.TotalColdLeads.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Multi_tenant_scoring_rule_isolation()
    {
        var tenantA = await Workspaces.SignupAsync(factory, "Scoring Alpha");
        var tenantB = await Workspaces.SignupAsync(factory, "Scoring Beta");

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/lead-scoring", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a rule
        var createResp = await tenantA.Client.PostAsJsonAsync("/api/v1/scoring/rules", new SaveLeadScoringRuleRequest
        {
            Name = "Alpha Rule",
            Points = 50,
            ConditionField = "HasEmail",
            Operator = "Occurred"
        });
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var alphaRule = (await createResp.Content.ReadFromJsonAsync<LeadScoringRuleDto>())!;

        // Tenant B lists rules -> should be empty
        var bListResp = await tenantB.Client.GetAsync("/api/v1/scoring/rules");
        bListResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bList = (await bListResp.Content.ReadFromJsonAsync<List<LeadScoringRuleDto>>())!;
        bList.ShouldBeEmpty();

        // Tenant B cannot get Tenant A's rule -> 404
        var bGetResp = await tenantB.Client.GetAsync($"/api/v1/scoring/rules/{alphaRule.Id}");
        bGetResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
