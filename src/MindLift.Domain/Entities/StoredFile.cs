using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Metadata for an uploaded file. The bytes live in IFileStorage (local disk in dev, blob storage later).
/// </summary>
[Audited]
public sealed class StoredFile : TenantEntity
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>Storage key relative to the storage root. Never taken from user input (server-generated GUID path).</summary>
    public string StoragePath { get; set; } = string.Empty;

    public string? Sha256 { get; set; }
    public Guid? UploadedById { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}
