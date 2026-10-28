using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Settings;

/// <summary>
/// Phase 2 / #9: Dependent Picklists (dependent-picklists).
/// Tests CRUD operations, dynamic allowed-value resolution based on controlling values,
/// payload validation, and tenant isolation.
/// </summary>
public sealed class DependentPicklistTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_dependent_picklist_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // List endpoint blocked (404)
        var listRes = await s.Client.GetAsync("/api/v1/dependent-picklists");
        listRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Create endpoint blocked (404)
        var createRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists", new SaveDependentPicklistMapRequest(
            "leads", "country", "state", "Country-State", null, true, new Dictionary<string, IReadOnlyList<string>>()
        ));
        createRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Allowed endpoint blocked (404)
        var allowedRes = await s.Client.GetAsync("/api/v1/dependent-picklists/allowed?entityType=leads&parentField=country&childField=state&parentValue=usa");
        allowedRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Validate endpoint blocked (404)
        var validateRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists/validate", new ValidateDependentPicklistRequest(
            "leads", new Dictionary<string, string?>()
        ));
        validateRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dependent_picklist_crud_and_allowed_options_evaluation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "dependent-picklists");

        // 1. Create mapping
        var mappings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["usa"] = ["ca", "ny", "tx"],
            ["uae"] = ["dxb", "auh", "shj"]
        };

        var createRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists", new SaveDependentPicklistMapRequest(
            "leads", "country", "state", "Lead Country State", "Controls allowed states by country", true, mappings
        ));
        createRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<DependentPicklistMapDto>();
        created.ShouldNotBeNull();
        created.Name.ShouldBe("Lead Country State");
        created.Mappings["usa"].ShouldBe(["ca", "ny", "tx"]);

        // 2. List mappings
        var list = await s.Client.GetFromJsonAsync<List<DependentPicklistMapDto>>("/api/v1/dependent-picklists?entityType=leads");
        list.ShouldNotBeNull();
        list.Count.ShouldBe(1);
        list[0].Id.ShouldBe(created.Id);

        // 3. Get allowed values for "usa"
        var usaRes = await s.Client.GetFromJsonAsync<AllowedPicklistValuesDto>(
            "/api/v1/dependent-picklists/allowed?entityType=leads&parentField=country&childField=state&parentValue=usa");
        usaRes.ShouldNotBeNull();
        usaRes.ChildField.ShouldBe("state");
        usaRes.ParentValue.ShouldBe("usa");
        usaRes.AllowedValues.ShouldBe(["ca", "ny", "tx"]);
        usaRes.AllowedOptions.Count.ShouldBe(3);

        // 4. Get allowed values for "uae"
        var uaeRes = await s.Client.GetFromJsonAsync<AllowedPicklistValuesDto>(
            "/api/v1/dependent-picklists/allowed?entityType=leads&parentField=country&childField=state&parentValue=uae");
        uaeRes.ShouldNotBeNull();
        uaeRes.AllowedValues.ShouldBe(["dxb", "auh", "shj"]);

        // 5. Get allowed values when parentValue is empty
        var emptyRes = await s.Client.GetFromJsonAsync<AllowedPicklistValuesDto>(
            "/api/v1/dependent-picklists/allowed?entityType=leads&parentField=country&childField=state&parentValue=");
        emptyRes.ShouldNotBeNull();
        emptyRes.AllowedValues.ShouldBeEmpty();

        // 6. Update mapping
        var updatedMappings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["usa"] = ["ca", "ny", "tx", "fl"],
            ["canada"] = ["on", "qc", "bc"]
        };
        var updateRes = await s.Client.PutAsJsonAsync($"/api/v1/dependent-picklists/{created.Id}", new SaveDependentPicklistMapRequest(
            "leads", "country", "state", "Lead Country State Updated", null, true, updatedMappings
        ));
        updateRes.EnsureSuccessStatusCode();
        var updated = await updateRes.Content.ReadFromJsonAsync<DependentPicklistMapDto>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Lead Country State Updated");
        updated.Mappings["usa"].ShouldBe(["ca", "ny", "tx", "fl"]);

        // 7. Delete mapping
        var delRes = await s.Client.DeleteAsync($"/api/v1/dependent-picklists/{created.Id}");
        delRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Confirm deleted
        var listAfterDelete = await s.Client.GetFromJsonAsync<List<DependentPicklistMapDto>>("/api/v1/dependent-picklists?entityType=leads");
        listAfterDelete.ShouldNotBeNull();
        listAfterDelete.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dependent_picklist_payload_validation()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "dependent-picklists");

        // Create mapping: leads country -> state (usa -> [ca, ny])
        var mappings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["usa"] = ["ca", "ny"]
        };

        (await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists", new SaveDependentPicklistMapRequest(
            "leads", "country", "state", "Country State Validation", null, true, mappings
        ))).EnsureSuccessStatusCode();

        // 1. Valid pair: usa + ca
        var validRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists/validate", new ValidateDependentPicklistRequest(
            "leads", new Dictionary<string, string?>
            {
                ["country"] = "usa",
                ["state"] = "ca"
            }
        ));
        validRes.EnsureSuccessStatusCode();
        var validResult = await validRes.Content.ReadFromJsonAsync<DependentPicklistValidationResult>();
        validResult.ShouldNotBeNull();
        validResult.IsValid.ShouldBeTrue();
        validResult.Errors.ShouldBeEmpty();

        // 2. Invalid child: usa + dxb
        var invalidRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists/validate", new ValidateDependentPicklistRequest(
            "leads", new Dictionary<string, string?>
            {
                ["country"] = "usa",
                ["state"] = "dxb"
            }
        ));
        invalidRes.EnsureSuccessStatusCode();
        var invalidResult = await invalidRes.Content.ReadFromJsonAsync<DependentPicklistValidationResult>();
        invalidResult.ShouldNotBeNull();
        invalidResult.IsValid.ShouldBeFalse();
        invalidResult.Errors[0].ShouldContain("Value 'dxb' is not valid for 'state' when 'country' is 'usa'.");

        // 3. Child selected with missing parent
        var noParentRes = await s.Client.PostAsJsonAsync("/api/v1/dependent-picklists/validate", new ValidateDependentPicklistRequest(
            "leads", new Dictionary<string, string?>
            {
                ["country"] = null,
                ["state"] = "ca"
            }
        ));
        noParentRes.EnsureSuccessStatusCode();
        var noParentResult = await noParentRes.Content.ReadFromJsonAsync<DependentPicklistValidationResult>();
        noParentResult.ShouldNotBeNull();
        noParentResult.IsValid.ShouldBeFalse();
        noParentResult.Errors[0].ShouldContain("Field 'state' cannot be selected without choosing 'country' first");
    }

    [Fact]
    public async Task Dependent_picklist_multi_tenant_isolation()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantA, "dependent-picklists");

        var tenantB = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantB, "dependent-picklists");

        // Tenant A creates a rule
        var mappings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["technology"] = ["software", "hardware"]
        };
        var createRes = await tenantA.Client.PostAsJsonAsync("/api/v1/dependent-picklists", new SaveDependentPicklistMapRequest(
            "accounts", "industry", "sub_industry", "Tenant A Industry Rule", null, true, mappings
        ));
        createRes.EnsureSuccessStatusCode();
        var ruleA = await createRes.Content.ReadFromJsonAsync<DependentPicklistMapDto>();
        ruleA.ShouldNotBeNull();

        // Tenant B lists rules -> empty
        var listB = await tenantB.Client.GetFromJsonAsync<List<DependentPicklistMapDto>>("/api/v1/dependent-picklists?entityType=accounts");
        listB.ShouldNotBeNull();
        listB.Any(r => r.Id == ruleA.Id).ShouldBeFalse();

        // Tenant B tries to get Tenant A's rule -> 404
        var getB = await tenantB.Client.GetAsync($"/api/v1/dependent-picklists/{ruleA.Id}");
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to edit Tenant A's rule -> 404
        var putB = await tenantB.Client.PutAsJsonAsync($"/api/v1/dependent-picklists/{ruleA.Id}", new SaveDependentPicklistMapRequest(
            "accounts", "industry", "sub_industry", "Hacked Rule", null, true, mappings
        ));
        putB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to delete Tenant A's rule -> 404
        var delB = await tenantB.Client.DeleteAsync($"/api/v1/dependent-picklists/{ruleA.Id}");
        delB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
