using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Accounts;

public sealed class AccountHierarchyTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveAccountRequest NewAccount(string name, Guid? parentId = null)
        => new(name, "https://example.com", "+1555123456", "Technology", "Enterprise", parentId, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, Guid? accountId)
        => new(first, last, email, "+1555987654", "Director", null, accountId, null, null, null);

    private static SaveDealRequest NewDeal(string name, decimal amount, Guid accountId, string status = "Open")
        => new(name, null, null, amount, null, 50, DateTime.UtcNow.AddMonths(1), accountId, null, "Test deal", null, null);

    [Fact]
    public async Task Feature_flag_gates_account_hierarchy_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var accResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Acme Corp"));
        accResp.EnsureSuccessStatusCode();
        var acc = await accResp.Content.ReadFromJsonAsync<AccountDto>();
        acc.ShouldNotBeNull();

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync($"/api/v1/accounts/{acc.Id}/hierarchy");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature is now accessible -> 200 OK
        var getOn = await admin.Client.GetAsync($"/api/v1/accounts/{acc.Id}/hierarchy");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Build_multi_level_tree_and_aggregate_rollups()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create 3 accounts: Global Corp -> EMEA Region -> UK Division
        var globalResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Global Corp"));
        var globalAcc = await globalResp.Content.ReadFromJsonAsync<AccountDto>();
        globalAcc.ShouldNotBeNull();

        var emeaResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("EMEA Region", globalAcc.Id));
        var emeaAcc = await emeaResp.Content.ReadFromJsonAsync<AccountDto>();
        emeaAcc.ShouldNotBeNull();

        var ukResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("UK Division", emeaAcc.Id));
        var ukAcc = await ukResp.Content.ReadFromJsonAsync<AccountDto>();
        ukAcc.ShouldNotBeNull();

        // 2. Attach contacts: 1 to Global, 2 to EMEA, 1 to UK (Total 4)
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Alice", "Smith", "alice@global.com", globalAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Bob", "Jones", "bob@emea.com", emeaAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Charlie", "Brown", "charlie@emea.com", emeaAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("David", "Clark", "david@uk.com", ukAcc.Id))).EnsureSuccessStatusCode();

        // 3. Attach deals:
        // Global: $200,000 Open
        // EMEA: $100,000 Open, $40,000 Open
        // UK: $50,000 Open
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Global Expansion", 200000m, globalAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("EMEA Rollout", 100000m, emeaAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("EMEA Pilot", 40000m, emeaAcc.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("UK Local Contract", 50000m, ukAcc.Id))).EnsureSuccessStatusCode();

        // 4. Query hierarchy from lowest child (UK Division)
        var treeResp = await admin.Client.GetAsync($"/api/v1/accounts/{ukAcc.Id}/hierarchy");
        treeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tree = await treeResp.Content.ReadFromJsonAsync<AccountHierarchyTreeDto>();

        tree.ShouldNotBeNull();
        tree.FocusAccountId.ShouldBe(ukAcc.Id);

        // Root must be Global Corp (Level 0)
        var root = tree.Root;
        root.Id.ShouldBe(globalAcc.Id);
        root.Level.ShouldBe(0);
        root.Name.ShouldBe("Global Corp");

        // Global Corp should have 1 child (EMEA Region, Level 1)
        root.Children.Count.ShouldBe(1);
        var emeaNode = root.Children[0];
        emeaNode.Id.ShouldBe(emeaAcc.Id);
        emeaNode.Level.ShouldBe(1);
        emeaNode.Name.ShouldBe("EMEA Region");

        // EMEA Region should have 1 child (UK Division, Level 2)
        emeaNode.Children.Count.ShouldBe(1);
        var ukNode = emeaNode.Children[0];
        ukNode.Id.ShouldBe(ukAcc.Id);
        ukNode.Level.ShouldBe(2);
        ukNode.Name.ShouldBe("UK Division");

        // Rollup verification across the 3 accounts:
        tree.TotalRollup.SubtreeAccountCount.ShouldBe(3);
        tree.TotalRollup.TotalContactsCount.ShouldBe(4);
        tree.TotalRollup.OpenDealsCount.ShouldBeGreaterThanOrEqualTo(3);
        tree.TotalRollup.OpenDealsValue.ShouldBeGreaterThanOrEqualTo(350000m);
    }

    [Fact]
    public async Task Subtree_rollups_for_intermediate_node()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var parentResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Holding Co"));
        var parent = await parentResp.Content.ReadFromJsonAsync<AccountDto>();
        parent.ShouldNotBeNull();

        var childResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Subsidiary A", parent.Id));
        var child = await childResp.Content.ReadFromJsonAsync<AccountDto>();
        child.ShouldNotBeNull();

        var grandChildResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Sub-branch A1", child.Id));
        var grandChild = await grandChildResp.Content.ReadFromJsonAsync<AccountDto>();
        grandChild.ShouldNotBeNull();

        // Attach 1 contact to Subsidiary A and 1 contact to Sub-branch A1
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Jane", "Doe", "jane@sub.com", child.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("John", "Doe", "john@branch.com", grandChild.Id))).EnsureSuccessStatusCode();

        // Attach deals
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Child Deal", 75000m, child.Id))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/deals", NewDeal("Grandchild Deal", 25000m, grandChild.Id))).EnsureSuccessStatusCode();

        // Request rollups for Subsidiary A only
        var rollupResp = await admin.Client.GetAsync($"/api/v1/accounts/{child.Id}/hierarchy/rollups");
        rollupResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rollups = await rollupResp.Content.ReadFromJsonAsync<AccountHierarchyMetricsDto>();

        rollups.ShouldNotBeNull();
        rollups.SubtreeAccountCount.ShouldBe(2); // Subsidiary A + Sub-branch A1
        rollups.DirectChildCount.ShouldBe(1);
        rollups.TotalContactsCount.ShouldBe(2);
        rollups.OpenDealsCount.ShouldBe(2);
        rollups.OpenDealsValue.ShouldBe(100000m);
    }

    [Fact]
    public async Task Circular_dependency_and_self_parent_prevention()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var nodeAResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Node A"));
        var nodeA = await nodeAResp.Content.ReadFromJsonAsync<AccountDto>();
        nodeA.ShouldNotBeNull();

        var nodeBResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Node B", nodeA.Id));
        var nodeB = await nodeBResp.Content.ReadFromJsonAsync<AccountDto>();
        nodeB.ShouldNotBeNull();

        var nodeCResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Node C", nodeB.Id));
        var nodeC = await nodeCResp.Content.ReadFromJsonAsync<AccountDto>();
        nodeC.ShouldNotBeNull();

        // 1. Cannot be own parent
        var selfResp = await admin.Client.PutAsJsonAsync($"/api/v1/accounts/{nodeA.Id}/hierarchy/parent", new SetAccountParentRequest(nodeA.Id));
        selfResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 2. Setting Node A's parent to Node C would cause A -> C -> B -> A cycle
        var cycleResp = await admin.Client.PutAsJsonAsync($"/api/v1/accounts/{nodeA.Id}/hierarchy/parent", new SetAccountParentRequest(nodeC.Id));
        cycleResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_hierarchy_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/account-hierarchy", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var accRespA = await tenantA.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Tenant A Secret Parent"));
        var accA = await accRespA.Content.ReadFromJsonAsync<AccountDto>();
        accA.ShouldNotBeNull();

        var accRespB = await tenantB.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Tenant B Account"));
        var accB = await accRespB.Content.ReadFromJsonAsync<AccountDto>();
        accB.ShouldNotBeNull();

        // Tenant B attempts to read hierarchy of Tenant A's account -> 404
        var crossGet = await tenantB.Client.GetAsync($"/api/v1/accounts/{accA.Id}/hierarchy");
        crossGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to read rollups of Tenant A's account -> 404
        var crossRollup = await tenantB.Client.GetAsync($"/api/v1/accounts/{accA.Id}/hierarchy/rollups");
        crossRollup.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to set Tenant A's account as its parent -> 404
        var crossSetParent = await tenantB.Client.PutAsJsonAsync($"/api/v1/accounts/{accB.Id}/hierarchy/parent", new SetAccountParentRequest(accA.Id));
        crossSetParent.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to set parent on Tenant A's account -> 404
        var crossModify = await tenantB.Client.PutAsJsonAsync($"/api/v1/accounts/{accA.Id}/hierarchy/parent", new SetAccountParentRequest(accB.Id));
        crossModify.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
