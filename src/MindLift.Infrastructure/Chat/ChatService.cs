using System;
using System.Collections.Generic;
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

public sealed class ChatService(AppDbContext db, ITenantContext tenant, INotificationService notifications, ChatPresence presence, Microsoft.Extensions.Logging.ILogger<ChatService> logger)
{
    private Guid Me => tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedException("no_tenant", "No active tenant.");

    public async Task<IReadOnlyList<ChatMessageDto>> GetMessagesAsync(string? channel, Guid? recipientId, DateTime? since, CancellationToken ct = default)
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

        var list = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        list.Reverse(); // chronological order

        return list.Select(m => new ChatMessageDto(
            m.Id,
            m.SenderId,
            m.SenderName,
            m.SenderEmail,
            m.RecipientId,
            m.ChannelName,
            m.Message,
            m.CreatedAtUtc,
            m.IsRead
        )).ToList();
    }

    public async Task<ChatMessageDto> SendMessageAsync(SendChatMessageRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ValidationException("message_empty", "Chat message cannot be empty.");
        }

        var me = Me;
        var myName = !string.IsNullOrWhiteSpace(tenant.UserName) ? tenant.UserName! : "Colleague";

        var msg = new InternalChatMessage
        {
            Id = Guid.NewGuid(),
            TenantId = CurrentTenantId,
            SenderId = me,
            SenderName = myName,
            SenderEmail = tenant.UserEmail,
            RecipientId = request.RecipientId,
            ChannelName = request.RecipientId == null ? (string.IsNullOrWhiteSpace(request.ChannelName) ? "general" : request.ChannelName.Trim().ToLowerInvariant()) : null,
            Message = request.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false
        };

        db.InternalChatMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        if (msg.RecipientId is Guid recipient)
        {
            try
            {
                // One notification record: it shows in the recipient's Alerts list and is delivered as a push.
                var preview = msg.Message.Length <= 140 ? msg.Message : msg.Message[..139] + "…";
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

        return new ChatMessageDto(
            msg.Id,
            msg.SenderId,
            msg.SenderName,
            msg.SenderEmail,
            msg.RecipientId,
            msg.ChannelName,
            msg.Message,
            msg.CreatedAtUtc,
            msg.IsRead
        );
    }

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

