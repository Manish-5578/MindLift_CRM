using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Dashboard;

public sealed class DashboardService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    IEnumerable<IDashboardWidget> widgets,
    ILogger<DashboardService> logger,
    IPermissionService permissions,
    IDataScopeService scope
) : IDashboardService
{
    /// <summary>
    /// A widget is shown only to people who can view its module, and its numbers are limited to the intersection of the
    /// dashboard selection and the person's data scope for that module.
    /// </summary>
    private async Task<DashboardContext?> WidgetContextAsync(IDashboardWidget widget, DashboardContext context, CancellationToken ct)
    {
        if (!await permissions.HasAsync(widget.Module, PermissionAction.View, ct))
        {
            return null;
        }

        var moduleScope = await scope.GetScopeUserIdsAsync(widget.Module, ct);
        if (moduleScope is null)
        {
            return context;
        }

        var narrowed = context.ScopeUserIds is null ? moduleScope.ToList() : context.ScopeUserIds.Intersect(moduleScope).ToList();
        return context with { ScopeUserIds = narrowed };
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        DashboardView? requestedView,
        string? periodPreset,
        DateTime? fromUtc,
        DateTime? toUtc,
        Guid? teamId,
        Guid? userId,
        Guid? pipelineId,
        bool refresh,
        CancellationToken ct = default
    )
    {
        var currentUserId = tenant.UserId ?? throw new ForbiddenException("Sign in to access dashboard.", "no_user");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new NotFoundException("User", currentUserId);

        var (allowedMaxView, availableViews) = await ResolveAvailableViewsAsync(user.RoleId, ct);
        var activeView = ResolveActiveView(requestedView, allowedMaxView);

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new TenantSettings { BaseCurrency = "USD", TimeZoneId = "UTC" };

        var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(settings.TimeZoneId) ? "UTC" : settings.TimeZoneId);

        var (period, previousPeriod) = ResolvePeriods(periodPreset, fromUtc, toUtc, clock.UtcNow, tz);
        var scopeUserIds = await ResolveScopeUserIdsAsync(activeView, currentUserId, teamId, userId, ct);

        var context = new DashboardContext(
            currentUserId,
            tenant.TenantId ?? Guid.Empty,
            activeView,
            scopeUserIds,
            period,
            previousPeriod,
            pipelineId,
            settings.BaseCurrency,
            tz
        );

        var layout = await GetEffectiveLayoutAsync(currentUserId, activeView, ct);
        var widgetMap = widgets.ToDictionary(w => w.Key);

        var computedWidgets = new List<DashboardWidgetDto>();
        foreach (var item in layout.Widgets.OrderBy(w => w.Order))
        {
            if (!widgetMap.TryGetValue(item.Key, out var widget))
            {
                continue;
            }

            if (!widget.Views.Contains(activeView))
            {
                continue;
            }

            if (item.IsHidden)
            {
                computedWidgets.Add(new DashboardWidgetDto(
                    item.Key,
                    widget.Key,
                    item.Size,
                    item.Order,
                    true,
                    null,
                    null,
                    null
                ));
                continue;
            }

            try
            {
                var widgetContext = await WidgetContextAsync(widget, context, ct);
                if (widgetContext is null)
                {
                    continue;
                }

                var result = await widget.ComputeAsync(widgetContext, ct);
                var json = result.Data is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(result.Data);

                computedWidgets.Add(new DashboardWidgetDto(
                    widget.Key,
                    result.Title,
                    item.Size,
                    item.Order,
                    false,
                    json,
                    result.DrillDownUrl,
                    result.Error
                ));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to compute dashboard widget {Key}", widget.Key);
                computedWidgets.Add(new DashboardWidgetDto(
                    widget.Key,
                    widget.Key,
                    item.Size,
                    item.Order,
                    false,
                    null,
                    null,
                    "widget_failed"
                ));
            }
        }

        return new DashboardResponse(activeView, availableViews, clock.UtcNow, computedWidgets);
    }

    public async Task<DashboardWidgetDto> GetWidgetAsync(
        string key,
        DashboardView? requestedView,
        string? periodPreset,
        DateTime? fromUtc,
        DateTime? toUtc,
        Guid? teamId,
        Guid? userId,
        Guid? pipelineId,
        bool refresh,
        CancellationToken ct = default
    )
    {
        var widget = widgets.FirstOrDefault(w => w.Key == key)
            ?? throw new NotFoundException("Dashboard widget", key);

        var currentUserId = tenant.UserId ?? throw new ForbiddenException("Sign in to access dashboard.", "no_user");
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUserId, ct)
            ?? throw new NotFoundException("User", currentUserId);

        var (allowedMaxView, _) = await ResolveAvailableViewsAsync(user.RoleId, ct);
        var activeView = ResolveActiveView(requestedView, allowedMaxView);

        if (!widget.Views.Contains(activeView))
        {
            throw new NotFoundException("Dashboard widget for view", key);
        }

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            ?? new TenantSettings { BaseCurrency = "USD", TimeZoneId = "UTC" };
        var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(settings.TimeZoneId) ? "UTC" : settings.TimeZoneId);
        var (period, previousPeriod) = ResolvePeriods(periodPreset, fromUtc, toUtc, clock.UtcNow, tz);
        var scopeUserIds = await ResolveScopeUserIdsAsync(activeView, currentUserId, teamId, userId, ct);

        var context = new DashboardContext(
            currentUserId,
            tenant.TenantId ?? Guid.Empty,
            activeView,
            scopeUserIds,
            period,
            previousPeriod,
            pipelineId,
            settings.BaseCurrency,
            tz
        );

        var widgetContext = await WidgetContextAsync(widget, context, ct) ?? throw new ForbiddenException();
        var result = await widget.ComputeAsync(widgetContext, ct);
        var json = result.Data is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(result.Data);

        return new DashboardWidgetDto(
            widget.Key,
            result.Title,
            widget.DefaultSize,
            widget.DefaultOrder,
            false,
            json,
            result.DrillDownUrl,
            result.Error
        );
    }

    public async Task<DashboardLayoutDto> GetLayoutAsync(DashboardView view, CancellationToken ct = default)
    {
        var currentUserId = tenant.UserId ?? throw new ForbiddenException("Sign in required.", "no_user");
        return await GetEffectiveLayoutAsync(currentUserId, view, ct);
    }

    public async Task<DashboardLayoutDto> SaveLayoutAsync(SaveDashboardLayoutRequest request, CancellationToken ct = default)
    {
        var currentUserId = tenant.UserId ?? throw new ForbiddenException("Sign in required.", "no_user");
        var existing = await db.DashboardLayouts.FirstOrDefaultAsync(l => l.UserId == currentUserId && l.View == request.View, ct);

        var doc = JsonSerializer.SerializeToDocument(request.Widgets);
        if (existing is null)
        {
            existing = new DashboardLayout
            {
                UserId = currentUserId,
                View = request.View,
                Layout = doc
            };
            db.DashboardLayouts.Add(existing);
        }
        else
        {
            existing.Layout = doc;
        }

        await db.SaveChangesAsync(ct);
        return new DashboardLayoutDto(request.View, request.Widgets);
    }

    public async Task<DashboardLayoutDto> ResetLayoutAsync(DashboardView view, CancellationToken ct = default)
    {
        var currentUserId = tenant.UserId ?? throw new ForbiddenException("Sign in required.", "no_user");
        var existing = await db.DashboardLayouts.FirstOrDefaultAsync(l => l.UserId == currentUserId && l.View == view, ct);
        if (existing is not null)
        {
            db.DashboardLayouts.Remove(existing);
            await db.SaveChangesAsync(ct);
        }

        return GetDefaultLayout(view);
    }

    private async Task<(DashboardView MaxView, IReadOnlyList<DashboardView> Available)> ResolveAvailableViewsAsync(Guid? roleId, CancellationToken ct)
    {
        if (roleId is null)
        {
            return (DashboardView.Staff, [DashboardView.Staff]);
        }

        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId.Value, ct);
        var maxView = role?.DashboardView ?? DashboardView.Staff;

        var available = new List<DashboardView>();
        if (maxView >= DashboardView.Staff) available.Add(DashboardView.Staff);
        if (maxView >= DashboardView.Manager) available.Add(DashboardView.Manager);
        if (maxView >= DashboardView.Admin) available.Add(DashboardView.Admin);

        return (maxView, available);
    }

    private static DashboardView ResolveActiveView(DashboardView? requested, DashboardView maxView)
    {
        if (requested is null) return maxView;
        return requested.Value <= maxView ? requested.Value : maxView;
    }

    private async Task<IReadOnlyList<Guid>?> ResolveScopeUserIdsAsync(DashboardView view, Guid currentUserId, Guid? teamId, Guid? userId, CancellationToken ct)
    {
        if (view == DashboardView.Staff)
        {
            return [currentUserId];
        }

        // A manager sees their Team (themselves, their team and everyone reporting to them); the Admin view is company-wide.
        IReadOnlyList<Guid>? allowed = view == DashboardView.Manager ? await scope.TeamOfAsync(currentUserId, ct) : null;

        if (userId is not null)
        {
            if (allowed is not null && !allowed.Contains(userId.Value))
            {
                throw new ForbiddenException("That person is outside your team.", "out_of_scope");
            }

            return [userId.Value];
        }

        if (teamId is not null)
        {
            var members = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.TeamId == teamId)
                .Select(u => u.Id)
                .ToListAsync(ct);
            return allowed is null ? members : members.Intersect(allowed).ToList();
        }

        return allowed;
    }

    private static (DateRange Period, DateRange PreviousPeriod) ResolvePeriods(string? preset, DateTime? fromUtc, DateTime? toUtc, DateTime nowUtc, TimeZoneInfo zone)
    {
        if (fromUtc is not null && toUtc is not null)
        {
            var p = new DateRange(fromUtc.Value, toUtc.Value);
            var span = toUtc.Value - fromUtc.Value;
            var pp = new DateRange(fromUtc.Value - span, fromUtc.Value);
            return (p, pp);
        }

        var datePreset = preset?.ToLowerInvariant() switch
        {
            "last_month" => DatePreset.LastMonth,
            "this_quarter" => DatePreset.ThisQuarter,
            "this_year" => DatePreset.ThisYear,
            _ => DatePreset.ThisMonth
        };

        var current = DateRangeResolver.Resolve(datePreset, nowUtc, zone);

        // Previous equivalent range
        var prevPreset = datePreset switch
        {
            DatePreset.ThisMonth => DatePreset.LastMonth,
            DatePreset.LastMonth => DatePreset.LastMonth,
            DatePreset.ThisQuarter => DatePreset.LastQuarter,
            DatePreset.ThisYear => DatePreset.LastYear,
            _ => DatePreset.LastMonth
        };

        var prev = DateRangeResolver.Resolve(prevPreset, nowUtc, zone);
        return (current, prev);
    }

    private async Task<DashboardLayoutDto> GetEffectiveLayoutAsync(Guid userId, DashboardView view, CancellationToken ct)
    {
        var saved = await db.DashboardLayouts.AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == userId && l.View == view, ct);

        if (saved is not null)
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<DashboardWidgetLayoutItem>>(saved.Layout.RootElement.GetRawText());
                if (items is not null && items.Count > 0)
                {
                    return new DashboardLayoutDto(view, items);
                }
            }
            catch
            {
                // Fall back to default
            }
        }

        return GetDefaultLayout(view);
    }

    private static DashboardLayoutDto GetDefaultLayout(DashboardView view) => view switch
    {
        DashboardView.Staff => new DashboardLayoutDto(view,
        [
            new("kpi_strip", 10, WidgetSize.Xl, false),
            new("my_day", 20, WidgetSize.Lg, false),
            new("target_progress", 30, WidgetSize.Sm, false),
            new("new_leads", 40, WidgetSize.Md, false),
            new("upcoming_meetings", 50, WidgetSize.Md, false),
            new("deals_in_progress", 60, WidgetSize.Md, false),
            new("deals_closing_soon", 70, WidgetSize.Md, false),
            new("activity_summary", 80, WidgetSize.Md, false)
        ]),
        DashboardView.Manager => new DashboardLayoutDto(view,
        [
            new("kpi_strip", 10, WidgetSize.Xl, false),
            new("team_leaderboard", 20, WidgetSize.Lg, false),
            new("target_progress", 30, WidgetSize.Sm, false),
            new("sales_trend", 40, WidgetSize.Md, false),
            new("deals_in_progress", 50, WidgetSize.Md, false),
            new("leads_by_source", 60, WidgetSize.Sm, false),
            new("pipeline_funnel", 70, WidgetSize.Sm, false),
            new("team_overdue", 80, WidgetSize.Sm, false),
            new("deals_closing_soon", 90, WidgetSize.Md, false),
            new("recent_wins", 100, WidgetSize.Md, false),
            new("my_day", 110, WidgetSize.Md, false),
            new("upcoming_meetings", 120, WidgetSize.Md, false)
        ]),
        DashboardView.Admin => new DashboardLayoutDto(view,
        [
            new("kpi_strip", 10, WidgetSize.Xl, false),
            new("sales_trend", 20, WidgetSize.Lg, false),
            new("target_progress", 25, WidgetSize.Sm, false),
            new("system_overview", 30, WidgetSize.Sm, false),
            new("team_leaderboard", 40, WidgetSize.Md, false),
            new("leads_by_source", 50, WidgetSize.Md, false),
            new("deals_in_progress", 60, WidgetSize.Md, false),
            new("pipeline_funnel", 70, WidgetSize.Md, false),
            new("team_overdue", 80, WidgetSize.Sm, false),
            new("recent_wins", 90, WidgetSize.Md, false)
        ]),
        _ => new DashboardLayoutDto(view, [new("kpi_strip", 10, WidgetSize.Xl, false)])
    };
}

