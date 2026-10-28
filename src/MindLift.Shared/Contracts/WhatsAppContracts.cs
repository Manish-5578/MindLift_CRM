using System.Text.Json.Serialization;

namespace MindLift.Shared.Contracts;

public sealed record WhatsAppSettingsDto(
    string PhoneNumberId,
    string WabaId,
    string DisplayPhoneNumber,
    string WebhookVerifyToken,
    bool IsActive,
    bool Enforce24HourWindow,
    bool AutoCreateLeads);

public sealed record UpdateWhatsAppSettingsRequest(
    string? PhoneNumberId,
    string? WabaId,
    string? DisplayPhoneNumber,
    string? AccessToken,
    string? WebhookVerifyToken,
    bool IsActive,
    bool Enforce24HourWindow,
    bool AutoCreateLeads);

public sealed record SendWhatsAppMessageRequest(
    string ToNumber,
    string MessageType = "text",
    string? Content = null,
    string? TemplateName = null,
    string? TemplateLanguage = "en_US",
    string[]? TemplateParameters = null,
    Guid? LeadId = null,
    Guid? ContactId = null,
    Guid? AccountId = null,
    Guid? DealId = null);

public sealed record WhatsAppMessageDto(
    Guid Id,
    string MessageId,
    string Direction,
    string FromNumber,
    string ToNumber,
    string MessageType,
    string Content,
    string? TemplateName,
    string Status,
    string? ErrorMessage,
    DateTime? SentAtUtc,
    DateTime? DeliveredAtUtc,
    DateTime? ReadAtUtc,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId,
    Guid? ConversationId);

public sealed record WhatsAppConversationDto(
    Guid Id,
    string CustomerPhoneNumber,
    DateTime? LastCustomerMessageAtUtc,
    DateTime LastMessageAtUtc,
    int UnreadCount,
    bool Is24HourWindowOpen,
    DateTime? WindowExpiresAtUtc,
    Guid? LeadId,
    Guid? ContactId,
    Guid? AccountId,
    Guid? DealId);

public sealed record WhatsAppWebhookVerificationQuery(
    [property: JsonPropertyName("hub.mode")] string? HubMode,
    [property: JsonPropertyName("hub.verify_token")] string? HubVerifyToken,
    [property: JsonPropertyName("hub.challenge")] string? HubChallenge);

public sealed class WhatsAppCloudWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<WhatsAppEntry>? Entry { get; set; }
}

public sealed class WhatsAppEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("changes")]
    public List<WhatsAppChange>? Changes { get; set; }
}

public sealed class WhatsAppChange
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public WhatsAppValue? Value { get; set; }
}

public sealed class WhatsAppValue
{
    [JsonPropertyName("messaging_product")]
    public string? MessagingProduct { get; set; }

    [JsonPropertyName("metadata")]
    public WhatsAppMetadata? Metadata { get; set; }

    [JsonPropertyName("messages")]
    public List<WhatsAppIncomingMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<WhatsAppStatusUpdate>? Statuses { get; set; }
}

public sealed class WhatsAppMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string? DisplayPhoneNumber { get; set; }

    [JsonPropertyName("phone_number_id")]
    public string? PhoneNumberId { get; set; }
}

public sealed class WhatsAppIncomingMessage
{
    [JsonPropertyName("from")]
    public string? From { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public WhatsAppTextContent? Text { get; set; }
}

public sealed class WhatsAppTextContent
{
    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

public sealed class WhatsAppStatusUpdate
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; } // "sent", "delivered", "read", "failed"

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("recipient_id")]
    public string? RecipientId { get; set; }
}
