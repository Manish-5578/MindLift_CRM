using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Settings;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Tests schedule configuration, on-demand archive generation, SHA256 integrity checksums,
/// archive downloads, retention/deletion, and multi-tenant isolation.
/// </summary>
public sealed class BackupTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Feature_flag_off_blocks_backup_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);

        // Schedule GET blocked (404)
        var getSchedRes = await s.Client.GetAsync("/api/v1/backups/schedule");
        getSchedRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Schedule PUT blocked (404)
        var putSchedRes = await s.Client.PutAsJsonAsync("/api/v1/backups/schedule", new SaveBackupScheduleConfigRequest(
            true, "Weekly"
        ));
        putSchedRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Archives GET blocked (404)
        var getArchRes = await s.Client.GetAsync("/api/v1/backups/archives");
        getArchRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Trigger POST blocked (404)
        var triggerRes = await s.Client.PostAsJsonAsync("/api/v1/backups/trigger", new TriggerBackupRequest());
        triggerRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Download GET blocked (404)
        var dlRes = await s.Client.GetAsync($"/api/v1/backups/archives/{Guid.NewGuid()}/download");
        dlRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Backup_schedule_config_crud()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "scheduled-backups");

        // 1. Get default schedule config
        var defSched = await s.Client.GetFromJsonAsync<BackupScheduleConfigDto>("/api/v1/backups/schedule");
        defSched.ShouldNotBeNull();
        defSched.IsEnabled.ShouldBeTrue();
        defSched.Frequency.ShouldBe("Weekly");
        defSched.HourUtc.ShouldBe(2);
        defSched.RetentionDays.ShouldBe(30);

        // 2. Save updated schedule config
        var saveReq = new SaveBackupScheduleConfigRequest(
            IsEnabled: true,
            Frequency: "Daily",
            HourUtc: 3,
            RetentionDays: 60,
            Format: "json",
            NotifyEmails: ["admin@mindlift.test"]
        );
        var putRes = await s.Client.PutAsJsonAsync("/api/v1/backups/schedule", saveReq);
        putRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putRes.Content.ReadFromJsonAsync<BackupScheduleConfigDto>();
        updated.ShouldNotBeNull();
        updated.Frequency.ShouldBe("Daily");
        updated.HourUtc.ShouldBe(3);
        updated.RetentionDays.ShouldBe(60);
        updated.NotifyEmails.ShouldContain("admin@mindlift.test");

        // 3. Re-read schedule config
        var reRead = await s.Client.GetFromJsonAsync<BackupScheduleConfigDto>("/api/v1/backups/schedule");
        reRead.ShouldNotBeNull();
        reRead.Frequency.ShouldBe("Daily");
        reRead.RetentionDays.ShouldBe(60);
    }

    [Fact]
    public async Task Trigger_backup_generates_archive_and_allows_download()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "scheduled-backups");

        // Seed some data: create a lead
        var leadRes = await s.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest(
            FirstName: "Backup",
            LastName: "Tester",
            Email: "backup.tester@example.com",
            Phone: null,
            CompanyName: "Acme Backup Corp",
            Status: "new",
            Source: null,
            Rating: null,
            Description: null,
            Tags: null,
            OwnerId: null,
            CustomFields: null
        ));
        leadRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 1. Trigger backup on demand
        var triggerRes = await s.Client.PostAsJsonAsync("/api/v1/backups/trigger", new TriggerBackupRequest("json"));
        triggerRes.StatusCode.ShouldBe(HttpStatusCode.Created);
        var archive = await triggerRes.Content.ReadFromJsonAsync<BackupArchiveDto>();
        archive.ShouldNotBeNull();
        archive.Status.ShouldBe("Completed");
        archive.Format.ShouldBe("json");
        archive.SizeBytes.ShouldBeGreaterThan(0);
        archive.ChecksumSha256.Length.ShouldBe(64);
        archive.RecordCounts["leads"].ShouldBeGreaterThanOrEqualTo(1);
        archive.ExpiresAt.ShouldBeGreaterThan(DateTime.UtcNow);

        // 2. List archives
        var list = await s.Client.GetFromJsonAsync<List<BackupArchiveDto>>("/api/v1/backups/archives");
        list.ShouldNotBeNull();
        list.Count.ShouldBeGreaterThanOrEqualTo(1);
        list[0].Id.ShouldBe(archive.Id);

        // 3. Get archive by ID
        var getArch = await s.Client.GetFromJsonAsync<BackupArchiveDto>($"/api/v1/backups/archives/{archive.Id}");
        getArch.ShouldNotBeNull();
        getArch.Id.ShouldBe(archive.Id);

        // 4. Download archive
        var dlRes = await s.Client.GetAsync($"/api/v1/backups/archives/{archive.Id}/download");
        dlRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        dlRes.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var downloadedBytes = await dlRes.Content.ReadAsByteArrayAsync();
        downloadedBytes.Length.ShouldBe((int)archive.SizeBytes);

        // Verify SHA256 checksum matches
        var actualChecksum = Convert.ToHexStringLower(SHA256.HashData(downloadedBytes));
        actualChecksum.ShouldBe(archive.ChecksumSha256);

        // Verify content contains the seeded lead
        var downloadedText = System.Text.Encoding.UTF8.GetString(downloadedBytes);
        downloadedText.ShouldContain("backup.tester@example.com");
        downloadedText.ShouldContain("Acme Backup Corp");

        // 5. Verify download count incremented
        var archAfterDl = await s.Client.GetFromJsonAsync<BackupArchiveDto>($"/api/v1/backups/archives/{archive.Id}");
        archAfterDl.ShouldNotBeNull();
        archAfterDl.DownloadCount.ShouldBe(1);

        // 6. Delete archive
        var delRes = await s.Client.DeleteAsync($"/api/v1/backups/archives/{archive.Id}");
        delRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var getAfterDel = await s.Client.GetAsync($"/api/v1/backups/archives/{archive.Id}");
        getAfterDel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_backups()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA, "scheduled-backups");
        await EnableFeatureAsync(tenantB, "scheduled-backups");

        // Tenant A triggers a backup
        var triggerA = await tenantA.Client.PostAsJsonAsync("/api/v1/backups/trigger", new TriggerBackupRequest("json"));
        triggerA.StatusCode.ShouldBe(HttpStatusCode.Created);
        var archiveA = await triggerA.Content.ReadFromJsonAsync<BackupArchiveDto>();
        archiveA.ShouldNotBeNull();

        // Tenant B lists archives -> empty
        var listB = await tenantB.Client.GetFromJsonAsync<List<BackupArchiveDto>>("/api/v1/backups/archives");
        listB.ShouldNotBeNull();
        listB.ShouldBeEmpty();

        // Tenant B tries to get Tenant A's archive -> 404
        var getB = await tenantB.Client.GetAsync($"/api/v1/backups/archives/{archiveA.Id}");
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to download Tenant A's archive -> 404
        var dlB = await tenantB.Client.GetAsync($"/api/v1/backups/archives/{archiveA.Id}/download");
        dlB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B tries to delete Tenant A's archive -> 404
        var delB = await tenantB.Client.DeleteAsync($"/api/v1/backups/archives/{archiveA.Id}");
        delB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
