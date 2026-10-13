using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MindLift.Domain.Common;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Persistence;

/// <summary>
/// Reflection guard (plan section 12): every tenant entity in the model must have the tenant query filter,
/// so a new entity can never accidentally skip isolation.
/// </summary>
public sealed class FilterCoverageTests
{
    [Fact]
    public async Task Every_tenant_entity_has_a_tenant_query_filter()
    {
        var db = await ProbeDatabase.CreateAsync();
        await using var context = db.CreateContext();

        var tenantEntities = context.Model.GetEntityTypes()
            .Where(t => typeof(TenantEntity).IsAssignableFrom(t.ClrType))
            .ToList();

        tenantEntities.ShouldNotBeEmpty();
        var withoutFilter = tenantEntities.Where(t => t.GetQueryFilter() is null).Select(t => t.ClrType.Name).ToList();
        withoutFilter.ShouldBeEmpty("these TenantEntity types are missing their query filter");
    }

    [Fact]
    public async Task Users_and_audit_logs_have_tenant_query_filters()
    {
        var db = await ProbeDatabase.CreateAsync();
        await using var context = db.CreateContext();

        foreach (var clrType in new[] { typeof(MindLift.Infrastructure.Identity.AppUser), typeof(MindLift.Domain.Entities.AuditLog) })
        {
            var entityType = context.Model.FindEntityType(clrType);
            entityType.ShouldNotBeNull();
            entityType!.GetQueryFilter().ShouldNotBeNull($"{clrType.Name} has no tenant filter");
        }
    }
}
