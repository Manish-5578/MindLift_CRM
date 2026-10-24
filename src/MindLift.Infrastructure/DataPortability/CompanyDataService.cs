using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Setup;
using MindLift.Shared.Contracts;
using Npgsql;

namespace MindLift.Infrastructure.DataPortability;

/// <summary>
/// A company's data as one download, and its permanent removal.
/// The export holds every table that belongs to the company (one JSON file each) and every file it uploaded, so a company can take its data
/// with it, and a backup of one company exists without restoring the whole database. Secrets (password hashes, keys, tokens) are left out.
/// Removal deletes every row and file of the company and cannot be undone; only the platform owner can do it.
/// </summary>
public sealed partial class CompanyDataService(
    AppDbContext db,
    ITenantContext tenant,
    IFileStorage storage,
    IBackgroundJobClient jobs,
    IClock clock,
    INotificationService notifications,
    Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
    MindLift.Infrastructure.Communication.ISecretProtector protector,
    ILogger<CompanyDataService> logger)
{
    /// <summary>How long a finished export can be downloaded.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    /// <summary>Tables kept when a company is removed: the platform's own audit trail records that it happened.</summary>
    private static readonly HashSet<string> KeptOnPurge = ["platform_audit"];

    // ------------------------------------------------------------------ export

    public async Task<DataExportDto> RequestExportAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (await db.DataExports.AnyAsync(e => e.TenantId == tenantId && e.Status == "running", cancellationToken))
        {
            throw new ConflictException("export_running", "An export is already being prepared.");
        }

        var export = new DataExport { TenantId = tenantId, RequestedById = tenant.UserId, Status = "running", CreatedAt = clock.UtcNow };
        db.DataExports.Add(export);
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<CompanyDataService>("imports", s => s.BuildExportAsync(export.Id, CancellationToken.None));
        return Map(export);
    }

    public async Task<IReadOnlyList<DataExportDto>> ExportsAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => (await db.DataExports.AsNoTracking().Where(e => e.TenantId == tenantId).OrderByDescending(e => e.CreatedAt).Take(10).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<(Stream Stream, string FileName)> OpenExportAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default)
    {
        var export = await db.DataExports.AsNoTracking().FirstOrDefaultAsync(e => e.Id == exportId && e.TenantId == tenantId, cancellationToken)
            ?? throw new NotFoundException("Export", exportId);
        if (export.Status != "ready" || export.StorageKey is null || export.ExpiresAt < clock.UtcNow)
        {
            throw new BusinessRuleException("export_not_ready", "This export is not available. Prepare a new one.");
        }

        var slug = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Slug).FirstAsync(cancellationToken);
        return (await storage.OpenReadAsync(export.StorageKey, cancellationToken), $"{slug}-data-{export.CreatedAt:yyyyMMdd-HHmm}.zip");
    }

    /// <summary>
    /// A link that downloads an export without signing in, valid for ten minutes. The browser fetches the file directly instead of through
    /// the app, so even a very large export downloads like any other file.
    /// </summary>
    public async Task<string> DownloadLinkAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken = default)
    {
        var (check, _) = await OpenExportAsync(tenantId, exportId, cancellationToken);   // refuses an export that is not there or not ready
        await check.DisposeAsync();
        var token = protector.Protect($"export|{tenantId:N}|{exportId:N}|{clock.UtcNow.AddMinutes(10).Ticks}")!;
        return $"/downloads/export?t={Uri.EscapeDataString(token)}";
    }

    /// <summary>Opens the export a download link names, or null when the link is wrong or too old.</summary>
    public async Task<(Stream Stream, string FileName)?> OpenByLinkAsync(string token, CancellationToken cancellationToken = default)
    {
        var parts = protector.Unprotect(token)?.Split('|');
        if (parts is not { Length: 4 } || parts[0] != "export" || !Guid.TryParseExact(parts[1], "N", out var tenantId) || !Guid.TryParseExact(parts[2], "N", out var exportId)
            || !long.TryParse(parts[3], out var ticks) || new DateTime(ticks, DateTimeKind.Utc) < clock.UtcNow)
        {
            return null;
        }

        try
        {
            return await OpenExportAsync(tenantId, exportId, cancellationToken);
        }
        catch (Exception ex) when (ex is NotFoundException or BusinessRuleException)
        {
            return null;
        }
    }

    /// <summary>The background job: writes the archive to storage and tells whoever asked for it.</summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task BuildExportAsync(Guid exportId, CancellationToken cancellationToken)
    {
        var export = await db.DataExports.FirstAsync(e => e.Id == exportId, cancellationToken);
        var tenantId = export.TenantId;
        var temp = Path.Combine(Path.GetTempPath(), $"mindlift-export-{exportId:N}.zip");
        try
        {
            var tables = 0;
            var rows = 0L;
            var files = 0;
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var entity in CompanyTables())
                {
                    var count = await WriteTableAsync(zip, entity, tenantId, cancellationToken);
                    tables++;
                    rows += count;
                }

                foreach (var key in await StorageKeysAsync(tenantId, cancellationToken))
                {
                    try
                    {
                        await using var source = await storage.OpenReadAsync(key, cancellationToken);
                        var entry = zip.CreateEntry("files/" + key.Replace('\\', '/'), CompressionLevel.Fastest);
                        await using var target = entry.Open();
                        await source.CopyToAsync(target, cancellationToken);
                        files++;
                    }
                    catch (FileNotFoundException)
                    {
                        // A record whose file is gone: the record is still in the export.
                    }
                }

                var readme = zip.CreateEntry("README.txt");
                await using (var writer = new StreamWriter(readme.Open()))
                {
                    await writer.WriteAsync($"MindLift data export\nCreated (UTC): {clock.UtcNow:yyyy-MM-dd HH:mm}\nTables: {tables}\nRows: {rows}\nFiles: {files}\n\n" +
                        "Each data/*.json file is one table, as a list of rows. Uploaded files are under files/, at the path the rows name.\n" +
                        "Passwords, keys and tokens are not included.\n");
                }
            }

            var storageKey = $"exports/{tenantId:N}/{exportId:N}.zip";
            await using (var upload = File.OpenRead(temp))
            {
                await storage.SaveAsync(storageKey, upload, cancellationToken);
            }

            export.Status = "ready";
            export.StorageKey = storageKey;
            export.SizeBytes = new FileInfo(temp).Length;
            export.CompletedAt = clock.UtcNow;
            export.ExpiresAt = clock.UtcNow + Keep;
            await db.SaveChangesAsync(cancellationToken);

            if (export.RequestedById is { } who && await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == who && u.TenantId == tenantId, cancellationToken))
            {
                using (tenant.Use(tenantId))
                {
                    await notifications.NotifyAsync(new NotificationRequest([who], NotificationTypes.ImportCompleted, "Your data export is ready",
                        $"{rows:N0} records and {files:N0} files. The download is available for {Keep.Days} days.", null, null, "/settings/data", IncludeActor: true, InAppOnly: true), cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Data export {ExportId} failed", exportId);
            export.Status = "failed";
            export.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    /// <summary>Every table with a company id column, the company's own data.</summary>
    private IEnumerable<IEntityType> CompanyTables()
        => db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is { } p && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)) && e.GetTableName() is not null && !e.IsOwned())
            .Where(e => e.GetTableName() is not ("platform_audit" or "number_sequences" or "billing_events"))
            .OrderBy(e => e.GetTableName());

    /// <summary>Writes one table as JSON, a page at a time, leaving out anything that looks like a secret.</summary>
    private async Task<long> WriteTableAsync(ZipArchive zip, IEntityType entity, Guid tenantId, CancellationToken cancellationToken)
    {
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema();
        var columns = entity.GetProperties()
            .Where(p => !Secret().IsMatch(p.Name))
            .Select(p => p.GetColumnName(StoreObjectIdentifier.Table(table, schema)))
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
        var tenantColumn = entity.FindProperty("TenantId")!.GetColumnName(StoreObjectIdentifier.Table(table, schema))!;

        var entry = zip.CreateEntry($"data/{table}.json", CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        json.WriteStartArray();

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var select = string.Join(", ", columns.Select(c => $"\"{c}\""));
        var from = schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
        await using var command = new NpgsqlCommand($"SELECT {select} FROM {from} WHERE \"{tenantColumn}\" = @t", connection);
        command.Parameters.AddWithValue("t", tenantId);
        var rows = 0L;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                json.WriteStartObject();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    json.WritePropertyName(reader.GetName(i));
                    WriteValue(json, reader.IsDBNull(i) ? null : reader.GetValue(i));
                }

                json.WriteEndObject();
                rows++;
                if (rows % 500 == 0)
                {
                    await json.FlushAsync(cancellationToken);
                }
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(cancellationToken);
        return rows;
    }

    private static void WriteValue(Utf8JsonWriter json, object? value)
    {
        switch (value)
        {
            case null: json.WriteNullValue(); break;
            case string s: json.WriteStringValue(s); break;
            case bool b: json.WriteBooleanValue(b); break;
            case Guid g: json.WriteStringValue(g); break;
            case DateTime d: json.WriteStringValue(DateTime.SpecifyKind(d, DateTimeKind.Utc)); break;
            case DateTimeOffset o: json.WriteStringValue(o); break;
            case DateOnly d: json.WriteStringValue(d.ToString("yyyy-MM-dd")); break;
            case int or long or short or decimal or double or float: json.WriteRawValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!); break;
            case string[] list:
                json.WriteStartArray();
                foreach (var item in list) json.WriteStringValue(item);
                json.WriteEndArray();
                break;
            default: json.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)); break;
        }
    }

    private async Task<IReadOnlyList<string>> StorageKeysAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var stored = await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == tenantId).Select(f => f.StoragePath).ToListAsync(cancellationToken);
        var documents = await db.BusinessDocuments.IgnoreQueryFilters().Where(d => d.TenantId == tenantId).Select(d => d.StorageKey).ToListAsync(cancellationToken);
        return stored.Concat(documents).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
    }

    // ------------------------------------------------------------------ permanent removal

    /// <summary>
    /// Removes a company and everything it has, for good. The platform owner must type the company's address (slug) to confirm.
    /// Runs in the background; the platform audit trail keeps a record that it happened.
    /// </summary>
    public async Task RequestPurgeAsync(Guid tenantId, string confirmSlug, string actor, CancellationToken cancellationToken = default)
    {
        var company = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken) ?? throw new NotFoundException("Company", tenantId);
        if (company.Slug == PlatformAdminSeeder.PlatformSlug)
        {
            throw new ForbiddenException("The platform's own workspace cannot be removed.");
        }

        if (!string.Equals(confirmSlug?.Trim(), company.Slug, StringComparison.Ordinal))
        {
            throw new ValidationException("confirm", $"Type {company.Slug} to confirm.");
        }

        // Nobody in the company can sign in from this moment, while the removal runs.
        company.Status = "Cancelled";
        db.PlatformAudit.Add(new PlatformAuditEntry
        {
            TenantId = company.Id, Company = company.Name, ActorId = tenant.UserId, Actor = actor, Action = "company.purged",
            Summary = $"{company.Name} ({company.Slug}) and all its data permanently removed", CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        jobs.Enqueue<CompanyDataService>("imports", s => s.PurgeAsync(tenantId, CancellationToken.None));
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task PurgeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var keys = await StorageKeysAsync(tenantId, cancellationToken);
        var people = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId).Select(u => u.Id).ToListAsync(cancellationToken);
        keys = keys.Concat(await db.DataExports.IgnoreQueryFilters().Where(e => e.TenantId == tenantId && e.StorageKey != null).Select(e => e.StorageKey!).ToListAsync(cancellationToken)).ToList();

        // Tables refer to one another, so a delete can be refused until the rows that point at it are gone. Each pass removes what it can;
        // passes repeat until nothing is left or nothing more can be removed.
        var tables = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("TenantId") is not null && e.GetTableName() is { } name && !KeptOnPurge.Contains(name) && !e.IsOwned())
            .Select(e => (Table: e.GetTableName()!, Schema: e.GetSchema(), Column: e.FindProperty("TenantId")!.GetColumnName(StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema()))!))
            .Distinct()
            .ToList();

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var remaining = tables;
        for (var pass = 0; pass < 10 && remaining.Count > 0; pass++)
        {
            var blocked = new List<(string Table, string? Schema, string Column)>();
            foreach (var t in remaining)
            {
                var from = t.Schema is null ? $"\"{t.Table}\"" : $"\"{t.Schema}\".\"{t.Table}\"";
                try
                {
                    await using var delete = new NpgsqlCommand($"DELETE FROM {from} WHERE \"{t.Column}\" = @t", connection);
                    delete.Parameters.AddWithValue("t", tenantId);
                    delete.CommandTimeout = 300;
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                {
                    blocked.Add(t);
                }
            }

            if (blocked.Count == remaining.Count)
            {
                throw new InvalidOperationException("Could not remove the company: " + string.Join(", ", blocked.Select(b => b.Table)) + " are still referenced.");
            }

            remaining = blocked;
        }

        await using (var tenantRow = new NpgsqlCommand("DELETE FROM \"tenants\" WHERE \"id\" = @t", connection))
        {
            tenantRow.Parameters.AddWithValue("t", tenantId);
            await tenantRow.ExecuteNonQueryAsync(cancellationToken);
        }

        // The rows went with plain SQL, so what was remembered about the people and the company is dropped here: their sessions end at once.
        await cache.RemoveAsync(people.Select(MindLift.Infrastructure.Caching.RequestStateCache.UserKey)
            .Append(MindLift.Infrastructure.Caching.RequestStateCache.TenantKey(tenantId))
            .Append(MindLift.Infrastructure.Caching.RequestStateCache.ModulesKey(tenantId))
            .Append($"platform:entitlements:{tenantId}"), cancellationToken);

        foreach (var key in keys.Distinct())
        {
            try
            {
                await storage.DeleteAsync(key, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                logger.LogWarning(ex, "File {Key} of removed company {TenantId} could not be deleted", key, tenantId);
            }
        }

        logger.LogInformation("Company {TenantId} removed with {Files} files", tenantId, keys.Count);
    }

    private static DataExportDto Map(DataExport e) => new(e.Id, e.Status, e.CreatedAt, e.CompletedAt, e.ExpiresAt, e.SizeBytes);

    [GeneratedRegex("(Password|Secret|Token|Hash|SecurityStamp|ConcurrencyStamp|ApiKey|PrivateKey)", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();
}
