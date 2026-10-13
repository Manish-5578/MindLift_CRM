using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Identity;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Security;

/// <summary>
/// Computes which owners the current user may see (plan 4.4.5):
/// Own = me; Team = me, my team's members and everyone who reports to me (directly or indirectly); All = no filter.
/// </summary>
public sealed class DataScopeService(IAppDbContext db, ITenantContext tenant, IPermissionService permissions) : IDataScopeService
{
    private const int MaxReportingDepth = 10;

    public async Task<IReadOnlyList<Guid>?> GetScopeUserIdsAsync(AppModule module, CancellationToken cancellationToken = default)
    {
        var permission = await permissions.GetModulePermissionAsync(module, cancellationToken);
        var scope = permission?.Scope ?? DataScope.Own;

        if (scope == DataScope.All)
        {
            return null;
        }

        if (tenant.UserId is not { } me)
        {
            return [];
        }

        if (scope == DataScope.Own)
        {
            return [me];
        }

        return await TeamScopeAsync(me, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> TeamScopeAsync(Guid me, CancellationToken cancellationToken)
    {
        var users = db.Set<AppUser>().AsNoTracking();
        var myTeam = await users.Where(u => u.Id == me).Select(u => u.TeamId).FirstOrDefaultAsync(cancellationToken);

        var ids = new HashSet<Guid> { me };

        if (myTeam is { } teamId)
        {
            var members = await users.Where(u => u.TeamId == teamId).Select(u => u.Id).ToListAsync(cancellationToken);
            ids.UnionWith(members);
        }

        var frontier = ids.ToList();
        for (var depth = 0; depth < MaxReportingDepth && frontier.Count > 0; depth++)
        {
            var reports = await users
                .Where(u => u.ManagerId != null && frontier.Contains(u.ManagerId.Value))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            frontier = reports.Where(ids.Add).ToList();
        }

        return ids.ToList();
    }
}
