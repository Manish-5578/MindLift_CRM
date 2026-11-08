using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Chat;

public sealed class ChatService(AppDbContext db, ITenantContext tenant, INotificationService notifications, ChatPresence presence, IFileStorage storage, Microsoft.Extensions.Logging.ILogger<ChatService> logger)
{
    private Guid Me => tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedException("no_tenant", "No active tenant.");

    private static readonly Dictionary<string, (string Type, string Kind)> AttachmentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ("image/jpeg", "image"), [".jpeg"] = ("image/jpeg", "image"), [".png"] = ("image/png", "image"), [".webp"] = ("image/webp", "image"),
        [".m4a"] = ("audio/mp4", "audio"), [".aac"] = ("audio/aac", "audio"), [".mp3"] = ("audio/mpeg", "audio"), [".ogg"] = ("audio/ogg", "audio"),
        [".opus"] = ("audio/ogg", "audio"), [".wav"] = ("audio/wav", "audio"), [".3gp"] = ("audio/3gpp", "audio"),
        [".pdf"] = ("application/pdf", "file"), [".txt"] = ("text/plain", "file"), [".csv"] = ("text/csv", "file"),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "file"),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "file"),
    };

    private const long MaxAttachmentBytes = 15L * 1024 * 1024;

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(string? channel, Guid? recipientId, DateTime? since, CancellationToken ct = default, string? search = null)
    {
        var me = Me;
        IQueryable<InternalChatMessage> query = db.InternalChatMessages.AsNoTracking();

        if (recipientId.HasValue)
        {
            var other = recipientId.Value;
            query = query.Where(m => (m.SenderId == me && m.RecipientId == other) ||
                                     (m.SenderId == other && m.RecipientId == me));
        }
        else
        {
            var ch = string.IsNullOrWhiteSpace(channel) ? "general" : channel.Trim().ToLowerInvariant();
            query = query.Where(m => m.RecipientId == null && m.ChannelName == ch);
        }

        if (since.HasValue)
        {
            query = query.Where(m => m.CreatedAtUtc > since.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(m => m.Message.ToLower().Contains(term) || (m.AttachmentName != null && m.AttachmentName.ToLower().Contains(term)));
        }

        var list = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        list.Reverse(); // chronological order
        return await MapAsync(list, me, ct);
    }

    private async Task<IReadOnlyList<ChatMessageDto>> MapAsync(List<InternalChatMessage> list, Guid me, CancellationToken ct)
    {
        var replyIds = list.Where(m => m.ReplyToId.HasValue).Select(m => m.ReplyToId!.Value).Distinct().ToList();
        var replies = replyIds.Count == 0
            ? new Dictionary<Guid, InternalChatMessage>()
            : await db.InternalChatMessages.AsNoTracking().Where(m => replyIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

        return list.Select(m =>
        {
            InternalChatMessage? reply = null;
            if (m.ReplyToId is Guid rid) replies.TryGetValue(rid, out reply);
            return ToDto(m, me, reply);
        }).ToList();
    }

    private static string Describe(InternalChatMessage m)
        => !string.IsNullOrWhiteSpace(m.Message) ? m.Message : m.AttachmentKind switch
        {
            "image" => "\U0001F4F7 Photo",
            "audio" => "\U0001F3A4 Voice message",
            "file" => "\U0001F4CE " + (m.AttachmentName ?? "File"),
            _ => string.Empty,
        };

    private static ChatMessageDto ToDto(InternalChatMessage m, Guid me, InternalChatMessage? reply)
        => new(
            m.Id, m.SenderId, m.SenderName, m.SenderEmail, m.RecipientId, m.ChannelName, m.Message, m.CreatedAtUtc, m.IsRead,
            m.ReplyToId,
            reply is null ? null : Describe(reply),
            reply?.SenderName,
            m.AttachmentKey is null ? null : m.AttachmentName,
            m.AttachmentKey is null ? null : m.AttachmentKind,
            m.AttachmentSize,
            m.AttachmentDurationMs,
            ReadReactions(m, me));

    private static List<ChatReactionDto> ReadReactions(InternalChatMessage m, Guid me)
    {
        var result = new List<ChatReactionDto>();
        if (string.IsNullOrWhiteSpace(m.Reactions)) return result;
        try
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(m.Reactions) ?? [];
            foreach (var (emoji, users) in map.Where(kv => kv.Value.Count > 0))
                result.Add(new ChatReactionDto(emoji, users.Count, users.Contains(me)));
        }
        catch (System.Text.Json.JsonException) { }
        return result;
    }

    public async Task<ChatMessageDto> SendMessageAsync(SendChatMessageRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ValidationException("message_empty", "Chat message cannot be empty.");
        }

        return await SaveAsync(request.Message.Trim(), request.ChannelName, request.RecipientId, request.ReplyToId, null, ct);
    }

    /// <summary>A photo, document or voice note, with an optional caption.</summary>
    public async Task<ChatMessageDto> SendAttachmentAsync(Stream content, string fileName, long length, string? caption, string? channel, Guid? recipientId, Guid? replyToId, int? durationMs, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!AttachmentTypes.TryGetValue(extension, out var kind))
        {
            throw new ValidationException("attachment_type", "This file type cannot be sent in chat.");
        }

        if (length > MaxAttachmentBytes)
        {
            throw new ValidationException("attachment_size", "Files can be up to 15 MB.");
        }

        var key = $"{CurrentTenantId}/chat/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await storage.SaveAsync(key, content, ct);
        return await SaveAsync(caption?.Trim() ?? string.Empty, channel, recipientId, replyToId,
            new Attachment(key, Path.GetFileName(fileName!), kind.Type, kind.Kind, length, durationMs), ct);
    }

    private sealed record Attachment(string Key, string Name, string ContentType, string Kind, long Size, int? DurationMs);

    private async Task<ChatMessageDto> SaveAsync(string text, string? channel, Guid? recipientId, Guid? replyToId, Attachment? attachment, CancellationToken ct)
    {
        var me = Me;
        var myName = !string.IsNullOrWhiteSpace(tenant.UserName) ? tenant.UserName! : "Colleague";

        var msg = new InternalChatMessage
        {
            Id = Guid.NewGuid(),
            TenantId = CurrentTenantId,
            SenderId = me,
            SenderName = myName,
            SenderEmail = tenant.UserEmail,
            RecipientId = recipientId,
            ChannelName = recipientId == null ? (string.IsNullOrWhiteSpace(channel) ? "general" : channel.Trim().ToLowerInvariant()) : null,
            Message = text,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false,
            ReplyToId = replyToId,
            AttachmentKey = attachment?.Key,
            AttachmentName = attachment?.Name,
            AttachmentContentType = attachment?.ContentType,
            AttachmentKind = attachment?.Kind,
            AttachmentSize = attachment?.Size,
            AttachmentDurationMs = attachment?.DurationMs,
        };

        db.InternalChatMessages.Add(msg);
        await db.SaveChangesAsync(ct);
        presence.ClearTyping(me);

        if (msg.RecipientId is Guid recipient)
        {
            try
            {
                // One notification record: it shows in the recipient's Alerts list and is delivered as a push.
                var described = Describe(msg);
                var preview = described.Length <= 140 ? described : described[..139] + "...";
                await notifications.NotifyAsync(new NotificationRequest(
                    new[] { recipient }, NotificationTypes.ChatMessage, myName, preview,
                    EntityType: "ChatMessage", EntityId: msg.Id, Url: "/chat",
                    GroupKey: $"chat:{me}", InAppOnly: true), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Chat notification failed for recipient {Recipient}", recipient);
            }
        }
        presence.Touch(me);

        InternalChatMessage? reply = null;
        if (replyToId is Guid rid) reply = await db.InternalChatMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == rid, ct);
        return ToDto(msg, me, reply);
    }

    /// <summary>The sender or the recipient (or anyone, for a channel message) may open an attachment.</summary>
    public async Task<(string Name, string ContentType, Stream Content)> OpenAttachmentAsync(Guid messageId, CancellationToken ct = default)
    {
        var me = Me;
        var m = await db.InternalChatMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (m?.AttachmentKey is null || (m.RecipientId != null && m.SenderId != me && m.RecipientId != me))
        {
            throw new NotFoundException("Attachment", messageId);
        }

        return (m.AttachmentName ?? "file", m.AttachmentContentType ?? "application/octet-stream", await storage.OpenReadAsync(m.AttachmentKey, ct));
    }

    /// <summary>Tapping the same emoji again removes your reaction.</summary>
    public async Task<ChatMessageDto> ReactAsync(Guid messageId, string emoji, CancellationToken ct = default)
    {
        var me = Me;
        emoji = (emoji ?? string.Empty).Trim();
        if (emoji.Length == 0 || emoji.Length > 16)
        {
            throw new ValidationException("emoji_invalid", "Choose an emoji.");
        }

        var m = await db.InternalChatMessages.FirstOrDefaultAsync(x => x.Id == messageId, ct)
            ?? throw new NotFoundException("Message", messageId);
        if (m.RecipientId != null && m.SenderId != me && m.RecipientId != me)
        {
            throw new NotFoundException("Message", messageId);
        }

        Dictionary<string, List<Guid>> map;
        try { map = string.IsNullOrWhiteSpace(m.Reactions) ? [] : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(m.Reactions) ?? []; }
        catch (System.Text.Json.JsonException) { map = []; }

        // One reaction per person: choosing a new emoji replaces the old one.
        var had = map.TryGetValue(emoji, out var current) && current.Contains(me);
        foreach (var list in map.Values) list.Remove(me);
        if (!had)
        {
            if (!map.TryGetValue(emoji, out var users)) map[emoji] = users = [];
            users.Add(me);
        }

        m.Reactions = System.Text.Json.JsonSerializer.Serialize(map.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value));
        await db.SaveChangesAsync(ct);
        return ToDto(m, me, null);
    }

    public Task TypingAsync(Guid toUserId) { presence.SetTyping(Me, toUserId); return Task.CompletedTask; }

    public bool IsTyping(Guid otherUserId) => presence.IsTyping(otherUserId, Me);

    public async Task<IReadOnlyList<ChatUserDto>> GetChatUsersAsync(CancellationToken ct = default)
    {
        var me = Me;
        var tenantId = CurrentTenantId;

        var users = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive && u.Id != me)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.JobTitle })
            .ToListAsync(ct);

        var unreadCounts = await db.InternalChatMessages.AsNoTracking()
            .Where(m => m.RecipientId == me && !m.IsRead)
            .GroupBy(m => m.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SenderId, x => x.Count, ct);

        var recent = await db.InternalChatMessages.AsNoTracking()
            .Where(m => m.RecipientId != null && (m.SenderId == me || m.RecipientId == me))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(500)
            .Select(m => new { m.SenderId, m.RecipientId, m.Message, m.CreatedAtUtc })
            .ToListAsync(ct);
        var last = new Dictionary<Guid, (string Text, DateTime At)>();
        foreach (var m in recent)
        {
            var other = m.SenderId == me ? m.RecipientId!.Value : m.SenderId;
            if (!last.ContainsKey(other)) last[other] = (m.SenderId == me ? "You: " + m.Message : m.Message, m.CreatedAtUtc);
        }
        presence.Touch(me);

        return users.Select(u =>
        {
            var fullName = $"{u.FirstName} {u.LastName}".Trim();
            var name = !string.IsNullOrWhiteSpace(fullName) ? fullName : u.Email;
            last.TryGetValue(u.Id, out var lm);
            return new ChatUserDto(
                u.Id,
                name,
                u.Email,
                u.JobTitle ?? "Staff",
                unreadCounts.TryGetValue(u.Id, out var count) ? count : 0,
                presence.IsOnline(u.Id),
                lm.Text is null ? null : (lm.Text.Length <= 80 ? lm.Text : lm.Text[..79] + "…"),
                lm.Text is null ? null : lm.At
            );
        }).OrderByDescending(u => u.LastMessageAtUtc ?? DateTime.MinValue).ThenByDescending(u => u.UnreadCount).ThenBy(u => u.Name).ToList();
    }

    /// <summary>Called by the app every ~30 seconds while it is in the foreground.</summary>
    public Task PingAsync() { presence.Touch(Me); return Task.CompletedTask; }

    public Task GoOfflineAsync() { presence.Clear(Me); return Task.CompletedTask; }

    public async Task MarkReadAsync(Guid? otherUserId, string? channel, CancellationToken ct = default)
    {
        var me = Me;
        if (otherUserId.HasValue)
        {
            var unread = await db.InternalChatMessages
                .Where(m => m.RecipientId == me && m.SenderId == otherUserId.Value && !m.IsRead)
                .ToListAsync(ct);

            foreach (var m in unread)
            {
                m.IsRead = true;
            }
            if (unread.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }
    }
}

