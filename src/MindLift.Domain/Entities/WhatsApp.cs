using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 5 / #34: WhatsApp Business (whatsapp).
/// Per-tenant WhatsApp Business Cloud API settings.
/// </summary>
[Audited]
public sealed class WhatsAppSettings : TenantEntity
{
    public string PhoneNumberId { get; set; } = string.Empty;
    public string WabaId { get; set; } = string.Empty;
    public string DisplayPhoneNumber { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool Enforce24HourWindow { get; set; } = true;
    public bool AutoCreateLeads { get; set; }
}

/// <summary>
/// Phase 5 / #34: WhatsApp Conversation session tracking 24-hour customer care window.
/// </summary>
[Audited]
public sealed class WhatsAppConversation : TenantEntity
{
    public string CustomerPhoneNumber { get; set; } = string.Empty;
    public DateTime? LastCustomerMessageAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public int UnreadCount { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
}

/// <summary>
/// Phase 5 / #34: Inbound and outbound WhatsApp messages.
/// </summary>
[Audited]
public sealed class WhatsAppMessage : TenantEntity
{
    public string MessageId { get; set; } = string.Empty;
    public string Direction { get; set; } = "Inbound"; // "Inbound", "Outbound"
    public string FromNumber { get; set; } = string.Empty;
    public string ToNumber { get; set; } = string.Empty;
    public string MessageType { get; set; } = "text"; // "text", "template", "interactive", "image", "document"
    public string Content { get; set; } = string.Empty;
    public string? TemplateName { get; set; }
    public string? TemplateLanguage { get; set; }
    public string[] TemplateParameters { get; set; } = [];
    public string Status { get; set; } = "sent"; // "queued", "sent", "delivered", "read", "failed"
    public string? ErrorMessage { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? AccountId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? ConversationId { get; set; }
}