public sealed class SalesTargetService(
    AppDbContext db,
    ITenantContext tenant,
    IDataScopeService scope,
    IPermissionService permissions) : ISalesTargetService
{
    // Access model: admins manage every target. Other callers resolve their
    // configured Reports data scope (Own = self only, Team = team members & reports, All = everyone).
    private sealed record Access(bool IsAdmin, Guid? CallerId, Guid? CallerTeamId, IReadOnlyList<Guid>? AllowedUserIds, DataScope Scope);

    private async Task<Access> AccessAsync(CancellationToken ct)
    {
        if (tenant.IsPlatformAdmin) return new Access(true, tenant.UserId, null, null, DataScope.All);
        if (tenant.UserId is not { } callerId) throw new ForbiddenException();
        var caller = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, ct)
            ?? throw new ForbiddenException();
        var isAdmin = caller.IsTenantOwner
            || (caller.RoleId is { } rid && await db.Roles.AsNoTracking().AnyAsync(r => r.Id == rid && r.IsAdmin, ct));
        if (isAdmin)
        {
            return new Access(true, callerId, caller.TeamId, null, DataScope.All);
        }

        var perm = await permissions.GetModulePermissionAsync(AppModule.Reports, ct);
        if (perm is null || !perm.CanView)
        {
            throw new ForbiddenException("You do not have permission to view sales targets.", "permission_denied");
        }

        var allowedUserIds = await scope.GetScopeUserIdsAsync(AppModule.Reports, ct);
        return new Access(false, callerId, caller.TeamId, allowedUserIds, perm.Scope);
    }

    private static bool CanSee(Access a, SalesTarget t)
    {
        if (a.IsAdmin || a.AllowedUserIds is null) return true;
        if (t.UserId is { } uid)
        {
            return a.AllowedUserIds.Contains(uid);
        }
        if (t.TeamId is { } tid)
        {
            return a.Scope is DataScope.Team or DataScope.All && tid == a.CallerTeamId;
        }
        return false;
    }

    private static bool CanManage(Access a, Guid? userId, Guid? teamId)
    {
        if (a.IsAdmin || a.AllowedUserIds is null) return true;
        if (userId is { } uid)
        {
            return a.AllowedUserIds.Contains(uid);
        }
        if (teamId is { } tid)
        {
            return a.Scope is DataScope.Team or DataScope.All && tid == a.CallerTeamId;
        }
        return false;
    }

    public async Task<IReadOnlyList<SalesTargetDto>> ListAsync(CancellationToken ct = default)
    {
        var access = await AccessAsync(ct);
        var targets = (await db.SalesTargets.AsNoTracking().ToListAsync(ct)).Where(t => CanSee(access, t)).ToList();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        return targets.Select(t => new SalesTargetDto(
            t.Id,
            t.UserId,
            t.UserId is { } uid ? users.GetValueOrDefault(uid) : null,
            t.TeamId,
            t.TeamId is { } tid ? teams.GetValueOrDefault(tid) : null,
            t.Metric,
            t.PeriodType,
            t.StartDate,
            t.EndDate,
            t.TargetValue
        )).ToList();
    }

    public async Task<SalesTargetDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.SalesTargets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Sales target", id);
        if (!CanSee(await AccessAsync(ct), t)) throw new NotFoundException("Sales target", id);

        var userName = t.UserId is { } uid ? await db.Users.Where(u => u.Id == uid).Select(u => $"{u.FirstName} {u.LastName}".Trim()).FirstOrDefaultAsync(ct) : null;
        var teamName = t.TeamId is { } tid ? await db.Teams.Where(team => team.Id == tid).Select(team => team.Name).FirstOrDefaultAsync(ct) : null;

        return new SalesTargetDto(t.Id, t.UserId, userName, t.TeamId, teamName, t.Metric, t.PeriodType, t.StartDate, t.EndDate, t.TargetValue);
    }

    public async Task<SalesTargetDto> SaveAsync(Guid? id, SaveSalesTargetRequest request, CancellationToken ct = default)
    {
        var access = await AccessAsync(ct);
        if (request.UserId is null && request.TeamId is null)
        {
            throw new ValidationException("UserId", "A target must be assigned to either a user or a team.");
        }

        if (request.UserId is { } reqUser)
        {
            if (!await db.Users.AnyAsync(u => u.Id == reqUser, ct))
                throw new NotFoundException("User", reqUser);
            if (access.AllowedUserIds is not null && !access.AllowedUserIds.Contains(reqUser))
                throw new ForbiddenException("You cannot set targets for users outside your scope.", "out_of_scope");
        }
        if (request.TeamId is { } reqTeam)
        {
            if (!await db.Teams.AnyAsync(t => t.Id == reqTeam, ct))
                throw new NotFoundException("Team", reqTeam);
            if (access.Scope == DataScope.Own || (access.CallerTeamId is not null && reqTeam != access.CallerTeamId))
                throw new ForbiddenException("You cannot set team targets outside your scope.", "out_of_scope");
        }
        if (!CanManage(access, request.UserId, request.TeamId)) throw new ForbiddenException();

        SalesTarget target;
        if (id is null)
        {
            target = new SalesTarget();
            db.SalesTargets.Add(target);
        }
        else
        {
            target = await db.SalesTargets.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Sales target", id);
            if (!CanSee(access, target)) throw new NotFoundException("Sales target", id);
            if (!CanManage(access, target.UserId, target.TeamId)) throw new ForbiddenException();
        }

        target.UserId = request.UserId;
        target.TeamId = request.TeamId;
        target.Metric = request.Metric;
        target.PeriodType = request.PeriodType;
        target.StartDate = request.StartDate;
        target.EndDate = request.EndDate;
        target.TargetValue = request.TargetValue;

        await db.SaveChangesAsync(ct);
        return await GetByIdAsync(target.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var target = await db.SalesTargets.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Sales target", id);
        var access = await AccessAsync(ct);
        if (!CanSee(access, target)) throw new NotFoundException("Sales target", id);
        if (!CanManage(access, target.UserId, target.TeamId)) throw new ForbiddenException();
        db.SalesTargets.Remove(target);
        await db.SaveChangesAsync(ct);
    }
}
