using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IVelocityReportService
{
    Task<PipelineFunnelReportDto> GetFunnelReportAsync(PipelineFunnelReportRequest request, CancellationToken ct = default);
    Task<SalesVelocityReportDto> GetSalesVelocityAsync(SalesVelocityReportRequest request, CancellationToken ct = default);
}
