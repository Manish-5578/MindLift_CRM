using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IBccDropboxService
{
    Task<BccDropboxAddressDto> GetOrCreateUserDropboxAddressAsync(Guid userId, CancellationToken ct = default);
    Task<BccDropboxAddressDto> RegenerateUserDropboxAddressAsync(Guid userId, CancellationToken ct = default);
    Task<BccDropboxSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<BccDropboxSettingsDto> UpdateSettingsAsync(UpdateBccDropboxSettingsRequest request, CancellationToken ct = default);
    Task<InboundBccEmailResult> ProcessInboundEmailAsync(InboundBccEmailRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BccDropboxLogDto>> ListLogsAsync(int limit = 50, CancellationToken ct = default);
}
