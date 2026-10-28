using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Phase 4 / #28: Automated data enrichment (data-enrichment).
/// Service for real-time firmographic lookups, lead/account auto-enrichment,
/// and batch intelligence sync.
/// </summary>
public interface IDataEnrichmentService
{
    Task<CompanyEnrichmentData?> LookupAsync(
        string? domain = null,
        string? email = null,
        CancellationToken ct = default);

    Task<EnrichmentResultDto> EnrichLeadAsync(
        Guid leadId,
        bool? overwrite = null,
        CancellationToken ct = default);

    Task<EnrichmentResultDto> EnrichAccountAsync(
        Guid accountId,
        bool? overwrite = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<EnrichmentResultDto>> BatchEnrichAsync(
        BatchEnrichRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<EnrichmentLogDto>> ListLogsAsync(
        string? entityType = null,
        Guid? entityId = null,
        CancellationToken ct = default);

    Task<EnrichmentConfigDto> GetConfigAsync(CancellationToken ct = default);

    Task<EnrichmentConfigDto> UpdateConfigAsync(
        SaveEnrichmentConfigRequest request,
        CancellationToken ct = default);
}
