namespace MindLift.Shared.Contracts;

/// <summary>An uploaded file as the client sees it. The storage path is never exposed.</summary>
public sealed record StoredFileDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? EntityType,
    Guid? EntityId,
    DateTime CreatedAtUtc);
