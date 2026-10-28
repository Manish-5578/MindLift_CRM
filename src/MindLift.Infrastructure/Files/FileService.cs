using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
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

    private static readonly SemaphoreSlim GlobalUploadThrottle = new(10, 10);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> TenantActiveUploads = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, long> TenantInFlightBytes = new();
    private const int MaxConcurrentUploadsPerTenant = 3;

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

        if (!await GlobalUploadThrottle.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken))
        {
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Server upload capacity is currently busy. Please retry shortly."] });
        }

        var activeTenantUploads = TenantActiveUploads.AddOrUpdate(tenantId, 1, (_, c) => c + 1);
        if (activeTenantUploads > MaxConcurrentUploadsPerTenant)
        {
            TenantActiveUploads.AddOrUpdate(tenantId, 0, (_, c) => Math.Max(0, c - 1));
            GlobalUploadThrottle.Release();
            throw new ValidationException(new Dictionary<string, string[]> { ["file"] = ["Too many concurrent uploads for this workspace. Please wait for in-progress uploads to finish."] });
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), $"mindlift_upload_{Guid.NewGuid():N}.tmp");
        long allocatedBytes = 0;

        try
        {
            var maxTenantBytes = (configuration.GetValue<long?>("Storage:MaxTenantStorageMegabytes") ?? 500) * 1024 * 1024;
            var currentTenantUsage = await db.Files.IgnoreQueryFilters().AsNoTracking()
                .Where(f => f.TenantId == tenantId)
                .SumAsync(f => (long)f.SizeBytes, cancellationToken);

            var inFlightBytes = TenantInFlightBytes.GetValueOrDefault(tenantId, 0L);
            if (currentTenantUsage + inFlightBytes + (content.CanSeek ? content.Length : 0) > maxTenantBytes)
            {
                throw Invalid($"Tenant storage quota of {maxTenantBytes / 1024 / 1024} MB exceeded.");
            }

            var limit = MaxBytes;
            if (content.CanSeek && content.Length > limit)
            {
                throw Invalid($"The file is larger than {limit / 1024 / 1024} MB.");
            }

            byte[] headerBuffer = new byte[512];
            int headerBytesRead = 0;
            long totalBytes = 0;

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose))
            {
                var chunk = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    totalBytes += read;
                    allocatedBytes += read;
                    TenantInFlightBytes.AddOrUpdate(tenantId, read, (_, current) => current + read);

                    if (totalBytes > limit)
                    {
                        throw Invalid($"The file is larger than {limit / 1024 / 1024} MB.");
                    }

                    if (currentTenantUsage + TenantInFlightBytes.GetValueOrDefault(tenantId, 0L) > maxTenantBytes)
                    {
                        throw Invalid($"Tenant storage quota of {maxTenantBytes / 1024 / 1024} MB exceeded.");
                    }

                    if (headerBytesRead < headerBuffer.Length)
                    {
                        var copyCount = Math.Min(read, headerBuffer.Length - headerBytesRead);
                        Array.Copy(chunk, 0, headerBuffer, headerBytesRead, copyCount);
                        headerBytesRead += copyCount;
                    }

                    hasher.AppendData(chunk, 0, read);
                    await fileStream.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
                }

                if (totalBytes == 0)
                {
                    throw Invalid("The file is empty.");
                }

                if (!SignatureMatches(extension, headerBuffer.AsSpan(0, headerBytesRead)))
                {
                    throw Invalid("The file content does not match its type.");
                }

                var sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                var key = $"{tenantId}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

                fileStream.Position = 0;
                await storage.SaveAsync(key, fileStream, cancellationToken);

                var row = new StoredFile
                {
                    FileName = SafeFileName(fileName!),
                    ContentType = contentType,
                    SizeBytes = totalBytes,
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
        }
        finally
        {
            if (allocatedBytes > 0)
            {
                TenantInFlightBytes.AddOrUpdate(tenantId, 0, (_, current) => Math.Max(0, current - allocatedBytes));
            }
            TenantActiveUploads.AddOrUpdate(tenantId, 0, (_, current) => Math.Max(0, current - 1));
            GlobalUploadThrottle.Release();
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch { }
        }
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
        if (row.EntityType is { } type && EntityModules.TryGetValue(type, out var module))
        {
            if (!await permissions.HasAsync(module, PermissionAction.Delete, cancellationToken))
            {
                throw new ForbiddenException();
            }

            if (row.EntityId is { } entityId)
            {
                var inScope = await IsEntityInScopeAsync(type, entityId, cancellationToken);
                if (!inScope)
                {
                    throw new NotFoundException(type, entityId);
                }
            }
        }
        else
        {
            if (row.UploadedById is null || row.UploadedById != tenant.UserId)
            {
                throw new ForbiddenException();
            }
        }

        try
        {
            await storage.DeleteAsync(row.StoragePath, cancellationToken);
        }
        catch
        {
            // Storage delete best-effort if backing object is already removed
        }

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

    private async Task<bool> IsEntityInScopeAsync(string type, Guid entityId, CancellationToken cancellationToken)
    {
        return type.ToLowerInvariant() switch
        {
            "leads" => await (await RecordHelpers.InScopeAsync(db.Leads.AsNoTracking(), scope, AppModule.Leads, x => x.OwnerId, cancellationToken)).AnyAsync(x => x.Id == entityId, cancellationToken),
            "contacts" => await (await RecordHelpers.InScopeAsync(db.Contacts.AsNoTracking(), scope, AppModule.Contacts, x => x.OwnerId, cancellationToken)).AnyAsync(x => x.Id == entityId, cancellationToken),
            "accounts" => await (await RecordHelpers.InScopeAsync(db.Accounts.AsNoTracking(), scope, AppModule.Accounts, x => x.OwnerId, cancellationToken)).AnyAsync(x => x.Id == entityId, cancellationToken),
            "deals" => await (await RecordHelpers.InScopeAsync(db.Deals.AsNoTracking(), scope, AppModule.Deals, x => x.OwnerId, cancellationToken)).AnyAsync(x => x.Id == entityId, cancellationToken),
            _ => await (await RecordHelpers.InScopeAsync(db.Activities.AsNoTracking(), scope, AppModule.Activities, x => x.OwnerId, cancellationToken)).AnyAsync(x => x.Id == entityId, cancellationToken),
        };
    }

    private async Task EnsureCanReadAsync(StoredFile row, CancellationToken cancellationToken)
    {
        // An attachment on a record can be read by anyone who can view that record's module and has row scope.
        if (row.EntityType is { } type && EntityModules.TryGetValue(type, out var module))
        {
            if (!await permissions.HasAsync(module, PermissionAction.View, cancellationToken))
            {
                throw new ForbiddenException();
            }

            if (row.EntityId is { } entityId)
            {
                var inScope = await IsEntityInScopeAsync(type, entityId, cancellationToken);
                if (!inScope)
                {
                    throw new NotFoundException(type, entityId);
                }
            }

            return;
        }

        // A file that is not attached to a record can only be read by the person who uploaded it.
        if (row.UploadedById is null || row.UploadedById != tenant.UserId)
        {
            throw new ForbiddenException();
        }
    }

    /// <summary>Checks the entity key, that the record exists and is in the user's scope, and that the user has the action on its module.</summary>
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
        var inScope = await IsEntityInScopeAsync(entityType, id, cancellationToken);
        if (!inScope)
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
