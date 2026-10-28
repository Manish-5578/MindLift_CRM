using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Automation;

public sealed class DataEnrichmentTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest NewLead(string first, string last, string email)
        => new(first, last, email, null, null, "new", "website", null, null, null, null, null);

    private static SaveAccountRequest NewAccount(string name, string website)
        => new(name, website, null, null, null, null, null, null);

    [Fact]
    public async Task Returns_404_when_data_enrichment_flag_is_disabled()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var lookupResp = await admin.Client.GetAsync("/api/v1/enrichment/lookup?domain=stripe.com");
        lookupResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var leadResp = await admin.Client.PostAsync($"/api/v1/enrichment/leads/{Guid.NewGuid()}", null);
        leadResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var configResp = await admin.Client.GetAsync("/api/v1/enrichment/config");
        configResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Real_time_firmographic_lookup_and_freemail_filtering()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Direct domain lookup (Stripe)
        var stripeResp = await admin.Client.GetAsync("/api/v1/enrichment/lookup?domain=stripe.com");
        stripeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stripe = await stripeResp.Content.ReadFromJsonAsync<CompanyEnrichmentData>();

        stripe.ShouldNotBeNull();
        stripe.Domain.ShouldBe("stripe.com");
        stripe.Name.ShouldBe("Stripe, Inc.");
        stripe.Industry.ShouldContain("Fintech");
        stripe.City.ShouldBe("South San Francisco");
        stripe.Country.ShouldBe("United States");
        stripe.TechStack.ShouldContain("Ruby");
        stripe.TechStack.ShouldContain("PostgreSQL");

        // 2. Email lookup (extracts domain from email)
        var datadogResp = await admin.Client.GetAsync("/api/v1/enrichment/lookup?email=alex@datadog.com");
        datadogResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var datadog = await datadogResp.Content.ReadFromJsonAsync<CompanyEnrichmentData>();

        datadog.ShouldNotBeNull();
        datadog.Domain.ShouldBe("datadog.com");
        datadog.Name.ShouldBe("Datadog, Inc.");
        datadog.City.ShouldBe("New York");

        // 3. Freemail lookup returns 404 (cannot enrich personal email)
        var freemailResp = await admin.Client.GetAsync("/api/v1/enrichment/lookup?email=john.doe@gmail.com");
        freemailResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Single_lead_auto_enrichment_and_audit_logging()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create a lead with corporate email and empty company details
        var leadResp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Patrick", "Collison", "patrick@stripe.com"));
        leadResp.EnsureSuccessStatusCode();
        var lead = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        lead.ShouldNotBeNull();
        lead.CompanyName.ShouldBeNullOrWhiteSpace();

        // 2. Enrich the lead
        var enrichResp = await admin.Client.PostAsync($"/api/v1/enrichment/leads/{lead.Id}", null);
        enrichResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrichResult = await enrichResp.Content.ReadFromJsonAsync<EnrichmentResultDto>();

        enrichResult.ShouldNotBeNull();
        enrichResult.Status.ShouldBe("Enriched");
        enrichResult.FieldsUpdated.ShouldContain("CompanyName");
        enrichResult.FieldsUpdated.ShouldContain("Description");
        enrichResult.FieldsUpdated.ShouldContain("Phone");

        // 3. Verify lead was updated in database
        var getLeadResp = await admin.Client.GetAsync($"/api/v1/leads/{lead.Id}");
        getLeadResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedLead = await getLeadResp.Content.ReadFromJsonAsync<LeadDto>();

        updatedLead.ShouldNotBeNull();
        updatedLead.CompanyName.ShouldBe("Stripe, Inc.");
        updatedLead.Description.ShouldNotBeNullOrWhiteSpace();
        updatedLead.Phone.ShouldBe("+1 888-926-2289");

        // 4. Verify enrichment audit log
        var logsResp = await admin.Client.GetAsync($"/api/v1/enrichment/logs?entityId={lead.Id}");
        logsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await logsResp.Content.ReadFromJsonAsync<List<EnrichmentLogDto>>();

        logs.ShouldNotBeNull();
        logs.Count.ShouldBeGreaterThanOrEqualTo(1);
        var log = logs.First();
        log.EntityType.ShouldBe("Lead");
        log.EntityId.ShouldBe(lead.Id);
        log.Domain.ShouldBe("stripe.com");
        log.Status.ShouldBe("Enriched");
    }

    [Fact]
    public async Task Single_account_auto_enrichment()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create an account with website only
        var acctResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Shopify Merchant", "https://shopify.com"));
        acctResp.EnsureSuccessStatusCode();
        var acct = await acctResp.Content.ReadFromJsonAsync<AccountDto>();
        acct.ShouldNotBeNull();

        // 2. Enrich the account
        var enrichResp = await admin.Client.PostAsync($"/api/v1/enrichment/accounts/{acct.Id}", null);
        enrichResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrichResult = await enrichResp.Content.ReadFromJsonAsync<EnrichmentResultDto>();

        enrichResult.ShouldNotBeNull();
        enrichResult.Status.ShouldBe("Enriched");
        enrichResult.FieldsUpdated.ShouldContain("Industry");
        enrichResult.FieldsUpdated.ShouldContain("Description");
        enrichResult.FieldsUpdated.ShouldContain("Phone");

        // 3. Verify account updated
        var getAcctResp = await admin.Client.GetAsync($"/api/v1/accounts/{acct.Id}");
        getAcctResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedAcct = await getAcctResp.Content.ReadFromJsonAsync<AccountDto>();

        updatedAcct.ShouldNotBeNull();
        updatedAcct.Industry.ShouldBe("E-Commerce & Retail Tech");
        updatedAcct.Phone.ShouldBe("+1 888-746-7439");
        updatedAcct.Description.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Batch_enrichment_of_multiple_leads()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create 2 leads
        var lead1Resp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Scott", "Farquhar", "scott@atlassian.com"));
        lead1Resp.EnsureSuccessStatusCode();
        var lead1 = await lead1Resp.Content.ReadFromJsonAsync<LeadDto>();
        lead1.ShouldNotBeNull();

        var lead2Resp = await admin.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Olivier", "Pomel", "olivier@datadog.com"));
        lead2Resp.EnsureSuccessStatusCode();
        var lead2 = await lead2Resp.Content.ReadFromJsonAsync<LeadDto>();
        lead2.ShouldNotBeNull();

        // 2. Batch enrich
        var batchReq = new BatchEnrichRequest
        {
            EntityType = "Lead",
            EntityIds = [lead1.Id, lead2.Id],
            Overwrite = false
        };
        var batchResp = await admin.Client.PostAsJsonAsync("/api/v1/enrichment/batch", batchReq);
        batchResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var results = await batchResp.Content.ReadFromJsonAsync<List<EnrichmentResultDto>>();

        results.ShouldNotBeNull();
        results.Count.ShouldBe(2);
        results.ShouldContain(r => r.EntityId == lead1.Id && r.Status == "Enriched");
        results.ShouldContain(r => r.EntityId == lead2.Id && r.Status == "Enriched");
    }

    [Fact]
    public async Task Config_management_and_domain_exclusions()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Get default config
        var getCfgResp = await admin.Client.GetAsync("/api/v1/enrichment/config");
        getCfgResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cfg = await getCfgResp.Content.ReadFromJsonAsync<EnrichmentConfigDto>();

        cfg.ShouldNotBeNull();
        cfg.AutoEnrichNewLeads.ShouldBeTrue();

        // 2. Update config to exclude competitor domain
        var updateCfgReq = new SaveEnrichmentConfigRequest
        {
            AutoEnrichNewLeads = false,
            AutoEnrichNewAccounts = true,
            OverwriteExistingFields = true,
            ExcludedDomains = ["blocked-competitor.com", "gmail.com"]
        };
        var putCfgResp = await admin.Client.PutAsJsonAsync("/api/v1/enrichment/config", updateCfgReq);
        putCfgResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedCfg = await putCfgResp.Content.ReadFromJsonAsync<EnrichmentConfigDto>();

        updatedCfg.ShouldNotBeNull();
        updatedCfg.AutoEnrichNewLeads.ShouldBeFalse();
        updatedCfg.ExcludedDomains.ShouldContain("blocked-competitor.com");

        // 3. Lookup for excluded domain returns 404
        var lookupResp = await admin.Client.GetAsync("/api/v1/enrichment/lookup?domain=blocked-competitor.com");
        lookupResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Strict_multi_tenant_isolation_fails_closed()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/data-enrichment", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates and enriches a lead
        var leadResp = await tenantA.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Private", "Lead", "contact@stripe.com"));
        leadResp.EnsureSuccessStatusCode();
        var leadA = await leadResp.Content.ReadFromJsonAsync<LeadDto>();
        leadA.ShouldNotBeNull();

        await tenantA.Client.PostAsync($"/api/v1/enrichment/leads/{leadA.Id}", null);

        // Tenant B attempts to enrich Tenant A's lead -> 404
        var crossEnrich = await tenantB.Client.PostAsync($"/api/v1/enrichment/leads/{leadA.Id}", null);
        crossEnrich.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B queries logs -> does not see Tenant A's lead log
        var logsB = await tenantB.Client.GetAsync($"/api/v1/enrichment/logs?entityId={leadA.Id}");
        logsB.StatusCode.ShouldBe(HttpStatusCode.OK);
        var logs = await logsB.Content.ReadFromJsonAsync<List<EnrichmentLogDto>>();
        logs.ShouldNotBeNull();
        logs.ShouldBeEmpty();
    }
}
