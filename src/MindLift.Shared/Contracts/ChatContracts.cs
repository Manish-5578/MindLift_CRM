using System;
using System.Collections.Generic;

namespace MindLift.Shared.Contracts;

public sealed record ChatMessageDto(
    Guid Id,
    Guid SenderId,
    string SenderName,
    string? SenderEmail,
    Guid? RecipientId,
    string? ChannelName,
    string Message,
    DateTime CreatedAtUtc,
    bool IsRead
);

public sealed record SendChatMessageRequest(
    string Message,
    string? ChannelName = null,
    Guid? RecipientId = null
);

public sealed record ChatUserDto(
    Guid Id,
    string Name,
    string Email,
    string? Role,
    int UnreadCount,
    bool IsOnline
);

public sealed record ExtractedLeadCardDto(
    string? CompanyName,
    string? FirstName,
    string? LastName,
    string? FullName,
    string? Email,
    string? Phone,
    string? JobTitle,
    string? Website,
    string? Address,
    string RawText
);

public sealed record ParseCardOcrRequest(
    string? RawText,
    string? ImageBase64
);

