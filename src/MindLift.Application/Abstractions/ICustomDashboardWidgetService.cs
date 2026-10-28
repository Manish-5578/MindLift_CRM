using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ICustomDashboardWidgetService
{
    Task<IReadOnlyList<CustomDashboardWidgetDto>> ListWidgetsAsync(CancellationToken ct = default);
    Task<CustomDashboardWidgetDto> GetWidgetAsync(Guid id, CancellationToken ct = default);
    Task<CustomDashboardWidgetDto> CreateWidgetAsync(SaveCustomDashboardWidgetRequest request, CancellationToken ct = default);
    Task<CustomDashboardWidgetDto> UpdateWidgetAsync(Guid id, SaveCustomDashboardWidgetRequest request, CancellationToken ct = default);
    Task<bool> DeleteWidgetAsync(Guid id, CancellationToken ct = default);
    Task<CustomWidgetDataDto> EvaluateDataAsync(Guid id, CancellationToken ct = default);
    Task<CustomWidgetDataDto> PreviewDataAsync(SaveCustomDashboardWidgetRequest request, CancellationToken ct = default);
}
