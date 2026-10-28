using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Analytics;

public sealed class CustomerHealthTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveAccountRequest NewAccount(string name)
        => new(name, "https://acme.org", "+1555123456", "SaaS", "Enterprise", null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, Guid? accountId)
        => new(first, last, email, "+1555987654", "VP Customer Success", null, accountId, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_customer_health_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync("/api/v1/customer-health/portfolio");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var getOn = await admin.Client.GetAsync("/api/v1/customer-health/portfolio");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Submit_surveys_and_validate_sentiment_classification()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var accResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Surveyed Client"));
        var acc = await accResp.Content.ReadFromJsonAsync<AccountDto>();
        acc.ShouldNotBeNull();

        // 1. Submit NPS 10 -> Promoter
        var nps10 = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "NPS",
            Score: 10,
            Comment: "Best platform we have ever used!",
            AccountId: acc.Id));
        nps10.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s1 = await nps10.Content.ReadFromJsonAsync<CustomerSurveyDto>();
        s1.ShouldNotBeNull();
        s1.Sentiment.ShouldBe("Promoter");
        s1.AccountName.ShouldBe("Surveyed Client");

        // 2. Submit NPS 7 -> Passive
        var nps7 = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "NPS",
            Score: 7,
            AccountId: acc.Id));
        nps7.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s2 = await nps7.Content.ReadFromJsonAsync<CustomerSurveyDto>();
        s2.ShouldNotBeNull();
        s2.Sentiment.ShouldBe("Passive");

        // 3. Submit NPS 4 -> Detractor
        var nps4 = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "NPS",
            Score: 4,
            Comment: "Too many unresolved bugs",
            AccountId: acc.Id));
        nps4.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s3 = await nps4.Content.ReadFromJsonAsync<CustomerSurveyDto>();
        s3.ShouldNotBeNull();
        s3.Sentiment.ShouldBe("Detractor");

        // 4. Submit CSAT 5 -> Satisfied
        var csat5 = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "CSAT",
            Score: 5,
            AccountId: acc.Id));
        csat5.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s4 = await csat5.Content.ReadFromJsonAsync<CustomerSurveyDto>();
        s4.ShouldNotBeNull();
        s4.Sentiment.ShouldBe("Satisfied");

        // 5. Submit CSAT 1 -> Dissatisfied
        var csat1 = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "CSAT",
            Score: 1,
            AccountId: acc.Id));
        csat1.StatusCode.ShouldBe(HttpStatusCode.Created);
        var s5 = await csat1.Content.ReadFromJsonAsync<CustomerSurveyDto>();
        s5.ShouldNotBeNull();
        s5.Sentiment.ShouldBe("Dissatisfied");

        // 6. Invalid score checks
        var badNps = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "NPS",
            Score: 11,
            AccountId: acc.Id));
        badNps.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var badCsat = await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest(
            Type: "CSAT",
            Score: 6,
            AccountId: acc.Id));
        badCsat.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Calculate_nps_and_csat_summary_metrics()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var accResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Analytics Account"));
        var acc = await accResp.Content.ReadFromJsonAsync<AccountDto>();
        acc.ShouldNotBeNull();

        // Submit 2 Promoters (10, 9), 1 Passive (8), 1 Detractor (3)
        // Total = 4. NPS = (2 - 1) / 4 * 100 = +25.0
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 10, AccountId: acc.Id));
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 9, AccountId: acc.Id));
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 8, AccountId: acc.Id));
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 3, AccountId: acc.Id));

        var npsSummaryResp = await admin.Client.GetAsync($"/api/v1/customer-health/surveys/summary?type=NPS&accountId={acc.Id}");
        npsSummaryResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var npsSummary = await npsSummaryResp.Content.ReadFromJsonAsync<SurveySummaryDto>();

        npsSummary.ShouldNotBeNull();
        npsSummary.TotalResponses.ShouldBe(4);
        npsSummary.PromotersCount.ShouldBe(2);
        npsSummary.PassivesCount.ShouldBe(1);
        npsSummary.DetractorsCount.ShouldBe(1);
        npsSummary.NpsScore.ShouldBe(25.0m);
    }

    [Fact]
    public async Task Account_health_score_evaluation_and_portfolio_risk_overview()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Healthy Account (good surveys)
        var healthyResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Healthy Cloud Corp"));
        var healthyAcc = await healthyResp.Content.ReadFromJsonAsync<AccountDto>();
        healthyAcc.ShouldNotBeNull();

        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 10, AccountId: healthyAcc.Id));
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 9, AccountId: healthyAcc.Id));

        var healthCheck = await admin.Client.GetAsync($"/api/v1/customer-health/accounts/{healthyAcc.Id}");
        healthCheck.StatusCode.ShouldBe(HttpStatusCode.OK);
        var healthyScore = await healthCheck.Content.ReadFromJsonAsync<AccountHealthScoreDto>();

        healthyScore.ShouldNotBeNull();
        healthyScore.HealthScore.ShouldBeGreaterThanOrEqualTo(70);
        healthyScore.HealthStatus.ShouldBe("Good");
        healthyScore.NpsScoreComponent.ShouldBe(100);

        // 2. Create At-Risk Account (bad surveys and open support tickets)
        var riskResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Struggling Retailer"));
        var riskAcc = await riskResp.Content.ReadFromJsonAsync<AccountDto>();
        riskAcc.ShouldNotBeNull();

        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 2, "Terrible service", AccountId: riskAcc.Id));
        await admin.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 3, "Still down", AccountId: riskAcc.Id));

        var riskCheck = await admin.Client.GetAsync($"/api/v1/customer-health/accounts/{riskAcc.Id}");
        riskCheck.StatusCode.ShouldBe(HttpStatusCode.OK);
        var riskScore = await riskCheck.Content.ReadFromJsonAsync<AccountHealthScoreDto>();

        riskScore.ShouldNotBeNull();
        riskScore.HealthScore.ShouldBeLessThan(60);
        riskScore.RiskFactors.ShouldNotBeEmpty();

        // 3. Query portfolio overview
        var portResp = await admin.Client.GetAsync("/api/v1/customer-health/portfolio");
        portResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var portfolio = await portResp.Content.ReadFromJsonAsync<PortfolioHealthOverviewDto>();

        portfolio.ShouldNotBeNull();
        portfolio.TotalAccounts.ShouldBe(2);
        portfolio.HealthyCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_health_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/customer-health", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var accRespA = await tenantA.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Tenant A Secret Client"));
        var accA = await accRespA.Content.ReadFromJsonAsync<AccountDto>();
        accA.ShouldNotBeNull();

        var surveyResp = await tenantA.Client.PostAsJsonAsync("/api/v1/customer-health/surveys", new SubmitSurveyRequest("NPS", 10, AccountId: accA.Id));
        surveyResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Tenant B attempts to read account health for Tenant A's account -> 404
        var crossHealth = await tenantB.Client.GetAsync($"/api/v1/customer-health/accounts/{accA.Id}");
        crossHealth.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B queries surveys -> should be empty
        var surveysB = await tenantB.Client.GetFromJsonAsync<List<CustomerSurveyDto>>("/api/v1/customer-health/surveys");
        surveysB.ShouldNotBeNull();
        surveysB.ShouldBeEmpty();
    }
}
