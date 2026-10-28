using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// Phase 2 / #13: Scheduled Backups (scheduled-backups).
/// Service for scheduling, creating, and downloading tenant CRM data backup archives.
/// </summary>
public sealed class BackupService(
    AppDbContext db,
    ITenantContext tenant,
    SettingsVersion version) : IBackupService
{
    private static readonly HashSet<string> ValidFrequencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Daily", "Weekly", "Monthly"
    };

    private static readonly HashSet<string> ValidFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "csv"
    };

    public async Task<BackupScheduleConfigDto> GetScheduleConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await db.BackupScheduleConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config == null)
        {
            config = new BackupScheduleConfig
            {
                IsEnabled = true,
                Frequency = "Weekly",
                DayOfWeek = DayOfWeek.Sunday,
                HourUtc = 2,
                RetentionDays = 30,
                Format = "json",
                NotifyEmails = []
            };

            db.BackupScheduleConfigs.Add(config);
            await db.SaveChangesAsync(cancellationToken);
        }

        return MapSchedule(config);
    }

    public async Task<BackupScheduleConfigDto> SaveScheduleConfigAsync(SaveBackupScheduleConfigRequest request, CancellationToken cancellationToken = default)
    {
        ValidateScheduleRequest(request);

        var config = await db.BackupScheduleConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config == null)
        {
            config = new BackupScheduleConfig();
            db.BackupScheduleConfigs.Add(config);
        }

        config.IsEnabled = request.IsEnabled;
        config.Frequency = request.Frequency.Trim();
        config.DayOfWeek = request.DayOfWeek;
        config.DayOfMonth = request.DayOfMonth;
        config.HourUtc = request.HourUtc;
        config.RetentionDays = request.RetentionDays;
        config.Format = request.Format.Trim().ToLowerInvariant();
        config.NotifyEmails = request.NotifyEmails?.ToArray() ?? [];

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return MapSchedule(config);
    }

    public async Task<IReadOnlyList<BackupArchiveDto>> ListArchivesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.BackupArchiveRecords.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(MapArchive).ToList();
    }

    public async Task<BackupArchiveDto> GetArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.BackupArchiveRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Backup archive", id);

        return MapArchive(row);
    }

    public async Task<BackupArchiveDto> TriggerBackupAsync(TriggerBackupRequest? request = null, CancellationToken cancellationToken = default)
    {
        var format = (request?.Format ?? "json").Trim().ToLowerInvariant();
        if (!ValidFormats.Contains(format))
            throw new ValidationException($"Format '{format}' is not supported. Use 'json' or 'csv'.");

        var config = await db.BackupScheduleConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var retentionDays = config?.RetentionDays ?? 30;

        // Fetch tenant entities
        var leads = await db.Leads.AsNoTracking()
            .Select(l => new { l.Id, l.FirstName, l.LastName, l.Email, l.Phone, l.CompanyName, l.Status, l.CreatedAt })
            .ToListAsync(cancellationToken);

        var contacts = await db.Contacts.AsNoTracking()
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.CreatedAt })
            .ToListAsync(cancellationToken);

        var accounts = await db.Accounts.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.Website, a.Industry, a.Phone, a.CreatedAt })
            .ToListAsync(cancellationToken);

        var deals = await db.Deals.AsNoTracking()
            .Select(d => new { d.Id, d.Name, d.Amount, d.StageId, d.PipelineId, d.ExpectedCloseDate, d.CreatedAt })
            .ToListAsync(cancellationToken);

        var activities = await db.Activities.AsNoTracking()
            .Select(ac => new { ac.Id, ac.Subject, ac.Type, ac.DueAt, ac.Status, ac.CompletedAt, ac.CreatedAt })
            .ToListAsync(cancellationToken);

        var customObjects = await db.CustomObjectRecords.AsNoTracking()
            .Select(cr => new { cr.Id, cr.ObjectDefinitionId, cr.RecordNumber, cr.DisplayName, cr.DataJson, cr.CreatedAt })
            .ToListAsync(cancellationToken);

        var counts = new Dictionary<string, int>
        {
            ["leads"] = leads.Count,
            ["contacts"] = contacts.Count,
            ["accounts"] = accounts.Count,
            ["deals"] = deals.Count,
            ["activities"] = activities.Count,
            ["custom_objects"] = customObjects.Count
        };

        var bundle = new
        {
            tenantId = tenant.TenantId,
            exportedAt = DateTime.UtcNow,
            format,
            recordCounts = counts,
            data = new
            {
                leads,
                contacts,
                accounts,
                deals,
                activities,
                custom_objects = customObjects
            }
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(bundle, new JsonSerializerOptions { WriteIndented = true });
        var checksum = Convert.ToHexStringLower(SHA256.HashData(payloadBytes));
        var payloadString = Encoding.UTF8.GetString(payloadBytes);

        var archive = new BackupArchiveRecord
        {
            Status = "Completed",
            Format = format,
            SizeBytes = payloadBytes.LongLength,
            ChecksumSha256 = checksum,
            RecordCountsJson = JsonSerializer.Serialize(counts),
            PayloadContent = payloadString,
            ExpiresAt = DateTime.UtcNow.AddDays(retentionDays),
            DownloadToken = Guid.NewGuid().ToString("N"),
            DownloadCount = 0
        };

        db.BackupArchiveRecords.Add(archive);

        if (config != null)
        {
            var trackedConfig = await db.BackupScheduleConfigs.FirstOrDefaultAsync(x => x.Id == config.Id, cancellationToken);
            if (trackedConfig != null)
            {
                trackedConfig.LastRunAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return MapArchive(archive);
    }

    public async Task<BackupDownloadResult> DownloadArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.BackupArchiveRecords.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Backup archive", id);

        if (row.ExpiresAt < DateTime.UtcNow)
        {
            throw new ValidationException("This backup archive has expired and can no longer be downloaded.");
        }

        row.DownloadCount++;
        await db.SaveChangesAsync(cancellationToken);

        var payload = row.PayloadContent ?? "{}";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var fileName = $"crm-backup-{row.CreatedAt:yyyyMMdd-HHmmss}.{row.Format}";
        var contentType = row.Format == "json" ? "application/json" : "text/plain";

        return new BackupDownloadResult(fileName, contentType, bytes);
    }

    public async Task DeleteArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.BackupArchiveRecords.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Backup archive", id);

        db.BackupArchiveRecords.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateScheduleRequest(SaveBackupScheduleConfigRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Frequency) || !ValidFrequencies.Contains(request.Frequency))
            throw new ValidationException($"Frequency must be one of: {string.Join(", ", ValidFrequencies)}.");
        if (string.IsNullOrWhiteSpace(request.Format) || !ValidFormats.Contains(request.Format))
            throw new ValidationException($"Format must be one of: {string.Join(", ", ValidFormats)}.");
        if (request.HourUtc is < 0 or > 23)
            throw new ValidationException("HourUtc must be between 0 and 23.");
        if (request.RetentionDays < 1)
            throw new ValidationException("RetentionDays must be at least 1 day.");
    }

    private static BackupScheduleConfigDto MapSchedule(BackupScheduleConfig c)
    {
        return new BackupScheduleConfigDto(
            c.Id,
            c.IsEnabled,
            c.Frequency,
            c.DayOfWeek,
            c.DayOfMonth,
            c.HourUtc,
            c.RetentionDays,
            c.Format,
            c.NotifyEmails,
            c.LastRunAt,
            c.NextRunAt
        );
    }

    private static BackupArchiveDto MapArchive(BackupArchiveRecord a)
    {
        Dictionary<string, int> counts;
        try
        {
            counts = JsonSerializer.Deserialize<Dictionary<string, int>>(a.RecordCountsJson) ?? [];
        }
        catch
        {
            counts = [];
        }

        return new BackupArchiveDto(
            a.Id,
            a.Status,
            a.Format,
            a.SizeBytes,
            a.ChecksumSha256,
            counts,
            a.CreatedAt,
            a.ExpiresAt,
            a.DownloadCount,
            a.ErrorMessage
        );
    }
}
