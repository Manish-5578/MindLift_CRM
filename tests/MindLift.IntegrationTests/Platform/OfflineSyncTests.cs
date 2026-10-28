using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Platform;

public sealed class OfflineSyncTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Feature_flag_gates_offline_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var getOff = await admin.Client.GetAsync("/api/v1/offline/bootstrap");
        getOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var getOn = await admin.Client.GetAsync("/api/v1/offline/bootstrap");
        getOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pwa_manifest_returns_valid_app_configuration()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var manifest = await admin.Client.GetFromJsonAsync<PwaManifestDto>("/api/v1/offline/manifest");
        manifest.ShouldNotBeNull();
        manifest.Name.ShouldBe("MindLift CRM");
        manifest.StartUrl.ShouldBe("/");
        manifest.Display.ShouldBe("standalone");
        manifest.Icons.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Offline_bootstrap_and_delta_sync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Query Bootstrap
        var bootstrap = await admin.Client.GetFromJsonAsync<OfflineBootstrapDto>("/api/v1/offline/bootstrap");
        bootstrap.ShouldNotBeNull();
        bootstrap.Pipelines.ShouldNotBeEmpty();
        bootstrap.SnapshotUtc.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);

        // Query Delta
        var since = DateTime.UtcNow.AddHours(-1);
        var delta = await admin.Client.GetFromJsonAsync<OfflineDeltaDto>($"/api/v1/offline/delta?sinceUtc={Uri.EscapeDataString(since.ToString("O"))}");
        delta.ShouldNotBeNull();
        delta.ServerTimestampUtc.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public async Task Offline_batch_sync_processes_creations_with_id_remapping_and_detects_conflicts()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var leadDoc = JsonDocument.Parse("{\"firstName\": \"Sam\", \"lastName\": \"Altman\", \"email\": \"sam@openai.test\", \"company\": \"OpenAI\"}");
        var dealDoc = JsonDocument.Parse("{\"name\": \"Enterprise AI Deal\", \"amount\": 150000}");
        var actDoc = JsonDocument.Parse("{\"subject\": \"Onsite meeting note taken while offline\"}");

        var batchRequest = new OfflineSyncBatchRequest(
            BatchId: "batch_001",
            Mutations:
            [
                new OfflineMutationRequest(
                    ClientMutationId: "m_1",
                    EntityType: "lead",
                    Operation: "create",
                    EntityId: null,
                    ClientTempId: "temp_lead_1",
                    Payload: leadDoc,
                    BaseVersion: null),
                new OfflineMutationRequest(
                    ClientMutationId: "m_2",
                    EntityType: "deal",
                    Operation: "create",
                    EntityId: null,
                    ClientTempId: "temp_deal_1",
                    Payload: dealDoc,
                    BaseVersion: null),
                new OfflineMutationRequest(
                    ClientMutationId: "m_3",
                    EntityType: "activity",
                    Operation: "create",
                    EntityId: null,
                    ClientTempId: "temp_act_1",
                    Payload: actDoc,
                    BaseVersion: null)
            ]);

        // Submit Batch Sync
        var syncResp = await admin.Client.PostAsJsonAsync("/api/v1/offline/sync", batchRequest);
        syncResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var batchResult = await syncResp.Content.ReadFromJsonAsync<OfflineSyncBatchResultDto>();
        batchResult.ShouldNotBeNull();
        batchResult.BatchId.ShouldBe("batch_001");
        batchResult.ProcessedCount.ShouldBe(3);
        batchResult.AppliedCount.ShouldBe(3);
        batchResult.ConflictCount.ShouldBe(0);
        batchResult.ErrorCount.ShouldBe(0);

        // Verify ID Remapping
        batchResult.IdMap.ShouldContainKey("temp_lead_1");
        batchResult.IdMap.ShouldContainKey("temp_deal_1");
        batchResult.IdMap.ShouldContainKey("temp_act_1");

        var createdDealId = batchResult.IdMap["temp_deal_1"];
        createdDealId.ShouldNotBe(Guid.Empty);

        // Test Optimistic Concurrency Conflict
        var conflictDoc = JsonDocument.Parse("{\"amount\": 200000}");
        var conflictBatch = new OfflineSyncBatchRequest(
            BatchId: "batch_002",
            Mutations:
            [
                new OfflineMutationRequest(
                    ClientMutationId: "m_conflict",
                    EntityType: "deal",
                    Operation: "update",
                    EntityId: createdDealId,
                    ClientTempId: null,
                    Payload: conflictDoc,
                    BaseVersion: 999) // Stale version triggers conflict
            ]);

        var conflictResp = await admin.Client.PostAsJsonAsync("/api/v1/offline/sync", conflictBatch);
        conflictResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var conflictResult = await conflictResp.Content.ReadFromJsonAsync<OfflineSyncBatchResultDto>();

        conflictResult.ShouldNotBeNull();
        conflictResult.ConflictCount.ShouldBe(1);
        conflictResult.Results.First().Status.ShouldBe("conflict");
        conflictResult.Results.First().ErrorMessage.ShouldContain("Conflict");
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_offline_data_leakage()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        (await tenantB.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/pwa-offline", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // Tenant A creates a lead offline
        var leadDoc = JsonDocument.Parse("{\"firstName\": \"TenantA\", \"lastName\": \"Secret\", \"email\": \"secret@tenanta.test\"}");
        var batch = new OfflineSyncBatchRequest("batch_ta", [
            new OfflineMutationRequest("m1", "lead", "create", null, "t_lead", leadDoc, null)
        ]);
        (await tenantA.Client.PostAsJsonAsync("/api/v1/offline/sync", batch)).EnsureSuccessStatusCode();

        // Tenant B bootstrap must NOT contain Tenant A's lead
        var bootstrapB = await tenantB.Client.GetFromJsonAsync<OfflineBootstrapDto>("/api/v1/offline/bootstrap");
        bootstrapB.ShouldNotBeNull();
        bootstrapB.Records.Any(r => r.Subtitle == "secret@tenanta.test").ShouldBeFalse();
    }
}
