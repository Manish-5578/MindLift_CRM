using System.ComponentModel.DataAnnotations;

namespace MindLift.Shared.Contracts;

public enum CommunicationChannel
{
    Email = 1,
    WhatsApp = 2,
    Sms = 3
}

public enum MessageDirection
{
    Inbound = 1,
    Outbound = 2
}

public enum MessageStatus
{
    Draft = 1,
    Queued = 2,
    Sending = 3,
    Sent = 4,
    Delivered = 5,
    Read = 6,
    Failed = 7
}

public enum TemplateCategory
{
    Marketing = 1,
    Utility = 2,
    Authentication = 3
}

public sealed record ChannelAccountDto(
    Guid Id,
    CommunicationChannel Channel,
    string Name,
    string SenderAddress,
    bool IsDefault,
    bool IsActive,
    bool AutoCreateLeads,
    DateTime CreatedAt,
    DateTime? LastSyncedAt,
    string? StatusMessage
);

public sealed record SaveChannelAccountRequest(
    [Required] CommunicationChannel Channel,
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(255)] string SenderAddress,
    bool IsDefault,
    bool IsActive,
    bool AutoCreateLeads,
    string? Host = null,
    int? Port = null,
    string? Username = null,
    string? Password = null,
    string? PhoneNumberId = null,
    string? AccountSid = null,
    string? AuthToken = null,
    string? WebhookSecret = null
);

public sealed record MessageAttachmentDto(
    Guid FileId,
    string FileName,
    long FileSizeBytes,
    string ContentType
);

public sealed record MessageDto(
    Guid Id,
    CommunicationChannel Channel,
    MessageDirection Direction,
    MessageStatus Status,
    string From,
    string To,
    string? Subject,
    string Body,
    string? ErrorMessage,
    Guid ThreadId,
    Guid? ChannelAccountId,
    Guid? LeadId,
    string? LeadName,
    Guid? ContactId,
    string? ContactName,
    Guid? AccountId,
    Guid? DealId,
    Guid? SentByUserId,
    string? SentByUserName,
    DateTime CreatedAt,
    DateTime? DeliveredAt,
    DateTime? ReadAt,
    string? ExternalMessageId = null,
    List<MessageAttachmentDto>? Attachments = null
);

public sealed record SendMessageRequest(
    [Required] CommunicationChannel Channel,
    Guid? ChannelAccountId,
    [Required, MaxLength(255)] string To,
    string? Subject,
    [Required] string Body,
    Guid? TemplateId = null,
    Dictionary<string, string>? TemplateParameters = null,
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? AccountId = null,
    Guid? DealId = null,
    List<Guid>? AttachmentFileIds = null
);

public sealed record MessageTemplateDto(
    Guid Id,
    CommunicationChannel Channel,
    string Name,
    string Code,
    TemplateCategory Category,
    string? Subject,
    string Body,
    List<string> Placeholders,
    bool IsApproved,
    string? ExternalTemplateId,
    DateTime CreatedAt
);

public sealed record SaveMessageTemplateRequest(
    [Required] CommunicationChannel Channel,
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(100)] string Code,
    TemplateCategory Category,
    string? Subject,
    [Required] string Body
);

public sealed record InboxThreadDto(
    Guid ThreadId,
    CommunicationChannel Channel,
    string ParticipantAddress,
    string? ParticipantName,
    Guid? LeadId,
    Guid? ContactId,
    string? LastMessageSnippet,
    DateTime LastMessageAt,
    int UnreadCount,
    MessageStatus LastMessageStatus
);

public sealed record OptOutDto(
    Guid Id,
    CommunicationChannel? Channel,
    string Target,
    string? Reason,
    DateTime CreatedAt
);

public sealed record RegisterOptOutRequest(
    CommunicationChannel? Channel,
    [Required, MaxLength(255)] string Target,
    string? Reason = null
);

public sealed record TestConnectionResult(
    bool Success,
    string Message
);
