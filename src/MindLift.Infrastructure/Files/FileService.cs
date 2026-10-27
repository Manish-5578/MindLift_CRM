using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Files;

/// <summary>
/// Uploads, downloads and deletes files (plan 4.9). The client never chooses the storage key, the content type
/// or anything else that decides what is served back: those come from the server.
/// Record permission is checked at the module level. Row-level data scope is added when each module owns its record screens.
/// </summary>
public sealed class FileService(AppDbContext db, IFileStorage storage, ITenantContext tenant, IPermissionService permissions, IDataScopeService scope, IConfiguration configuration)
{
    /// <summary>Allowed extensions and the content type we serve them with.</summary>
    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    private static readonly Dictionary<string, AppModule> EntityModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["leads"] = AppModule.Leads,
        ["contacts"] = AppModule.Contacts,
        ["accounts"] = AppModule.Accounts,
        ["deals"] = AppModule.Deals,
        ["activities"] = AppModule.Activities,
    };

    private long MaxBytes => (configuration.GetValue<long?>("Storage:MaxUploadMegabytes") ?? 25) * 1024 * 1024;

    public async Task<StoredFileDto> UploadAsync(Stream content, string fileName, string? entityType, Guid? entityId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException();

        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
        {
            throw Invalid("This file type is not allowed. Use PDF, JPG, PNG, WEBP, TXT, CSV, DOCX or XLSX.");
        }

        var module = await AuthorizeEntityAsync(entityType, entityId, PermissionAction.Edit, cancellationToken);

        var limit = MaxBytes;
        if (content.CanSeek && content.Length > limit)
        {
            throw Invalid($"The file is larger than {limit / 1024 / 1024} MB.");
        }

        // Read at most MaxBytes + 1 so an oversized upload is rejected without buffering the whole body.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
            {
                throw Invalid($"The file is larger than {limit / 1024 / 1024} MB.");
            }
        }

        if (buffer.Length == 0)
        {
            throw Invalid("The file is empty.");
        }

        // Spans cannot live across an await, so the checks that need one finish here.
        var raw = buffer.GetBuffer();
        var length = (int)buffer.Length;
        if (!SignatureMatches(extension, raw.AsSpan(0, length)))
        {
            throw Invalid("The file content does not match its type.");
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(raw.AsSpan(0, length))).ToLowerInvariant();

        var key = $"{tenantId}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        buffer.Position = 0;
        await storage.SaveAsync(key, buffer, cancellationToken);

        var row = new StoredFile
        {
            FileName = SafeFileName(fileName!),
            ContentType = contentType,
            SizeBytes = buffer.Length,
            StoragePath = key,
            Sha256 = sha256,
            UploadedById = tenant.UserId,
            EntityType = module is null ? null : entityType!.ToLowerInvariant(),
            EntityId = module is null ? null : entityId,
        };
        db.Files.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Map(row);
    }

    public async Task<(StoredFile File, Stream Content)> OpenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken);
        await EnsureCanReadAsync(row, cancellationToken);

        if (!await storage.ExistsAsync(row.StoragePath, cancellationToken))
        {
            throw new NotFoundException("File", id);
        }

        return (row, await storage.OpenReadAsync(row.StoragePath, cancellationToken));
    }

    public async Task<IReadOnlyList<StoredFileDto>> ListForRecordAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        await AuthorizeEntityAsync(entityType, entityId, PermissionAction.View, cancellationToken);
        var type = entityType.ToLowerInvariant();
        var rows = await db.Files.AsNoTracking()
            .Where(f => f.EntityType == type && f.EntityId == entityId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await LoadAsync(id, cancellationToken);
        var isUploader = row.UploadedById is { } uploader && uploader == tenant.UserId;
        var canDelete = row.EntityType is { } type && EntityModules.TryGetValue(type, out var module)
            ? await permissions.HasAsync(module, PermissionAction.Delete, cancellationToken)
            : false;
        if (!isUploader && !canDelete)
        {
            throw new ForbiddenException();
        }

        // Soft delete only. The bytes stay in storage until a retention job removes them.
        db.Files.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<StoredFile> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken) ?? throw new NotFoundException("File", id);

    /// <summary>Throws unless the current user may read every one of these files (used before sending them as email attachments).</summary>
    public async Task EnsureReadableAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        foreach (var id in ids.Distinct())
        {
            await EnsureCanReadAsync(await LoadAsync(id, cancellationToken), cancellationToken);
        }
    }

    private async Task EnsureCanReadAsync(StoredFile row, CancellationToken cancellationToken)
    {
        // An attachment on a record can be read by anyone who can view that record's module.
        if (row.EntityType is { } type && EntityModules.TryGetValue(type, out var module))
        {
            if (!await permissions.HasAsync(module, PermissionAction.View, cancellationToken))
            {
                throw new ForbiddenException();
            }

            return;
        }

        // A file that is not attached to a record can only be read by the person who uploaded it.
        if (row.UploadedById is null || row.UploadedById != tenant.UserId)
        {
            throw new ForbiddenException();
        }
    }

    /// <summary>Checks the entity key, that the record exists in this tenant, and that the user has the action on its module.</summary>
    private async Task<AppModule?> AuthorizeEntityAsync(string? entityType, Guid? entityId, PermissionAction action, CancellationToken cancellationToken)
    {
        if (entityType is null && entityId is null)
        {
            return null;
        }

        if (entityType is null || entityId is null || !EntityModules.TryGetValue(entityType, out var module))
        {
            throw Invalid("Choose a valid record to attach the file to.");
        }

        if (!await permissions.HasAsync(module, action, cancellationToken))
        {
            throw new ForbiddenException();
        }

        var id = entityId.Value;
        var exists = entityType.ToLowerInvariant() switch
        {
            "leads" => await db.Leads.AnyAsync(e => e.Id == id, cancellationToken),
            "contacts" => await db.Contacts.AnyAsync(e => e.Id == id, cancellationToken),
            "accounts" => await db.Accounts.AnyAsync(e => e.Id == id, cancellationToken),
            "deals" => await db.Deals.AnyAsync(e => e.Id == id, cancellationToken),
            _ => await db.Activities.AnyAsync(e => e.Id == id, cancellationToken),
        };
        if (!exists)
        {
            throw new NotFoundException(entityType, id);
        }

        return module;
    }

    /// <summary>Checks the first bytes against the claimed type so a renamed executable is not stored as a PDF.</summary>
    internal static bool SignatureMatches(string extension, ReadOnlySpan<byte> bytes)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                return bytes.Length >= 5 && bytes[..5].SequenceEqual("%PDF-"u8);
            case ".png":
                return bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            case ".jpg":
            case ".jpeg":
                return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            case ".webp":
                return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8);
            case ".bmp":
                return bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D;
            case ".tif":
            case ".tiff":
                // Little-endian "II*\0" or big-endian "MM\0*".
                return bytes.Length >= 4 && (bytes[..4].SequenceEqual("II*\0"u8) || bytes[..4].SequenceEqual("MM\0*"u8));
            case ".docx":
            case ".xlsx":
                return bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
            default:
                // Text and CSV have no signature. They must not contain NUL bytes.
                return !bytes.Contains((byte)0);
        }
    }

    /// <summary>Keeps only the file name, drops any path the client sent, and caps the length.</summary>
    internal static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !char.IsControl(c) && c != '"' && !invalid.Contains(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "file";
        }

        return name.Length > 200 ? name[^200..] : name;
    }

    private static StoredFileDto Map(StoredFile f) => new(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.EntityType, f.EntityId, f.CreatedAt);

    private static ValidationException Invalid(string message)
        => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
