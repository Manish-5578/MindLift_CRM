using System;
using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Internal workspace chat message between team members or within workspace channels.
/// Fully tenant-isolated with global tenant filters.
/// </summary>
public class InternalChatMessage : TenantEntity
{
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderEmail { get; set; }
    public Guid? RecipientId { get; set; }
    public string? ChannelName { get; set; } = "general";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }

    /// <summary>The message this one replies to.</summary>
    public Guid? ReplyToId { get; set; }

    /// <summary>Storage key of a photo, file or voice note sent with the message.</summary>
    public string? AttachmentKey { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentContentType { get; set; }

    /// <summary>image, audio or file.</summary>
    public string? AttachmentKind { get; set; }
    public long? AttachmentSize { get; set; }
    public int? AttachmentDurationMs { get; set; }

    /// <summary>JSON object: emoji to the ids of the people who reacted with it.</summary>
    public string? Reactions { get; set; }
}

