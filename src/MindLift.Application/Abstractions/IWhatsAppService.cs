using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IWhatsAppService
{
    Task<WhatsAppSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<WhatsAppSettingsDto> UpdateSettingsAsync(UpdateWhatsAppSettingsRequest request, CancellationToken ct = default);
    Task<string?> VerifyWebhookAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default);
    Task<bool> ProcessWebhookPayloadAsync(WhatsAppCloudWebhookPayload payload, CancellationToken ct = default);
    Task<WhatsAppMessageDto> SendMessageAsync(SendWhatsAppMessageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppConversationDto>> GetConversationsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<WhatsAppMessageDto>> GetMessagesAsync(string customerPhone, CancellationToken ct = default);
}
