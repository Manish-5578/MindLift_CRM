using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Infrastructure.Documents.Extraction;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Ocr;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Cpq;

public sealed class DocumentOcrService(
    IAppDbContext db,
    ITenantContext tenant,
    IFileStorage fileStorage,
    IOcrEngine ocrEngine,
    INotificationService notifications,
    DocumentPageReader pageReader,
    ILogger<DocumentOcrService> logger) : IDocumentOcrService
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    /// <summary>Copies the stored file to a temporary path that keeps its real extension, so the reader knows whether it is a PDF or an image.</summary>
    private async Task<string> SaveTempCopyAsync(BusinessDocument doc, CancellationToken ct)
    {
        var extension = Path.GetExtension(doc.FileName ?? string.Empty).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = doc.ContentType switch { "application/pdf" => ".pdf", "image/png" => ".png", "image/jpeg" => ".jpg", _ => ".bin" };
        }

        var path = Path.Combine(Path.GetTempPath(), $"doc_{Guid.NewGuid():N}{extension}");
        await using var source = await fileStorage.OpenReadAsync(doc.StorageKey, ct);
        await using var target = File.Create(path);
        await source.CopyToAsync(target, ct);
        return path;
    }

    /// <summary>The file types a document can be, with the content type each is stored and served as.</summary>
    private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.Ordinal)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".bmp"] = "image/bmp",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".md"] = "text/markdown",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>A record a document is linked to must be this company's own. The database only checks that an id exists somewhere, so another company's id would otherwise be accepted.</summary>
    private async Task EnsureLinkedRecordsExistAsync(Guid? accountId, Guid? dealId, Guid? quoteId, Guid? invoiceId, Guid? contactId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (accountId is { } account && !await db.Set<Account>().AnyAsync(x => x.Id == account && !x.IsDeleted, ct)) errors["accountId"] = ["Choose an existing account."];
        if (dealId is { } deal && !await db.Set<Deal>().AnyAsync(x => x.Id == deal && !x.IsDeleted, ct)) errors["dealId"] = ["Choose an existing deal."];
        if (quoteId is { } quote && !await db.Set<Quote>().AnyAsync(x => x.Id == quote && !x.IsDeleted, ct)) errors["quoteId"] = ["Choose an existing quote."];
        if (invoiceId is { } invoice && !await db.Set<Invoice>().AnyAsync(x => x.Id == invoice && !x.IsDeleted, ct)) errors["invoiceId"] = ["Choose an existing invoice."];
        if (contactId is { } contact && !await db.Set<Contact>().AnyAsync(x => x.Id == contact && !x.IsDeleted, ct)) errors["contactId"] = ["Choose an existing contact."];

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    public async Task<BusinessDocumentDto> UploadDocumentAsync(
        string fileName,
        string contentType,
        long fileSize,
        byte[] fileBytes,
        string category,
        Guid? accountId = null,
        Guid? dealId = null,
        Guid? quoteId = null,
        Guid? invoiceId = null,
        Guid? contactId = null,
        bool sharedWithPortal = false,
        CancellationToken ct = default)
    {
        // Only the kinds of file a business document can be are accepted, and the stored type comes from the extension, not from what the sender claims.
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedContentTypes.TryGetValue(extension, out var storedContentType))
        {
            throw new ValidationException("File", "This file type is not allowed. Use PDF, JPG, PNG, WEBP, TIF, BMP, TXT, CSV, MD, DOCX or XLSX.");
        }

        // A renamed file (for example an executable called scan.png) must not be stored as the type it claims to be.
        if (!MindLift.Infrastructure.Files.FileService.SignatureMatches(extension, fileBytes))
        {
            throw new ValidationException("File", "The file content does not match its type.");
        }

        await EnsureLinkedRecordsExistAsync(accountId, dealId, quoteId, invoiceId, contactId, ct);

        var storageKey = $"docs/{CurrentTenantId}/{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        using var stream = new MemoryStream(fileBytes);
        await fileStorage.SaveAsync(storageKey, stream, ct);

        var doc = new BusinessDocument
        {
            TenantId = CurrentTenantId,
            FileName = fileName,
            ContentType = storedContentType,
            FileSizeBytes = fileSize,
            StorageKey = storageKey,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            AssociatedAccountId = accountId,
            AssociatedDealId = dealId,
            AssociatedQuoteId = quoteId,
            AssociatedInvoiceId = invoiceId,
            AssociatedContactId = contactId,
            SharedWithPortal = sharedWithPortal,
            Status = "Queued",
            OcrStatus = (int)DocumentOcrStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        db.Set<BusinessDocument>().Add(doc);
        await db.SaveChangesAsync(ct);

        return MapDocument(doc);
    }

    public async Task<OcrResultDto> ProcessOcrAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", documentId);

        doc.OcrStatus = (int)DocumentOcrStatus.Processing;
        doc.Status = "Processing";
        await db.SaveChangesAsync(ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Read every page: PDF text layer where there is one, OCR for scans and photos.
        IReadOnlyList<PageText> pages = [];
        string rawText = string.Empty;
        try
        {
            pages = await pageReader.ReadAsync(await SaveTempCopyAsync(doc, ct), ct);
            rawText = string.Join("\n", pages.Select(p => p.Text));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read document {DocId}", documentId);
        }

        bool isBinaryOrEmpty = string.IsNullOrWhiteSpace(rawText);

        // No readable text: the document is marked Failed. Nothing is invented, so no made-up values can reach a record.
        if (isBinaryOrEmpty)
        {
            doc.OcrStatus = (int)DocumentOcrStatus.Failed;
            doc.Status = "Failed";
            doc.OcrRawText = null;
            doc.OcrStructuredDataJson = "[]";
            await db.SaveChangesAsync(ct);
            return new OcrResultDto
            {
                DocumentId = doc.Id,
                Status = DocumentOcrStatus.Failed,
                DocumentType = doc.Category,
                ExtractedText = string.Empty,
                ExtractedFields = [],
                ProcessingDuration = sw.Elapsed,
            };
        }

        // 3. Normalized and structured field extraction
        var extractedFields = new List<OcrExtractedFieldDto>();
        string detectedCategory = doc.Category;
        double overallConfidence = 0.90;
        string? vendor = null;
        string? invoiceNum = null;
        decimal? total = null;
        DateTime? detectedDate = null;
        DateOnly? docExpiry = null;
        DateOnly? docIssue = null;

        // A. Check for Passport MRZ
        var mrz = MrzParser.Parse(rawText);
        if (mrz != null)
        {
            detectedCategory = "Passport";
            overallConfidence = mrz.Confidence;
            vendor = $"{mrz.Surname} {mrz.GivenNames}".Trim();
            invoiceNum = mrz.DocumentNumber;
            docExpiry = mrz.ExpiryDate;
            docIssue = mrz.DateOfBirth;

            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "DocumentNumber", Value = mrz.DocumentNumber, Confidence = mrz.Confidence });
            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Surname", Value = mrz.Surname, Confidence = mrz.Confidence });
            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "GivenNames", Value = mrz.GivenNames, Confidence = mrz.Confidence });
            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Nationality", Value = mrz.Nationality, Confidence = mrz.Confidence });
            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Sex", Value = mrz.Sex, Confidence = mrz.Confidence });
            if (mrz.DateOfBirth.HasValue)
            {
                extractedFields.Add(new OcrExtractedFieldDto { FieldName = "DateOfBirth", Value = mrz.DateOfBirth.Value.ToString("yyyy-MM-dd"), Confidence = mrz.Confidence });
            }
            if (mrz.ExpiryDate.HasValue)
            {
                extractedFields.Add(new OcrExtractedFieldDto { FieldName = "ExpiryDate", Value = mrz.ExpiryDate.Value.ToString("yyyy-MM-dd"), Confidence = mrz.Confidence });
            }
        }
        else
        {
            var normalizedText = TextNormalizer.NormalizeDigitsAndPunctuation(rawText);

            // B. Check for Trade License
            var isTradeLicense = Regex.IsMatch(normalizedText, @"(?:Trade\s*License|Commercial\s*License|رخصة\s*تجارية|License\s*No)", RegexOptions.IgnoreCase);
            if (isTradeLicense)
            {
                detectedCategory = "TradeLicense";
                overallConfidence = 0.95;

                var licMatch = Regex.Match(normalizedText, @"(?:License\s*(?:No|#)|Licence\s*(?:No|#)|رقم\s*الرخصة)[\s:]*([A-Za-z0-9\-/]+)", RegexOptions.IgnoreCase);
                if (licMatch.Success)
                {
                    invoiceNum = licMatch.Groups[1].Value.Trim();
                    extractedFields.Add(new OcrExtractedFieldDto { FieldName = "LicenseNumber", Value = invoiceNum, Confidence = 0.98 });
                }

                var nameMatch = Regex.Match(normalizedText, @"(?:Trade\s*Name|Company\s*Name|الاسم\s*التجاري)[\s:]*([^\r\n,]+)", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    vendor = nameMatch.Groups[1].Value.Trim();
                    extractedFields.Add(new OcrExtractedFieldDto { FieldName = "CompanyName", Value = vendor, Confidence = 0.95 });
                }

                var expMatch = Regex.Match(normalizedText, @"(?:Expiry\s*Date|Valid\s*Until|تاريخ\s*الانتهاء)[\s:]*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (expMatch.Success)
                {
                    docExpiry = DateNormalizer.Parse(expMatch.Groups[1].Value);
                    if (docExpiry.HasValue)
                    {
                        extractedFields.Add(new OcrExtractedFieldDto { FieldName = "ExpiryDate", Value = docExpiry.Value.ToString("yyyy-MM-dd"), Confidence = 0.95 });
                    }
                }

                var issueMatch = Regex.Match(normalizedText, @"(?:Issue\s*Date|تاريخ\s*الإصدار)[\s:]*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (issueMatch.Success)
                {
                    docIssue = DateNormalizer.Parse(issueMatch.Groups[1].Value);
                    if (docIssue.HasValue)
                    {
                        extractedFields.Add(new OcrExtractedFieldDto { FieldName = "IssueDate", Value = docIssue.Value.ToString("yyyy-MM-dd"), Confidence = 0.92 });
                    }
                }
            }
            else
            {
                // C. Check for Emirates ID
                var eidMatch = Regex.Match(normalizedText, @"(784[-\s]?\d{4}[-\s]?\d{7}[-\s]?\d)");
                if (eidMatch.Success)
                {
                    detectedCategory = "Identity";
                    invoiceNum = eidMatch.Groups[1].Value.Replace(" ", "-").Trim();
                    extractedFields.Add(new OcrExtractedFieldDto { FieldName = "IdNumber", Value = invoiceNum, Confidence = 0.99 });

                    var expMatch = Regex.Match(normalizedText, @"(?:Expiry|Expires|تاريخ\s*الانتهاء)[\s:]*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (expMatch.Success)
                    {
                        docExpiry = DateNormalizer.Parse(expMatch.Groups[1].Value);
                        if (docExpiry.HasValue)
                        {
                            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "ExpiryDate", Value = docExpiry.Value.ToString("yyyy-MM-dd"), Confidence = 0.95 });
                        }
                    }
                }
                else
                {
                    // D. Standard Invoice / Receipt extraction
                    var vendorMatch = Regex.Match(normalizedText, @"(?:Vendor|Company|Billed From|From):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (vendorMatch.Success)
                    {
                        vendor = vendorMatch.Groups[1].Value.Trim();
                        extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Vendor", Value = vendor, Confidence = 0.95 });
                    }

                    var invMatch = Regex.Match(normalizedText, @"(?:Invoice\s*#?|INV\s*#?|Bill\s*#?):\s*([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase);
                    if (invMatch.Success)
                    {
                        invoiceNum = invMatch.Groups[1].Value.Trim();
                        extractedFields.Add(new OcrExtractedFieldDto { FieldName = "InvoiceNumber", Value = invoiceNum, Confidence = 0.98 });
                    }

                    var totalMatch = Regex.Match(normalizedText, @"(?:Total\s*Amount\s*(?:Due)?|Grand\s*Total|Total\s*Due|Total)[\s:]*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (totalMatch.Success)
                    {
                        var (parsedTotal, curr) = AmountNormalizer.Parse(totalMatch.Groups[1].Value);
                        if (parsedTotal.HasValue)
                        {
                            total = parsedTotal.Value;
                            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Total", Value = total.Value.ToString("F2", CultureInfo.InvariantCulture), Confidence = 0.99 });
                        }
                    }
                    else
                    {
                        var (parsedTotal, curr) = AmountNormalizer.Parse(normalizedText);
                        if (parsedTotal.HasValue)
                        {
                            total = parsedTotal.Value;
                            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Total", Value = total.Value.ToString("F2", CultureInfo.InvariantCulture), Confidence = 0.99 });
                        }
                    }

                    var dateMatch = Regex.Match(normalizedText, @"(?:Date|Invoice\s*Date):\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                    if (dateMatch.Success)
                    {
                        var parsedD = DateNormalizer.Parse(dateMatch.Groups[1].Value);
                        if (parsedD.HasValue)
                        {
                            docIssue = parsedD;
                            detectedDate = DateTime.SpecifyKind(parsedD.Value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                            extractedFields.Add(new OcrExtractedFieldDto { FieldName = "Date", Value = parsedD.Value.ToString("yyyy-MM-dd"), Confidence = 0.92 });
                        }
                    }
                }
            }
        }

        sw.Stop();

        // 4. Update and persist document
        // B2. The workspace's own document types (Settings > Document types). The best match sets the category and adds its fields.
        var customTypes = await db.Set<DocumentTypeDefinition>().AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        DocumentTypeDefinition? bestType = null;
        var bestScore = 0;
        foreach (var candidate in customTypes)
        {
            var score = CustomDocumentExtractor.Score(CustomDocumentExtractor.Keywords(candidate.KeywordsJson), rawText);
            if (score >= candidate.MinScore && score > bestScore)
            {
                bestType = candidate;
                bestScore = score;
            }
        }

        if (bestType is not null)
        {
            detectedCategory = bestType.Key;
            var read = CustomDocumentExtractor.Extract(CustomDocumentExtractor.Fields(bestType.FieldsJson), rawText);
            foreach (var field in read.Where(f => f.Found && f.Value is not null))
            {
                extractedFields.Add(new OcrExtractedFieldDto { FieldName = field.Key, Value = field.Value!, Confidence = field.Confidence / 100.0 });
                if (field.Key == "expiry_date" && DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) docExpiry = expiry;
                if (field.Key == "issue_date" && DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issue)) docIssue = issue;
            }
        }

        doc.Category = detectedCategory;
        doc.OcrStatus = (int)DocumentOcrStatus.Completed;
        doc.Status = "NeedsReview";
        doc.OcrRawText = rawText;
        doc.DetectedVendor = vendor;
        doc.DetectedInvoiceNumber = invoiceNum;
        doc.DetectedTotal = total;
        doc.DetectedDate = detectedDate;
        doc.ExpiryDate = docExpiry;
        doc.IssueDate = docIssue;
        doc.Confidence = overallConfidence;
        doc.OcrStructuredDataJson = JsonSerializer.Serialize(extractedFields);

        await db.SaveChangesAsync(ct);

        return new OcrResultDto
        {
            DocumentId = doc.Id,
            Status = DocumentOcrStatus.Completed,
            DocumentType = doc.Category,
            ExtractedText = rawText,
            ExtractedFields = extractedFields,
            DetectedVendor = vendor,
            DetectedInvoiceNumber = invoiceNum,
            DetectedTotal = total,
            DetectedDate = detectedDate,
            ProcessingDuration = sw.Elapsed
        };
    }

    public async Task<BusinessDocumentDto> ReviewDocumentAsync(Guid id, ReviewDocumentRequest request, Guid reviewerUserId, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", id);

        doc.ReviewedById = reviewerUserId;
        doc.ReviewedAt = DateTime.UtcNow;

        if (string.Equals(request.Action, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            doc.Status = "Approved";
            doc.RejectReason = null;

            // Apply fields to target records if requested
            if (request.ApplyFields is { Count: > 0 })
            {
                foreach (var apply in request.ApplyFields)
                {
                    if (apply.TargetField.StartsWith("accounts.", StringComparison.OrdinalIgnoreCase) && doc.AssociatedAccountId.HasValue)
                    {
                        var account = await db.Set<Account>().FirstOrDefaultAsync(a => a.Id == doc.AssociatedAccountId.Value && !a.IsDeleted, ct);
                        if (account != null)
                        {
                            if (apply.TargetField.EndsWith("trade_license_number", StringComparison.OrdinalIgnoreCase))
                            {
                                // Store license number in account custom field or notes if needed
                                account.Description = (account.Description ?? "") + $" [License: {apply.Value}]";
                            }
                        }
                    }
                    else if (apply.TargetField.StartsWith("contacts.", StringComparison.OrdinalIgnoreCase) && doc.AssociatedContactId.HasValue)
                    {
                        var contact = await db.Set<Contact>().FirstOrDefaultAsync(c => c.Id == doc.AssociatedContactId.Value && !c.IsDeleted, ct);
                        if (contact != null)
                        {
                            contact.Description = (contact.Description ?? "") + $" [{apply.FieldName}: {apply.Value}]";
                        }
                    }
                }
            }

            // If document was uploaded for a document request, mark it completed
            var req = await db.Set<DocumentRequest>().FirstOrDefaultAsync(r => r.ContactId == doc.AssociatedContactId && r.Status == "Submitted", ct);
            if (req != null)
            {
                req.Status = "Completed";
            }
        }
        else if (string.Equals(request.Action, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            doc.Status = "Rejected";
            doc.RejectReason = request.Reason ?? "Document rejected during review";

            // If there was a request, set it back to open so customer can re-upload
            var req = await db.Set<DocumentRequest>().FirstOrDefaultAsync(r => r.ContactId == doc.AssociatedContactId && r.Status == "Submitted", ct);
            if (req != null)
            {
                req.Status = "Open";
            }
        }
        else
        {
            throw new ValidationException("Action", "Review action must be 'Approve' or 'Reject'.");
        }

        await db.SaveChangesAsync(ct);
        return MapDocument(doc);
    }

    public async Task<bool> UpdateExtractedFieldsAsync(Guid id, UpdateExtractedFieldsRequest request, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", id);

        doc.OcrStructuredDataJson = JsonSerializer.Serialize(request.Fields);

        // Check if any standard fields were edited
        foreach (var f in request.Fields)
        {
            if (string.Equals(f.FieldName, "Total", StringComparison.OrdinalIgnoreCase) && decimal.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var t))
            {
                doc.DetectedTotal = t;
            }
            else if (string.Equals(f.FieldName, "Vendor", StringComparison.OrdinalIgnoreCase) || string.Equals(f.FieldName, "CompanyName", StringComparison.OrdinalIgnoreCase))
            {
                doc.DetectedVendor = f.Value;
            }
            else if (string.Equals(f.FieldName, "InvoiceNumber", StringComparison.OrdinalIgnoreCase) || string.Equals(f.FieldName, "LicenseNumber", StringComparison.OrdinalIgnoreCase))
            {
                doc.DetectedInvoiceNumber = f.Value;
            }
            else if (string.Equals(f.FieldName, "ExpiryDate", StringComparison.OrdinalIgnoreCase) && DateNormalizer.Parse(f.Value) is { } exp)
            {
                doc.ExpiryDate = exp;
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BusinessDocumentDto> ReprocessAsync(Guid id, CancellationToken ct = default)
    {
        await ProcessOcrAsync(id, ct);
        var doc = await GetDocumentByIdAsync(id, ct) ?? throw new NotFoundException("Document", id);
        return doc;
    }

    public async Task<int> RunExpiryChecksAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var docs = await db.Set<BusinessDocument>()
            .IgnoreQueryFilters()
            .Where(d => !d.IsDeleted && d.Status == "Approved" && d.ExpiryDate.HasValue)
            .ToListAsync(ct);

        int alertsSent = 0;
        int[] reminderOffsets = [60, 30, 7];

        foreach (var group in docs.GroupBy(d => d.TenantId))
        {
            using (tenant.Use(group.Key))
            {
                foreach (var doc in group)
                {
                    var expiry = doc.ExpiryDate!.Value;
                    int daysLeft = expiry.DayNumber - today.DayNumber;

                    // Deserialize already notified offsets
                    List<int> notified = [];
                    if (!string.IsNullOrWhiteSpace(doc.ExpiryNotifiedOffsetsJson))
                    {
                        try
                        {
                            notified = JsonSerializer.Deserialize<List<int>>(doc.ExpiryNotifiedOffsetsJson) ?? [];
                        }
                        catch { }
                    }

                    foreach (var offset in reminderOffsets)
                    {
                        if (daysLeft <= offset && !notified.Contains(offset))
                        {
                            notified.Add(offset);
                            alertsSent++;

                            // Send notification to document owner / admins
                            try
                            {
                                await notifications.NotifyAsync(new NotificationRequest(
                                    UserIds: [], // All active users in tenant
                                    Type: "DocumentExpiring",
                                    Title: $"Document Expiring: {doc.FileName}",
                                    Body: $"{doc.Category} ({doc.FileName}) is expiring in {daysLeft} days (on {expiry:yyyy-MM-dd}).",
                                    EntityType: "Document",
                                    EntityId: doc.Id,
                                    Url: $"/documents/review/{doc.Id}"
                                ), ct);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to send expiry notification for document {DocId}", doc.Id);
                            }
                        }
                    }

                    doc.ExpiryNotifiedOffsetsJson = JsonSerializer.Serialize(notified);
                }

                await db.SaveChangesAsync(ct);
            }
        }

        return alertsSent;
    }

    public async Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid? accountId = null, Guid? dealId = null, CancellationToken ct = default)
    {
        var q = db.Set<BusinessDocument>().AsNoTracking().Where(d => !d.IsDeleted);
        if (accountId.HasValue) q = q.Where(d => d.AssociatedAccountId == accountId.Value);
        if (dealId.HasValue) q = q.Where(d => d.AssociatedDealId == dealId.Value);

        var list = await q.OrderByDescending(d => d.CreatedAt).ToListAsync(ct);
        return list.Select(MapDocument).ToList();
    }

    public async Task<BusinessDocumentDto?> GetDocumentByIdAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        return doc == null ? null : MapDocument(doc);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetDocumentStreamAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (doc == null) return null;

        var stream = await fileStorage.OpenReadAsync(doc.StorageKey, ct);
        return (stream, doc.ContentType, doc.FileName);
    }

    public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (doc == null) return false;

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static BusinessDocumentDto MapDocument(BusinessDocument d)
    {
        List<OcrExtractedFieldDto> fields = [];
        if (!string.IsNullOrWhiteSpace(d.OcrStructuredDataJson))
        {
            try
            {
                fields = JsonSerializer.Deserialize<List<OcrExtractedFieldDto>>(d.OcrStructuredDataJson) ?? [];
            }
            catch { }
        }

        return new BusinessDocumentDto
        {
            Id = d.Id,
            FileName = d.FileName,
            ContentType = d.ContentType,
            FileSizeBytes = d.FileSizeBytes,
            Category = d.Category,
            AssociatedAccountId = d.AssociatedAccountId,
            AssociatedDealId = d.AssociatedDealId,
            AssociatedQuoteId = d.AssociatedQuoteId,
            AssociatedInvoiceId = d.AssociatedInvoiceId,
            AssociatedContactId = d.AssociatedContactId,
            SharedWithPortal = d.SharedWithPortal,
            Status = d.Status,
            ReviewedById = d.ReviewedById,
            ReviewedAt = d.ReviewedAt,
            RejectReason = d.RejectReason,
            ExpiryDate = d.ExpiryDate,
            IssueDate = d.IssueDate,
            Confidence = d.Confidence,
            DetectedVendor = d.DetectedVendor,
            DetectedTotal = d.DetectedTotal,
            DetectedInvoiceNumber = d.DetectedInvoiceNumber,
            OcrStatus = (DocumentOcrStatus)d.OcrStatus,
            UploadedAt = d.CreatedAt,
            OcrResult = d.OcrStatus == (int)DocumentOcrStatus.Completed ? new OcrResultDto
            {
                DocumentId = d.Id,
                Status = DocumentOcrStatus.Completed,
                DocumentType = d.Category,
                ExtractedText = d.OcrRawText,
                ExtractedFields = fields,
                DetectedVendor = d.DetectedVendor,
                DetectedInvoiceNumber = d.DetectedInvoiceNumber,
                DetectedTotal = d.DetectedTotal,
                DetectedDate = d.DetectedDate
            } : null
        };
    }
}
