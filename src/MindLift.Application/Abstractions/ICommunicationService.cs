using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public sealed record ChannelSendResult(bool Success, string? ExternalMessageId, string? ErrorMessage = null);

public sealed record InboundMessagePayload(
    CommunicationChannel Channel,
    string From,
    string To,
    string? Subject,
    string Body,
    string? ExternalMessageId,
    DateTime ReceivedAt,
    List<MessageAttachmentDto>? Attachments = null
);

public interface IChannelSender
{
    CommunicationChannel Channel { get; }
    Task<ChannelSendResult> SendAsync(ChannelAccount account, string to, string? subject, string body, List<Guid>? attachmentIds = null, CancellationToken ct = default);
    Task<TestConnectionResult> TestAsync(ChannelAccount account, CancellationToken ct = default);
}

public interface ICommunicationService
{
    Task<MessageDto> SendMessageAsync(SendMessageRequest request, Guid userId, CancellationToken ct = default);
    Task<List<MessageDto>> GetThreadMessagesAsync(Guid threadId, CancellationToken ct = default);
    Task<List<InboxThreadDto>> GetInboxThreadsAsync(CommunicationChannel? channel = null, string? search = null, CancellationToken ct = default);
    Task<List<MessageDto>> GetRecordMessagesAsync(string entityType, Guid entityId, CancellationToken ct = default);
    Task MarkThreadReadAsync(Guid threadId, CancellationToken ct = default);
    /// <summary>Stores an inbound message. When <paramref name="channelAccountId"/> is given (a verified webhook), that channel is used and the addresses in the payload are not trusted to pick the workspace.</summary>
    Task HandleInboundMessageAsync(InboundMessagePayload payload, Guid? channelAccountId = null, CancellationToken ct = default);

    /// <summary>Updates a delivery status. When <paramref name="tenantId"/> is given (a verified webhook), only that workspace's messages can change.</summary>
    Task UpdateMessageStatusAsync(string externalMessageId, MessageStatus status, DateTime? timestamp = null, string? error = null, Guid? tenantId = null, Guid? channelAccountId = null, CancellationToken ct = default);
}

public interface IChannelAccountService
{
    Task<List<ChannelAccountDto>> GetAccountsAsync(CancellationToken ct = default);
    Task<ChannelAccountDto> GetAccountByIdAsync(Guid id, CancellationToken ct = default);
    Task<ChannelAccountDto> SaveAccountAsync(Guid? id, SaveChannelAccountRequest request, CancellationToken ct = default);
    Task DeleteAccountAsync(Guid id, CancellationToken ct = default);
    Task<TestConnectionResult> TestConnectionAsync(Guid id, CancellationToken ct = default);
}

public interface IMessageTemplateService
{
    Task<List<MessageTemplateDto>> GetTemplatesAsync(CommunicationChannel? channel = null, CancellationToken ct = default);
    Task<MessageTemplateDto> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);
    Task<MessageTemplateDto> SaveTemplateAsync(Guid? id, SaveMessageTemplateRequest request, CancellationToken ct = default);
    Task DeleteTemplateAsync(Guid id, CancellationToken ct = default);
    Task<string> RenderTemplateAsync(Guid templateId, string entityType, Guid entityId, Dictionary<string, string>? extraParams = null, CancellationToken ct = default);
}

public interface IOptOutService
{
    Task<bool> IsOptedOutAsync(string target, CommunicationChannel channel, CancellationToken ct = default);
    Task<List<OptOutDto>> GetOptOutsAsync(CancellationToken ct = default);
    Task<OptOutDto> RegisterOptOutAsync(RegisterOptOutRequest request, CancellationToken ct = default);
    Task RemoveOptOutAsync(Guid id, CancellationToken ct = default);
}
