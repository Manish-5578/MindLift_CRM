using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Infrastructure.Documents.Ai;
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
    INotificationService notifications,
    DocumentPageReader pageReader,
    SearchablePdfService searchablePdf,
    DocumentFieldReader fieldReader,
    DocumentRecordApplier applier,
    IBackgroundJobClient jobs,
    IPlatformEntitlements entitlements,
    IDataScopeService scope,
    ICpqService cpq,
    ILogger<DocumentOcrService> logger) : IDocumentOcrService
{
    private Guid CurrentTenantId => tenant.TenantId ?? throw new UnauthorizedAccessException("Tenant context required.");

    private sealed record Scopes(IReadOnlyList<Guid>? Deals, IReadOnlyList<Guid>? Accounts, IReadOnlyList<Guid>? Contacts);

    private async Task<Scopes> ScopesAsync(CancellationToken ct) => new(
        await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Deals, ct),
        await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Accounts, ct),
        await scope.GetScopeUserIdsAsync(MindLift.Shared.Enums.AppModule.Contacts, ct));

    // A document follows the record it belongs to: the deal when there is one, otherwise the account,
    // otherwise the contact. One with no link is visible to everyone who can open Documents.
    private IQueryable<BusinessDocument> ScopeDocuments(IQueryable<BusinessDocument> q, Scopes s)
    {
        if (s.Deals is null && s.Accounts is null && s.Contacts is null) return q;
        var d = s.Deals; var a = s.Accounts; var c = s.Contacts;
        return q.Where(x =>
            x.AssociatedDealId != null ? (d == null || db.Set<Deal>().Any(r => r.Id == x.AssociatedDealId && d.Contains(r.OwnerId)))
            : x.AssociatedAccountId != null ? (a == null || db.Set<Account>().Any(r => r.Id == x.AssociatedAccountId && a.Contains(r.OwnerId)))
            : x.AssociatedContactId != null ? (c == null || db.Set<Contact>().Any(r => r.Id == x.AssociatedContactId && c.Contains(r.OwnerId)))
            : true);
    }

    public async Task EnsureDocumentInScopeAsync(Guid id, CancellationToken ct = default)
    {
        var visible = await ScopeDocuments(db.Set<BusinessDocument>().AsNoTracking(), await ScopesAsync(ct))
            .AnyAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (!visible) throw new NotFoundException("Document", id);
    }

    public async Task EnsureLinksInScopeAsync(Guid? accountId, Guid? dealId, Guid? contactId, Guid? quoteId = null, Guid? invoiceId = null, CancellationToken ct = default)
    {
        var s = await ScopesAsync(ct);
        if (dealId is { } did && s.Deals is { } ds && !await db.Set<Deal>().AnyAsync(r => r.Id == did && ds.Contains(r.OwnerId), ct)) throw new NotFoundException("Deal", did);
        if (accountId is { } aid && s.Accounts is { } accs && !await db.Set<Account>().AnyAsync(r => r.Id == aid && accs.Contains(r.OwnerId), ct)) throw new NotFoundException("Account", aid);
        if (contactId is { } cid && s.Contacts is { } cs && !await db.Set<Contact>().AnyAsync(r => r.Id == cid && cs.Contains(r.OwnerId), ct)) throw new NotFoundException("Contact", cid);
        if (quoteId is { } qid && await cpq.GetQuoteByIdAsync(qid, ct) is null) throw new NotFoundException("Quote", qid);
        if (invoiceId is { } iid && await cpq.GetInvoiceByIdAsync(iid, ct) is null) throw new NotFoundException("Invoice", iid);
    }

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

        var cleanFileName = Path.GetFileName(fileName);
        var storageKey = $"docs/{CurrentTenantId}/{Guid.NewGuid():N}_{cleanFileName}";
        using var stream = new MemoryStream(fileBytes);
        await fileStorage.SaveAsync(storageKey, stream, ct);

        var doc = new BusinessDocument
        {
            TenantId = CurrentTenantId,
            FileName = cleanFileName,
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
        doc.ExtractionMethod = null;
        doc.ExtractionNote = null;
        await db.SaveChangesAsync(ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Read every page: PDF text layer where there is one, OCR for scans and photos.
        IReadOnlyList<PageText> pages = [];
        string rawText = string.Empty;
        string? tempPath = null;
        try
        {
            tempPath = await SaveTempCopyAsync(doc, ct);
            pages = await pageReader.ReadAsync(tempPath, ct);
            rawText = string.Join("\n", pages.Select(p => p.Text));
        }
        catch (OcrBusyException)
        {
            // Too many documents are being read. This one is not failed; it goes back to waiting so it can be tried again.
            doc.OcrStatus = (int)DocumentOcrStatus.Pending;
            doc.Status = "Queued";
            await db.SaveChangesAsync(ct);
            throw new BusinessRuleException("ocr_busy", "The document reader is busy. Try again in a minute.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read document {DocId}", documentId);
        }
        finally
        {
            // The copy made for reading is only needed while it is read.
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Left for the system's temporary file cleanup.
                }
            }
        }

        // How well the pages were read. A file that already had its text is exact; a scan or photo is only as good as its reading.
        var scanned = pages.Where(p => p.Source == "Ocr").ToList();
        var readConfidence = scanned.Count == 0 ? 1.0 : Math.Clamp(scanned.Average(p => p.Confidence ?? 0) / 100.0, 0, 1);
        var lowConfidencePages = pages.Where(p => p.LowConfidence).Select(p => p.PageNumber).ToList();

        bool isBinaryOrEmpty = string.IsNullOrWhiteSpace(rawText);

        // No readable text: the document is marked Failed. Nothing is invented, so no made-up values can reach a record.
        if (isBinaryOrEmpty)
        {
            doc.OcrStatus = (int)DocumentOcrStatus.Failed;
            doc.Status = "Failed";
            doc.OcrRawText = null;
            doc.OcrStructuredDataJson = "[]";
            doc.ReadConfidence = null;
            doc.LowConfidencePagesJson = null;
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
        // B2. The workspace's own document types (Settings > Document types). A type already on the document (chosen on upload or review, or found
        // last time) is used; otherwise the best keyword match. Its fields are read by AI and/or patterns, each as the field's mode says.
        var aiAllowed = await db.Set<TenantSettings>().AsNoTracking().Select(t => t.AiReadingEnabled).FirstOrDefaultAsync(ct)
            && await entitlements.IsAiAllowedAsync(ct);
        var customTypes = await db.Set<DocumentTypeDefinition>().AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        var bestType = customTypes.FirstOrDefault(t => string.Equals(t.Key, doc.Category, StringComparison.Ordinal));
        if (bestType is null)
        {
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
        }

        string? extractionMethod = null;
        string? extractionNote = null;
        if (bestType is not null)
        {
            detectedCategory = bestType.Key;
            var rules = CustomDocumentExtractor.Fields(bestType.FieldsJson);
            var reading = await fieldReader.ReadAsync(bestType.Name, bestType.Description, rules, pages, aiAllowed, ct);
            extractionMethod = reading.Method;
            extractionNote = reading.Note;

            // The type says which values are wanted, so the values the built-in rules read on their own (an invoice's total on a passport slip, say) are not listed beside them.
            extractedFields.RemoveAll(f => string.IsNullOrEmpty(f.Key));
            foreach (var field in reading.Fields)
            {
                var rule = rules.First(r => r.Key == field.Key);

                // A value the built-in rules read for the same field (an invoice total, say) gives way to the type's own.
                extractedFields.RemoveAll(f => string.Equals(f.FieldName, field.Key, StringComparison.OrdinalIgnoreCase));

                // Every field the type defines is listed, found or not, so a person can fill in what could not be read.
                extractedFields.Add(new OcrExtractedFieldDto
                {
                    FieldName = field.Key,
                    Key = field.Key,
                    Label = field.Label,
                    Value = field.Found ? field.Value ?? string.Empty : string.Empty,
                    Confidence = field.Found ? field.Confidence / 100.0 : 0,
                    Method = field.Found ? field.Method : null,
                    Snippet = field.Snippet,
                    Page = field.Page,
                    Verified = field.Found ? field.Verified : null,
                    SaveTo = rule.SaveTo,
                    ValueType = rule.ValueType,
                    Required = rule.Required,
                });

                if (!field.Found || field.Value is null)
                {
                    continue;
                }

                if (field.Key == "expiry_date" && DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) docExpiry = expiry;
                if (field.Key == "issue_date" && DateOnly.TryParse(field.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issue)) docIssue = issue;
            }
        }
        else if (extractedFields.Count == 0 && aiAllowed)
        {
            // No type matched and the built-in rules found nothing: let the AI list the useful facts, so the document is not left empty.
            var (details, note) = await fieldReader.ReadDetailsAsync(pages, aiAllowed, ct);
            extractionNote = note;
            if (details.Count > 0)
            {
                extractionMethod = DocumentFieldReader.MethodAi;
            }

            foreach (var detail in details)
            {
                extractedFields.Add(new OcrExtractedFieldDto
                {
                    FieldName = detail.Key,
                    Key = detail.Key,
                    Label = detail.Label,
                    Value = detail.Value,
                    Confidence = detail.Confidence,
                    Method = "ai",
                    Snippet = detail.Snippet,
                    Page = detail.Page,
                    Verified = true,
                    ValueType = DocumentValueTypes.Text,
                });
            }
        }

        // A field's confidence is how sure the extractor is of the match; it can be no surer than the reading it matched in.
        if (readConfidence < 1.0)
        {
            foreach (var field in extractedFields)
            {
                field.Confidence = Math.Round(field.Confidence * readConfidence, 2);
            }

            overallConfidence = Math.Round(overallConfidence * readConfidence, 2);
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
        doc.ExtractionMethod = extractionMethod ?? DocumentFieldReader.MethodRules;
        doc.ExtractionNote = extractionNote;
        doc.ReadConfidence = Math.Round(readConfidence, 2);
        doc.LowConfidencePagesJson = lowConfidencePages.Count > 0 ? JsonSerializer.Serialize(lowConfidencePages) : null;

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
            ProcessingDuration = sw.Elapsed,
            ReadConfidence = Math.Round(readConfidence, 2),
            LowConfidencePages = lowConfidencePages,
            ExtractionMethod = doc.ExtractionMethod,
            ExtractionNote = doc.ExtractionNote,
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

            // Save the chosen values into the linked contact, account or deal. All or none: if one record cannot take its value the document stays unapproved.
            if (request.ApplyFields is { Count: > 0 })
            {
                await applier.ApplyAsync(doc, request.ApplyFields, ct);
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

        var before = ReadFields(doc.OcrStructuredDataJson);
        foreach (var field in request.Fields)
        {
            var old = before.FirstOrDefault(b => string.Equals(b.FieldName, field.FieldName, StringComparison.OrdinalIgnoreCase));
            if (old is null)
            {
                // A field a person added by hand.
                field.Method ??= "edited";
                continue;
            }

            field.Key ??= old.Key;
            field.Label ??= old.Label;
            field.SaveTo ??= old.SaveTo;
            field.ValueType ??= old.ValueType;
            if (!string.Equals(old.Value, field.Value, StringComparison.Ordinal))
            {
                field.Method = "edited";
                field.Verified = null;
                field.Snippet = null;
                field.Page = null;
                field.Confidence = string.IsNullOrWhiteSpace(field.Value) ? 0 : 1.0;
            }
            else
            {
                field.Method ??= old.Method;
                field.Snippet ??= old.Snippet;
                field.Page ??= old.Page;
                field.Verified ??= old.Verified;
            }
        }

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

    /// <summary>
    /// Puts the document in the queue to be read in the background and returns at once. Reading with AI can take a minute or more, so the person is not kept waiting:
    /// the status moves from Queued to Processing to NeedsReview. A type chosen here is kept on the document and used when it is read.
    /// </summary>
    public async Task<BusinessDocumentDto> QueueReadAsync(Guid id, string? documentType, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", id);

        if (!string.IsNullOrWhiteSpace(documentType))
        {
            var key = documentType.Trim();
            var exists = await db.Set<DocumentTypeDefinition>().AnyAsync(t => t.Key == key && t.IsActive, ct);
            if (!exists)
            {
                throw new ValidationException("documentType", "Choose one of the document types set up in Settings > Document types.");
            }

            doc.Category = key;
        }
        else if (documentType is not null)
        {
            // An empty choice means "decide from the words in the document": the type is forgotten.
            doc.Category = "General";
        }

        // A reading that has been going for longer than any reading can take was cut off (the app was stopped, say), so the document may be read again.
        if (doc.Status == "Processing" && DateTime.UtcNow - (doc.UpdatedAt ?? doc.CreatedAt) < TimeSpan.FromMinutes(20))
        {
            throw new BusinessRuleException("already_reading", "This document is being read right now.");
        }

        doc.OcrStatus = (int)DocumentOcrStatus.Pending;
        doc.Status = "Queued";
        await db.SaveChangesAsync(ct);

        var tenantId = CurrentTenantId;
        jobs.Enqueue<DocumentReadJob>(job => job.RunAsync(tenantId, id, 0, CancellationToken.None));
        return MapDocument(doc);
    }

    /// <summary>Marks a document whose reading stopped unexpectedly as Failed, with a note, so it is never left Queued or Processing.</summary>
    public async Task MarkReadFailedAsync(Guid id, string note, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (doc is null)
        {
            return;
        }

        doc.OcrStatus = (int)DocumentOcrStatus.Failed;
        doc.Status = "Failed";
        doc.ExtractionNote = note;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Links the document to a contact, an account and/or a deal (each must be this workspace's own), or clears a link by passing null.</summary>
    public async Task<BusinessDocumentDto> LinkAsync(Guid id, LinkDocumentRequest request, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct)
            ?? throw new NotFoundException("Document", id);

        await EnsureLinkedRecordsExistAsync(request.AccountId, request.DealId, null, null, request.ContactId, ct);
        doc.AssociatedContactId = request.ContactId;
        doc.AssociatedAccountId = request.AccountId;
        doc.AssociatedDealId = request.DealId;
        await db.SaveChangesAsync(ct);
        return MapDocument(doc);
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
        var good = MindLift.Infrastructure.Tenancy.TenantAccess.InGoodStanding(db.Set<Tenant>(), DateTime.UtcNow);
        var docs = await db.Set<BusinessDocument>()
            .IgnoreQueryFilters()
            .Where(d => good.Contains(d.TenantId) && !d.IsDeleted && d.Status == "Approved" && d.ExpiryDate.HasValue)
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

    public async Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid? accountId = null, Guid? dealId = null, Guid? contactId = null, CancellationToken ct = default)
    {
        var q = db.Set<BusinessDocument>().AsNoTracking().Where(d => !d.IsDeleted);
        if (accountId.HasValue) q = q.Where(d => d.AssociatedAccountId == accountId.Value);
        if (dealId.HasValue) q = q.Where(d => d.AssociatedDealId == dealId.Value);
        if (contactId.HasValue) q = q.Where(d => d.AssociatedContactId == contactId.Value);
        q = ScopeDocuments(q, await ScopesAsync(ct));

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

    public async Task<(Stream Stream, string ContentType, string FileName)?> GetSearchablePdfAsync(Guid id, CancellationToken ct = default)
    {
        var doc = await db.Set<BusinessDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct);
        if (doc == null) return null;

        string? tempPath = null;
        string? copy;
        try
        {
            tempPath = await SaveTempCopyAsync(doc, ct);
            copy = await searchablePdf.CreateAsync(tempPath, ct);
        }
        catch (OcrBusyException)
        {
            throw new BusinessRuleException("ocr_busy", "The document reader is busy. Try again in a minute.");
        }
        catch (NotSupportedException ex)
        {
            throw new ValidationException("File", ex.Message);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidDataException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not make a searchable copy of document {DocId}", id);
            throw new BusinessRuleException("searchable_pdf_failed", "A searchable copy could not be made of this document.");
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Left for the system's temporary file cleanup.
                }
            }
        }

        // Already searchable: the stored file is the answer.
        if (copy is null)
        {
            return (await fileStorage.OpenReadAsync(doc.StorageKey, ct), doc.ContentType, doc.FileName);
        }

        var name = Path.GetFileNameWithoutExtension(doc.FileName ?? "document") + "-searchable.pdf";
        var stream = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
        return (stream, "application/pdf", name);
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

    private static List<OcrExtractedFieldDto> ReadFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<OcrExtractedFieldDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<int> ReadPages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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
                DetectedDate = d.DetectedDate,
                ReadConfidence = d.ReadConfidence,
                LowConfidencePages = ReadPages(d.LowConfidencePagesJson),
                ExtractionMethod = d.ExtractionMethod,
                ExtractionNote = d.ExtractionNote,
            } : null
        };
    }
}
