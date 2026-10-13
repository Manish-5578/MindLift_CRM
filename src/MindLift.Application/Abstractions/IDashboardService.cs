using MindLift.Application.Common.Time;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Application.Abstractions;

public sealed record DashboardContext(
    Guid UserId,
    Guid TenantId,
    DashboardView View,
    IReadOnlyList<Guid>? ScopeUserIds,
    DateRange Period,
    DateRange PreviousPeriod,
    Guid? PipelineId,
    string BaseCurrency,
    TimeZoneInfo TimeZone
);

public sealed record WidgetResult(
    string Key,
    string Title,
    WidgetSize Size,
    object? Data,
    string? DrillDownUrl,
    string? Error = null
);

public interface IDashboardWidget
{
    string Key { get; }
    AppModule Module { get; }
    DashboardView[] Views { get; }
    WidgetSize DefaultSize { get; }
    int DefaultOrder { get; }
    Task<WidgetResult> ComputeAsync(DashboardContext ctx, CancellationToken ct);
}

public interface IDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(
        DashboardView? requestedView,
        string? periodPreset,
        DateTime? fromUtc,
        DateTime? toUtc,
        Guid? teamId,
        Guid? userId,
        Guid? pipelineId,
        bool refresh,
        CancellationToken ct = default
    );

    Task<DashboardWidgetDto> GetWidgetAsync(
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
    );

    Task<DashboardLayoutDto> GetLayoutAsync(DashboardView view, CancellationToken ct = default);
    Task<DashboardLayoutDto> SaveLayoutAsync(SaveDashboardLayoutRequest request, CancellationToken ct = default);
    Task<DashboardLayoutDto> ResetLayoutAsync(DashboardView view, CancellationToken ct = default);
}

public interface ISalesTargetService
{
    Task<IReadOnlyList<SalesTargetDto>> ListAsync(CancellationToken ct = default);
    Task<SalesTargetDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SalesTargetDto> SaveAsync(Guid? id, SaveSalesTargetRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
