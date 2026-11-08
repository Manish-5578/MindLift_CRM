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
}

