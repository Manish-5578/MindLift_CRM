using MindLift.Domain.Common;
using MindLift.Shared.Contracts;

namespace MindLift.Domain.Entities;

[Audited]
public sealed class ChannelAccount : TenantEntity, ISampleData
{
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
    public string Name { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AutoCreateLeads { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public string? PhoneNumberId { get; set; }
    public string? AccountSid { get; set; }
    public string? EncryptedAuthToken { get; set; }
    public string? WebhookSecret { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsSample { get; set; }
}

[Audited]
public sealed class Message : TenantEntity, ISampleData
{
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.Email;
    public MessageDirection Direction { get; set; } = MessageDirection.Outbound;
    public MessageStatus Status { get; set; } = MessageStatus.Sent;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Guid ThreadId { get; set; }
    public Guid? ChannelAccountId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? SentByUserId { get; set; }
    public string? ExternalMessageId { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public string AttachmentsJson { get; set; } = "[]";
    public bool IsSample { get; set; }
}

[Audited]
public sealed class MessageTemplate : TenantEntity
{
    public CommunicationChannel Channel { get; set; } = CommunicationChannel.WhatsApp;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public TemplateCategory Category { get; set; } = TemplateCategory.Utility;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsApproved { get; set; } = true;
    public string? ExternalTemplateId { get; set; }
}

[Audited]
public sealed class CommunicationOptOut : TenantEntity
{
    public CommunicationChannel? Channel { get; set; }
    public string Target { get; set; } = string.Empty;
    public string? Reason { get; set; }
}
