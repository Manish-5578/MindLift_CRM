using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Reporting;

/// <summary>
/// Background job that runs recurring scheduled reports, generates exports (PDF, CSV, Excel),
/// sends them to configured recipients via email, and logs execution.
/// </summary>
public sealed class ReportScheduleJob(
    AppDbContext db,
    IEmailSender emailSender,
    IReportExecutor executor,
    ReportSqlBuilder sqlBuilder,
    ISystemReportRegistry systemReportRegistry,
    ILogger<ReportScheduleJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task RunAsync(Guid tenantId, Guid scheduleId, CancellationToken ct = default)
    {
        logger.LogInformation("Executing ReportScheduleJob for tenant {TenantId}, schedule {ScheduleId}", tenantId, scheduleId);

        var schedule = await db.ReportSchedules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.TenantId == tenantId && !s.IsDeleted, ct);

        if (schedule == null || !schedule.IsActive)
        {
            logger.LogWarning("ReportSchedule {ScheduleId} not found or inactive, skipping execution.", scheduleId);
            return;
        }

        var workspace = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => new { t.Status, t.TrialEndsAt }).FirstOrDefaultAsync(ct);
        if (workspace is null || MindLift.Infrastructure.Tenancy.TenantAccess.IsLapsed(workspace.Status, workspace.TrialEndsAt, DateTime.UtcNow))
        {
            logger.LogInformation("Skipping report schedule {ScheduleId}: the workspace has lapsed.", scheduleId);
            return;
        }

        var report = await db.ReportDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == schedule.ReportId && r.TenantId == tenantId && !r.IsDeleted, ct);

        if (report == null)
        {
            logger.LogWarning("Report {ReportId} not found for schedule {ScheduleId}, skipping.", schedule.ReportId, scheduleId);
            return;
        }

        // Deserialize recipients
        List<string> recipients = [];
        try
        {
            recipients = JsonSerializer.Deserialize<List<string>>(schedule.RecipientsJson.RootElement.GetRawText()) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize recipients for schedule {ScheduleId}", scheduleId);
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("No recipients found for schedule {ScheduleId}.", scheduleId);
            return;
        }

        // Execute report
        ReportResult result;
        if (report.IsSystem && !string.IsNullOrWhiteSpace(report.SystemKey))
        {
            var sys = systemReportRegistry.Get(report.SystemKey);
            if (sys == null)
            {
                logger.LogError("System report {SystemKey} not found in registry.", report.SystemKey);
                return;
            }

            var ctx = new SystemReportContext(
                tenantId,
                [],
                TimeZoneInfo.Utc,
                DateRangeResolver.Resolve(DatePreset.ThisMonth, DateTime.UtcNow, TimeZoneInfo.Utc),
                new Dictionary<string, string>());

            result = await sys.RunAsync(ctx, ct);
        }
        else
        {
            ReportQuery query;
            try
            {
                query = JsonSerializer.Deserialize<ReportQuery>(report.QueryJson.RootElement.GetRawText()) ?? new();
            }
            catch
            {
                query = new ReportQuery();
            }

            var built = sqlBuilder.Build(query, tenantId, [], TimeZoneInfo.Utc);
            result = await executor.ExecuteAsync(built, query, ct);
        }

        // Format export
        var format = string.IsNullOrWhiteSpace(schedule.Format) ? "pdf" : schedule.Format.ToLowerInvariant();
        byte[] fileBytes;
        string contentType;
        string ext;

        switch (format)
        {
            case "csv":
                fileBytes = ExportCsv(result);
                contentType = "text/csv";
                ext = "csv";
                break;
            case "excel":
                fileBytes = ExportExcelXml(result, report.Name);
                contentType = "application/vnd.ms-excel";
                ext = "xml";
                break;
            case "pdf":
            default:
                fileBytes = ReportPdfExporter.ExportPdf(result, report.Name);
                contentType = "application/pdf";
                ext = "pdf";
                break;
        }

        var fileName = $"{report.Name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd}.{ext}";
        var attachment = new EmailAttachment(fileName, contentType, fileBytes);

        var subject = $"[MindLift Report] {report.Name} - {DateTime.UtcNow:MMM dd, yyyy}";
        var body = $"""
            Hello,

            Here is your scheduled report: {report.Name}.

            Report Summary:
            - Generated At: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
            - Total Records: {result.RowCount:N0}
            - Execution Time: {result.ExecutionMs} ms

            The full export ({ext.ToUpperInvariant()}) is attached to this email.

            Best regards,
            MindLift CRM Analytics
            """;

        // Send to each recipient
        foreach (var recipient in recipients)
        {
            try
            {
                await emailSender.SendAsync(recipient, subject, body, [attachment], ct);
                logger.LogInformation("Successfully sent report {ReportName} to {Recipient}", report.Name, recipient);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to email report {ReportName} to {Recipient}", report.Name, recipient);
            }
        }

        // Log execution
        schedule.LastRunAt = DateTime.UtcNow;

        db.ReportExecutionLogs.Add(new ReportExecutionLog
        {
            TenantId = tenantId,
            ReportName = report.Name,
            Source = report.Source,
            UserId = Guid.Empty, // Automated system job
            ExecutionMs = result.ExecutionMs,
            RowCount = result.RowCount,
            ExecutedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    private static byte[] ExportCsv(ReportResult result)
    {
        var sb = new System.Text.StringBuilder();
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };

        var headers = result.Columns.Select(c => EscapeCsv(c.Label)).ToArray();
        sb.AppendLine(string.Join(",", headers));

        foreach (var row in result.Rows)
        {
            var values = result.Columns.Select(c => EscapeCsv(row.TryGetValue(c.Key, out var v) ? v?.ToString() : "")).ToArray();
            sb.AppendLine(string.Join(",", values));
        }

        if (result.Totals != null)
        {
            var totalValues = result.Columns.Select(c => EscapeCsv(result.Totals.TryGetValue(c.Key, out var v) ? v?.ToString() : "")).ToArray();
            sb.AppendLine(string.Join(",", totalValues));
        }

        var textBytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        var fullBytes = new byte[bom.Length + textBytes.Length];
        Buffer.BlockCopy(bom, 0, fullBytes, 0, bom.Length);
        Buffer.BlockCopy(textBytes, 0, fullBytes, bom.Length, textBytes.Length);
        return fullBytes;
    }

    private static string EscapeCsv(string? val)
    {
        if (string.IsNullOrEmpty(val)) return "\"\"";
        return $"\"{val.Replace("\"", "\"\"")}\"";
    }

    private static byte[] ExportExcelXml(ReportResult result, string title)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<?mso-application progid=\"Excel.Sheet\"?>");
        sb.AppendLine("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\"");
        sb.AppendLine(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        sb.AppendLine(" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"");
        sb.AppendLine(" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">");
        sb.AppendLine("<Styles>");
        sb.AppendLine(" <Style ss:ID=\"Default\" ss:Name=\"Normal\"><Alignment ss:Vertical=\"Center\"/></Style>");
        sb.AppendLine(" <Style ss:ID=\"Header\"><Font ss:Bold=\"1\" ss:Color=\"#FFFFFF\"/><Interior ss:Color=\"#4F46E5\" ss:Pattern=\"Solid\"/></Style>");
        sb.AppendLine("</Styles>");
        sb.AppendLine("<Worksheet ss:Name=\"Report Data\"><Table>");
        sb.AppendLine(" <Row>");
        foreach (var col in result.Columns)
            sb.AppendLine($"  <Cell ss:StyleID=\"Header\"><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(col.Label)}</Data></Cell>");
        sb.AppendLine(" </Row>");

        foreach (var row in result.Rows)
        {
            sb.AppendLine(" <Row>");
            foreach (var col in result.Columns)
            {
                var val = row.TryGetValue(col.Key, out var v) ? v?.ToString() ?? "" : "";
                sb.AppendLine($"  <Cell><Data ss:Type=\"String\">{System.Security.SecurityElement.Escape(val)}</Data></Cell>");
            }
            sb.AppendLine(" </Row>");
        }
        sb.AppendLine("</Table></Worksheet></Workbook>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }
}
