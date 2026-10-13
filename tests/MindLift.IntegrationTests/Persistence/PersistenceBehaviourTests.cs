using Microsoft.EntityFrameworkCore;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Persistence;

/// <summary>Interceptor behaviour: timestamps, versions, soft delete, audit rows and optimistic concurrency.</summary>
public sealed class PersistenceBehaviourTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Fact]
    public async Task Insert_stamps_tenant_timestamps_and_version()
    {
        var db = await ProbeDatabase.CreateAsync();
        using var _ = db.Tenant.Use(Tenant, Guid.CreateVersion7());

        await using var write = db.CreateContext();
        var probe = new ProbeEntity { Name = "first" };
        write.Probes.Add(probe);
        await write.SaveChangesAsync();

        probe.TenantId.ShouldBe(Tenant);
        probe.CreatedAt.ShouldBe(db.Clock.UtcNow);
        probe.CreatedById.ShouldNotBeNull();
        probe.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Update_increments_version_and_writes_an_audit_row_with_the_changed_field()
    {
        var db = await ProbeDatabase.CreateAsync();
        using var _ = db.Tenant.Use(Tenant);
        var id = await InsertAsync(db, "before");

        db.Clock.UtcNow = db.Clock.UtcNow.AddMinutes(1);
        await using (var update = db.CreateContext())
        {
            var probe = await update.Probes.SingleAsync(p => p.Id == id);
            probe.Name = "after";
            await update.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        var stored = await read.Probes.SingleAsync(p => p.Id == id);
        stored.Version.ShouldBe(2);
        stored.Name.ShouldBe("after");

        var audits = await read.AuditLogs.Where(a => a.EntityId == id).OrderBy(a => a.OccurredAt).ToListAsync();
        audits.Select(a => a.Action).ShouldBe(["Create", "Update"]);
        // jsonb stores the change list normalised (spaces after colons, keys reordered), so match the stored form.
        audits[1].Changes!.ShouldContain("\"Field\": \"Name\"");
        audits[1].Changes!.ShouldContain("\"Old\": \"before\"");
        audits[1].Changes!.ShouldContain("\"New\": \"after\"");
    }

    [Fact]
    public async Task Delete_is_a_soft_delete_hidden_by_the_query_filter_and_audited()
    {
        var db = await ProbeDatabase.CreateAsync();
        using var _ = db.Tenant.Use(Tenant);
        var id = await InsertAsync(db, "to delete");

        await using (var delete = db.CreateContext())
        {
            var probe = await delete.Probes.SingleAsync(p => p.Id == id);
            delete.Probes.Remove(probe);
            await delete.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        (await read.Probes.CountAsync(p => p.Id == id)).ShouldBe(0);

        var raw = await read.Probes.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        raw.IsDeleted.ShouldBeTrue();
        raw.DeletedAt.ShouldNotBeNull();

        (await read.AuditLogs.CountAsync(a => a.EntityId == id && a.Action == "Delete")).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_updates_are_rejected_by_the_version_token()
    {
        var db = await ProbeDatabase.CreateAsync();
        using var _ = db.Tenant.Use(Tenant);
        var id = await InsertAsync(db, "shared");

        await using var first = db.CreateContext();
        await using var second = db.CreateContext();
        var firstCopy = await first.Probes.SingleAsync(p => p.Id == id);
        var secondCopy = await second.Probes.SingleAsync(p => p.Id == id);

        firstCopy.Name = "first wins";
        await first.SaveChangesAsync();

        secondCopy.Name = "second loses";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static async Task<Guid> InsertAsync(ProbeDatabase db, string name)
    {
        await using var write = db.CreateContext();
        var probe = new ProbeEntity { Name = name };
        write.Probes.Add(probe);
        await write.SaveChangesAsync();
        return probe.Id;
    }
}
