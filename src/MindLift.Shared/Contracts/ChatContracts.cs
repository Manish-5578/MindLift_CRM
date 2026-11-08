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
    bool IsOnline,
    string? LastMessage = null,
    DateTime? LastMessageAtUtc = null
);

public sealed record LeadDuplicateCandidateDto(
    Guid Id,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    string RecordType
);

public sealed record CardEvidenceBoxDto(
    string Text,
    double Confidence,
    double X,
    double Y,
    double Width,
    double Height,
    string? Side = null
);

/// <summary>A field the front and back of a card disagree on: both readings, so the person can choose.</summary>
public sealed record CardFieldConflictDto(string Field, string FrontValue, string BackValue);

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
    string RawText,
    double OverallConfidence = 0.9,
    double? CompanyConfidence = null,
    double? NameConfidence = null,
    double? EmailConfidence = null,
    double? PhoneConfidence = null,
    double? TitleConfidence = null,
    string DetectedScript = "Latin",
    LeadDuplicateCandidateDto? Duplicate = null,
    double? EngineConfidence = null,
    IReadOnlyList<CardEvidenceBoxDto>? Evidence = null,
    double? AddressConfidence = null,
    IReadOnlyList<CardFieldConflictDto>? Conflicts = null
);

public sealed record ParseCardOcrRequest(
    string? RawText,
    string? ImageBase64
);

