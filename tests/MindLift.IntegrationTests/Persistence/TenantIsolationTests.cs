using Microsoft.EntityFrameworkCore;
using MindLift.Application.Common.Exceptions;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Persistence;

/// <summary>Plan section 13: tenant isolation is mandatory and tested end to end against PostgreSQL.</summary>
public sealed class TenantIsolationTests
{
    private static readonly Guid TenantA = Guid.CreateVersion7();
    private static readonly Guid TenantB = Guid.CreateVersion7();

    [Fact]
    public async Task Rows_are_visible_only_to_their_own_tenant()
    {
        var db = await ProbeDatabase.CreateAsync();

        using (db.Tenant.Use(TenantA))
        {
            await using var write = db.CreateContext();
            write.Probes.Add(new ProbeEntity { Name = "belongs to A" });
            await write.SaveChangesAsync();
        }

        using (db.Tenant.Use(TenantB))
        {
            await using var read = db.CreateContext();
            (await read.Probes.CountAsync()).ShouldBe(0);
        }

        using (db.Tenant.Use(TenantA))
        {
            await using var read = db.CreateContext();
            (await read.Probes.CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Without_a_tenant_no_rows_are_returned()
    {
        var db = await ProbeDatabase.CreateAsync();
        using (db.Tenant.Use(TenantA))
        {
            await using var write = db.CreateContext();
            write.Probes.Add(new ProbeEntity { Name = "hidden" });
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext();
        (await read.Probes.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Audit_rows_are_tenant_filtered()
    {
        var db = await ProbeDatabase.CreateAsync();
        using (db.Tenant.Use(TenantA))
        {
            await using var write = db.CreateContext();
            write.Probes.Add(new ProbeEntity { Name = "audited" });
            await write.SaveChangesAsync();
        }

        using (db.Tenant.Use(TenantB))
        {
            await using var read = db.CreateContext();
            (await read.AuditLogs.CountAsync()).ShouldBe(0);
        }

        using (db.Tenant.Use(TenantA))
        {
            await using var read = db.CreateContext();
            (await read.AuditLogs.CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Inserting_a_row_for_another_tenant_is_forbidden()
    {
        var db = await ProbeDatabase.CreateAsync();
        using var _ = db.Tenant.Use(TenantA);

        await using var write = db.CreateContext();
        write.Probes.Add(new ProbeEntity { Name = "wrong tenant", TenantId = TenantB });

        await Should.ThrowAsync<ForbiddenException>(() => write.SaveChangesAsync());
    }

    [Fact]
    public async Task Changing_the_tenant_of_an_existing_row_is_forbidden()
    {
        var db = await ProbeDatabase.CreateAsync();
        Guid id;
        using (db.Tenant.Use(TenantA))
        {
            await using var write = db.CreateContext();
            var probe = new ProbeEntity { Name = "mine" };
            write.Probes.Add(probe);
            await write.SaveChangesAsync();
            id = probe.Id;
        }

        using (db.Tenant.Use(TenantA))
        {
            await using var write = db.CreateContext();
            var probe = await write.Probes.SingleAsync(p => p.Id == id);
            probe.TenantId = TenantB;

            await Should.ThrowAsync<ForbiddenException>(() => write.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Writes_without_a_tenant_are_forbidden()
    {
        var db = await ProbeDatabase.CreateAsync();

        await using var write = db.CreateContext();
        write.Probes.Add(new ProbeEntity { Name = "no tenant" });

        await Should.ThrowAsync<ForbiddenException>(() => write.SaveChangesAsync());
    }
}
