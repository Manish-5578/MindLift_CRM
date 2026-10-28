using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Communication;

public sealed class WhatsAppService(
    AppDbContext db,
    ITenantContext tenantContext,
    IFeatureFlags featureFlags,
    ITimelineEventBus timeline) : IWhatsAppService
{
    private static string NormalizePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var digitsAndPlus = new string(input.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (!digitsAndPlus.StartsWith('+') && digitsAndPlus.Length > 0)
        {
            digitsAndPlus = "+" + digitsAndPlus;
        }
        return digitsAndPlus;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    public async Task<WhatsAppSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(tenantId, ct);
        return MapSettingsDto(settings);
    }

    public async Task<WhatsAppSettingsDto> UpdateSettingsAsync(UpdateWhatsAppSettingsRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await GetSettingsEntityAsync(tenantId, ct);

        if (!string.IsNullOrWhiteSpace(request.PhoneNumberId))
            settings.PhoneNumberId = request.PhoneNumberId.Trim();

        if (!string.IsNullOrWhiteSpace(request.WabaId))
            settings.WabaId = request.WabaId.Trim();

        if (!string.IsNullOrWhiteSpace(request.DisplayPhoneNumber))
            settings.DisplayPhoneNumber = NormalizePhone(request.DisplayPhoneNumber);

        if (!string.IsNullOrWhiteSpace(request.AccessToken))
            settings.AccessToken = request.AccessToken.Trim();

        if (!string.IsNullOrWhiteSpace(request.WebhookVerifyToken))
            settings.WebhookVerifyToken = request.WebhookVerifyToken.Trim();

        settings.IsActive = request.IsActive;
        settings.Enforce24HourWindow = request.Enforce24HourWindow;
        settings.AutoCreateLeads = request.AutoCreateLeads;

        await db.SaveChangesAsync(ct);
        return MapSettingsDto(settings);
    }

    public async Task<string?> VerifyWebhookAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(verifyToken))
        {
            return null;
        }

        var settings = await db.WhatsAppSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.WebhookVerifyToken == verifyToken && s.IsActive && !s.IsDeleted, ct);

        return settings != null ? challenge : null;
    }

    public async Task<bool> ProcessWebhookPayloadAsync(WhatsAppCloudWebhookPayload payload, CancellationToken ct = default)
    {
        if (payload.Entry == null || payload.Entry.Count == 0)
            return false;

        foreach (var entry in payload.Entry)
        {
            if (entry.Changes == null) continue;

            foreach (var change in entry.Changes)
            {
                var val = change.Value;
                if (val == null) continue;

                var phoneNumberId = val.Metadata?.PhoneNumberId;
                if (string.IsNullOrWhiteSpace(phoneNumberId)) continue;

                var settings = await db.WhatsAppSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.PhoneNumberId == phoneNumberId && s.IsActive && !s.IsDeleted, ct);

                if (settings == null) continue;

                using var scope = tenantContext.Use(settings.TenantId);

                var isEnabled = await featureFlags.IsEnabledAsync("whatsapp", ct);
                if (!isEnabled) continue;

                // 1. Process incoming messages
                if (val.Messages != null)
                {
                    foreach (var msg in val.Messages)
                    {
                        var fromNumber = NormalizePhone(msg.From);
                        var body = msg.Text?.Body ?? string.Empty;
                        var msgId = msg.Id ?? $"wamid.{Guid.NewGuid():N}";

                        // Match contact, lead, or auto-create lead
                        Guid? contactId = null;
                        Guid? leadId = null;
                        Guid? accountId = null;
                        Guid? dealId = null;

                        var contact = await db.Contacts
                            .FirstOrDefaultAsync(c => c.NormalizedPhone == fromNumber || c.Phone == fromNumber, ct);

                        if (contact != null)
                        {
                            contactId = contact.Id;
                            accountId = contact.AccountId;
                        }
                        else
                        {
                            var lead = await db.Leads
                                .FirstOrDefaultAsync(l => l.NormalizedPhone == fromNumber || l.Phone == fromNumber, ct);

                            if (lead != null)
                            {
                                leadId = lead.Id;
                            }
                            else if (settings.AutoCreateLeads)
                            {
                                var newLead = new Lead
                                {
                                    TenantId = settings.TenantId,
                                    FirstName = "WhatsApp",
                                    LastName = fromNumber,
                                    Phone = fromNumber,
                                    NormalizedPhone = fromNumber,
                                    Source = "whatsapp",
                                    CompanyName = "Inbound WhatsApp Lead",
                                    Status = "new"
                                };
                                db.Leads.Add(newLead);
                                await db.SaveChangesAsync(ct);
                                leadId = newLead.Id;
                            }
                        }

                        // Update or create conversation
                        var conversation = await db.WhatsAppConversations
                            .FirstOrDefaultAsync(c => c.CustomerPhoneNumber == fromNumber, ct);

                        if (conversation == null)
                        {
                            conversation = new WhatsAppConversation
                            {
                                TenantId = settings.TenantId,
                                CustomerPhoneNumber = fromNumber,
                                LastCustomerMessageAtUtc = DateTime.UtcNow,
                                LastMessageAtUtc = DateTime.UtcNow,
                                UnreadCount = 1,
                                LeadId = leadId,
                                ContactId = contactId,
                                AccountId = accountId,
                                DealId = dealId
                            };
                            db.WhatsAppConversations.Add(conversation);
                        }
                        else
                        {
                            conversation.LastCustomerMessageAtUtc = DateTime.UtcNow;
                            conversation.LastMessageAtUtc = DateTime.UtcNow;
                            conversation.UnreadCount++;
                            if (leadId != null) conversation.LeadId = leadId;
                            if (contactId != null) conversation.ContactId = contactId;
                            if (accountId != null) conversation.AccountId = accountId;
                        }

                        await db.SaveChangesAsync(ct);

                        // Save message
                        var message = new WhatsAppMessage
                        {
                            TenantId = settings.TenantId,
                            MessageId = msgId,
                            Direction = "Inbound",
                            FromNumber = fromNumber,
                            ToNumber = settings.DisplayPhoneNumber,
                            MessageType = msg.Type ?? "text",
                            Content = body,
                            Status = "delivered",
                            DeliveredAtUtc = DateTime.UtcNow,
                            ConversationId = conversation.Id,
                            LeadId = leadId,
                            ContactId = contactId,
                            AccountId = accountId,
                            DealId = dealId
                        };
                        db.WhatsAppMessages.Add(message);
                        await db.SaveChangesAsync(ct);

                        // Publish timeline event
                        await PublishTimelineAsync(
                            leadId,
                            contactId,
                            dealId,
                            "whatsapp_message_received",
                            $"Received WhatsApp message from {fromNumber}: \"{Truncate(body, 60)}\"",
                            ct);
                    }
                }

                // 2. Process status callbacks (delivered, read, failed)
                if (val.Statuses != null)
                {
                    foreach (var statusUpdate in val.Statuses)
                    {
                        if (string.IsNullOrWhiteSpace(statusUpdate.Id)) continue;

                        var existingMsg = await db.WhatsAppMessages
                            .FirstOrDefaultAsync(m => m.MessageId == statusUpdate.Id, ct);

                        if (existingMsg != null)
                        {
                            existingMsg.Status = statusUpdate.Status ?? existingMsg.Status;
                            if (statusUpdate.Status == "delivered" && existingMsg.DeliveredAtUtc == null)
                                existingMsg.DeliveredAtUtc = DateTime.UtcNow;
                            if (statusUpdate.Status == "read" && existingMsg.ReadAtUtc == null)
                            {
                                existingMsg.ReadAtUtc = DateTime.UtcNow;
                                await PublishTimelineAsync(
                                    existingMsg.LeadId,
                                    existingMsg.ContactId,
                                    existingMsg.DealId,
                                    "whatsapp_message_read",
                                    $"WhatsApp message read by {existingMsg.ToNumber}",
                                    ct);
                            }
                        }
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        return true;
    }

    public async Task<WhatsAppMessageDto> SendMessageAsync(SendWhatsAppMessageRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        if (string.IsNullOrWhiteSpace(request.ToNumber))
            throw new ValidationException("toNumber", "ToNumber is required.");

        var normalizedTo = NormalizePhone(request.ToNumber);
        var settings = await GetSettingsEntityAsync(tenantId, ct);

        string content;
        var messageType = string.IsNullOrWhiteSpace(request.MessageType) ? "text" : request.MessageType.ToLowerInvariant();

        if (messageType == "template")
        {
            if (string.IsNullOrWhiteSpace(request.TemplateName))
                throw new ValidationException("templateName", "TemplateName is required when sending a template message.");

            content = $"[Template: {request.TemplateName}]";
            if (request.TemplateParameters is { Length: > 0 } paramsList)
            {
                content += $" ({string.Join(", ", paramsList)})";
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Content))
                throw new ValidationException("content", "Message content is required.");

            content = request.Content;

            // Check 24-hour customer care window if enabled
            if (settings.Enforce24HourWindow)
            {
                var existingConv = await db.WhatsAppConversations
                    .FirstOrDefaultAsync(c => c.CustomerPhoneNumber == normalizedTo, ct);

                var isWindowOpen = existingConv?.LastCustomerMessageAtUtc != null &&
                    (DateTime.UtcNow - existingConv.LastCustomerMessageAtUtc.Value) <= TimeSpan.FromHours(24);

                if (!isWindowOpen)
                {
                    throw new ValidationException("window_expired",
                        "The 24-hour customer care window has expired for this number. Outbound messages outside the session window must use a pre-approved WhatsApp template message.");
                }
            }
        }

        // Resolve Contact / Lead / Account / Deal associations
        var leadId = request.LeadId;
        var contactId = request.ContactId;
        var accountId = request.AccountId;
        var dealId = request.DealId;

        if (contactId == null && leadId == null)
        {
            var contact = await db.Contacts.FirstOrDefaultAsync(c => c.NormalizedPhone == normalizedTo || c.Phone == normalizedTo, ct);
            if (contact != null)
            {
                contactId = contact.Id;
                accountId ??= contact.AccountId;
            }
            else
            {
                var lead = await db.Leads.FirstOrDefaultAsync(l => l.NormalizedPhone == normalizedTo || l.Phone == normalizedTo, ct);
                if (lead != null)
                {
                    leadId = lead.Id;
                }
            }
        }

        // Find or create conversation
        var conversation = await db.WhatsAppConversations
            .FirstOrDefaultAsync(c => c.CustomerPhoneNumber == normalizedTo, ct);

        if (conversation == null)
        {
            conversation = new WhatsAppConversation
            {
                TenantId = tenantId,
                CustomerPhoneNumber = normalizedTo,
                LastMessageAtUtc = DateTime.UtcNow,
                UnreadCount = 0,
                LeadId = leadId,
                ContactId = contactId,
                AccountId = accountId,
                DealId = dealId
            };
            db.WhatsAppConversations.Add(conversation);
        }
        else
        {
            conversation.LastMessageAtUtc = DateTime.UtcNow;
            if (leadId != null) conversation.LeadId = leadId;
            if (contactId != null) conversation.ContactId = contactId;
            if (accountId != null) conversation.AccountId = accountId;
            if (dealId != null) conversation.DealId = dealId;
        }

        await db.SaveChangesAsync(ct);

        var messageId = "wamid." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var message = new WhatsAppMessage
        {
            TenantId = tenantId,
            MessageId = messageId,
            Direction = "Outbound",
            FromNumber = settings.DisplayPhoneNumber,
            ToNumber = normalizedTo,
            MessageType = messageType,
            Content = content,
            TemplateName = request.TemplateName,
            TemplateLanguage = request.TemplateLanguage,
            TemplateParameters = request.TemplateParameters ?? [],
            Status = "sent",
            SentAtUtc = DateTime.UtcNow,
            ConversationId = conversation.Id,
            LeadId = leadId,
            ContactId = contactId,
            AccountId = accountId,
            DealId = dealId
        };

        db.WhatsAppMessages.Add(message);
        await db.SaveChangesAsync(ct);

        await PublishTimelineAsync(
            leadId,
            contactId,
            dealId,
            "whatsapp_message_sent",
            $"Sent WhatsApp message to {normalizedTo}: \"{Truncate(content, 60)}\"",
            ct);

        return MapMessageDto(message);
    }

    public async Task<IReadOnlyList<WhatsAppConversationDto>> GetConversationsAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var convs = await db.WhatsAppConversations
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.LastMessageAtUtc)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return convs.Select(c =>
        {
            var isWindowOpen = c.LastCustomerMessageAtUtc != null && (now - c.LastCustomerMessageAtUtc.Value) <= TimeSpan.FromHours(24);
            var windowExpiresAt = c.LastCustomerMessageAtUtc?.AddHours(24);

            return new WhatsAppConversationDto(
                c.Id,
                c.CustomerPhoneNumber,
                c.LastCustomerMessageAtUtc,
                c.LastMessageAtUtc,
                c.UnreadCount,
                isWindowOpen,
                windowExpiresAt,
                c.LeadId,
                c.ContactId,
                c.AccountId,
                c.DealId);
        }).ToList();
    }

    public async Task<IReadOnlyList<WhatsAppMessageDto>> GetMessagesAsync(string customerPhone, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var normalized = NormalizePhone(customerPhone);
        var messages = await db.WhatsAppMessages
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && (m.FromNumber == normalized || m.ToNumber == normalized))
            .OrderBy(m => m.SentAtUtc ?? m.CreatedAt)
            .ToListAsync(ct);

        return messages.Select(MapMessageDto).ToList();
    }

    private async Task<WhatsAppSettings> GetSettingsEntityAsync(Guid tenantId, CancellationToken ct)
    {
        var settings = await db.WhatsAppSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings == null)
        {
            settings = new WhatsAppSettings
            {
                TenantId = tenantId,
                PhoneNumberId = "10000000000",
                WabaId = "20000000000",
                DisplayPhoneNumber = "+15551234567",
                AccessToken = "EAAG" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                WebhookVerifyToken = "wa_verify_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
                IsActive = true,
                Enforce24HourWindow = true,
                AutoCreateLeads = false
            };
            db.WhatsAppSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }
        return settings;
    }

    private async Task PublishTimelineAsync(Guid? leadId, Guid? contactId, Guid? dealId, string kind, string summary, CancellationToken ct)
    {
        try
        {
            if (leadId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("lead", leadId.Value, kind, summary, "whatsapp"), ct);
            }
            else if (contactId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("contact", contactId.Value, kind, summary, "whatsapp"), ct);
            }

            if (dealId != null)
            {
                await timeline.PublishAsync(new TimelineEventInput("deal", dealId.Value, kind, summary, "whatsapp"), ct);
            }
        }
        catch
        {
            // Non-fatal
        }
    }

    private static WhatsAppSettingsDto MapSettingsDto(WhatsAppSettings s) => new(
        s.PhoneNumberId,
        s.WabaId,
        s.DisplayPhoneNumber,
        s.WebhookVerifyToken,
        s.IsActive,
        s.Enforce24HourWindow,
        s.AutoCreateLeads);

    private static WhatsAppMessageDto MapMessageDto(WhatsAppMessage m) => new(
        m.Id,
        m.MessageId,
        m.Direction,
        m.FromNumber,
        m.ToNumber,
        m.MessageType,
        m.Content,
        m.TemplateName,
        m.Status,
        m.ErrorMessage,
        m.SentAtUtc,
        m.DeliveredAtUtc,
        m.ReadAtUtc,
        m.LeadId,
        m.ContactId,
        m.AccountId,
        m.DealId,
        m.ConversationId);
}
