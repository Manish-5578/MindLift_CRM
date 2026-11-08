using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Leads;

public sealed class LeadCardOcrService(
    DocumentPageReader? pageReader = null,
    IAppDbContext? db = null,
    ITenantContext? tenant = null,
    ILogger<LeadCardOcrService>? logger = null)
{
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(?:\+?\d{1,4}[-.\s]?)?\(?\d{1,4}\)?[-.\s]?\d{2,4}[-.\s]?\d{3,5}|\+?\d{7,15}", RegexOptions.Compiled);
    private static readonly Regex WebRegex = new(@"(https?:\/\/[^\s]+|www\.[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}[^\s]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] TitleKeywords =
    [
        "director", "manager", "ceo", "cto", "cfo", "coo", "founder", "co-founder", "president",
        "executive", "engineer", "consultant", "head of", "lead", "developer", "specialist",
        "representative", "partner", "officer", "advisor", "supervisor", "architect", "analyst", "designer",
        // Arabic Job Titles
        "مدير عام", "المدير العام", "رئيس تنفيذي", "الرئيس التنفيذي", "مؤسس", "شريك", "مدير",
        "مهندس", "مستشار", "رئيس", "أخصائي", "مشرف", "مسؤول", "ممثل", "مطور", "معماري"
    ];

    private static readonly string[] CompanyKeywords =
    [
        "llc", "inc", "corp", "corporation", "ltd", "limited", "group", "solutions",
        "technologies", "tech", "co.", "company", "holdings", "ventures", "services",
        "industries", "consulting", "enterprises", "global", "international", "agency", "media", "capital",
        // Arabic Corporate & Legal Entity Designations
        "ذ.م.م", "ذ م م", "ش.م.ح", "ش م ح", "ش.م.ع", "ش م ع", "شركة", "مؤسسة", "مجموعة",
        "استشارات", "تكنولوجيا", "حلول", "خدمات", "مشاريع", "القابضة", "التجارية", "العالمية", "للتجارة", "للمقاولات"
    ];

    public async Task<ExtractedLeadCardDto> ExtractFromDirectStreamAsync(
        Stream inputStream,
        string fileName,
        CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"stream_{Guid.NewGuid():N}_{Path.GetFileName(fileName)}");
        try
        {
            // Direct streaming from HTTP request to temporary file on disk (zero RAM memory pressure)
            await using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await inputStream.CopyToAsync(fileStream, ct);
            }

            var header = new byte[16];
            await using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bytesRead = await fs.ReadAsync(header, 0, 16, ct);
                if (bytesRead < 8)
                {
                    throw new ValidationException("invalid_image", "The uploaded file is empty or too small to be a valid image.");
                }
            }
            ValidateImageHeader(header);

            var ext = DetectExtension(header, Path.GetExtension(fileName));
            return await ProcessImageFileAsync(tempFile, ext, null, ct);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch
            {
                // cleanup
            }
        }
    }

    public async Task<ExtractedLeadCardDto> ExtractFromFileStreamAsync(
        Stream stream,
        string fileName,
        CancellationToken ct = default)
    {
        return await ExtractFromDirectStreamAsync(stream, fileName, ct);
    }

    public async Task<ExtractedLeadCardDto> ExtractAsync(
        ParseCardOcrRequest request,
        CancellationToken ct = default)
    {
        var clientText = request.RawText?.Trim();

        if (!string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            var rawBase64 = request.ImageBase64;
            var commaIdx = rawBase64.IndexOf(',');
            if (commaIdx >= 0)
            {
                rawBase64 = rawBase64[(commaIdx + 1)..];
            }

            if (rawBase64.Length > 22_000_000)
            {
                throw new ValidationException("file_too_large", "The card image exceeds the 15 MB limit.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(rawBase64.Trim());
            }
            catch (FormatException)
            {
                throw new ValidationException("invalid_image_base64", "The provided image data is corrupted or invalid.");
            }

            return await ExtractFromBytesAsync(bytes, ".png", clientText, ct);
        }

        if (string.IsNullOrWhiteSpace(clientText))
        {
            throw new ValidationException("empty_card_text", "No readable text or image was provided.");
        }

        return await ParseTextAndCheckDuplicatesAsync(clientText, null, null, ct);
    }

    private async Task<ExtractedLeadCardDto> ExtractFromBytesAsync(
        byte[] bytes,
        string extension,
        string? clientText,
        CancellationToken ct)
    {
        ValidateImageHeader(bytes);

        var ext = DetectExtension(bytes, extension);
        var tempFile = Path.Combine(Path.GetTempPath(), $"card_{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllBytesAsync(tempFile, bytes, ct);
            return await ProcessImageFileAsync(tempFile, ext, clientText, ct);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch
            {
                // cleanup
            }
        }
    }

    private async Task<ExtractedLeadCardDto> ProcessImageFileAsync(
        string filePath,
        string? extension,
        string? clientText,
        CancellationToken ct)
    {
        string? serverText = null;
        double? engineConfidence = null;
        List<CardEvidenceBoxDto>? evidence = null;

        if (pageReader != null)
        {
            try
            {
                var pages = await pageReader.ReadAsync(filePath, ct);
                if (pages.Count > 0)
                {
                    var p0 = pages[0];
                    if (p0.Confidence.HasValue)
                    {
                        engineConfidence = Math.Round(p0.Confidence.Value / 100.0, 2);
                    }

                    if (p0.Words != null && p0.Words.Count > 0)
                    {
                        evidence = p0.Words.Select(w => new CardEvidenceBoxDto(
                            Text: w.Text,
                            Confidence: Math.Round(w.Confidence / 100.0, 2),
                            X: Math.Round(w.X, 3),
                            Y: Math.Round(w.Y, 3),
                            Width: Math.Round(w.Width, 3),
                            Height: Math.Round(w.Height, 3)
                        )).ToList();
                    }

                    var ocrLines = pages
                        .SelectMany(p => (p.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        .Select(l => l.Trim())
                        .Where(l => l.Length > 0);

                    serverText = string.Join("\n", ocrLines).Trim();
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Server OCR engine failed to process image file.");
            }
        }

        var chosenText = !string.IsNullOrWhiteSpace(serverText) ? serverText : (clientText ?? string.Empty);

        if (string.IsNullOrWhiteSpace(chosenText))
        {
            throw new ValidationException("empty_card_text", "No readable text could be recognized from the card. Please ensure the card is clear, well lit, and in focus.");
        }

        return await ParseTextAndCheckDuplicatesAsync(chosenText, engineConfidence, evidence, ct);
    }

    private async Task<ExtractedLeadCardDto> ParseTextAndCheckDuplicatesAsync(
        string rawText,
        double? engineConfidence,
        IReadOnlyList<CardEvidenceBoxDto>? evidence,
        CancellationToken ct)
    {
        var normalizedText = NormalizeArabicIndicDigits(rawText);

        var lines = normalizedText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        string? email = null;
        string? phone = null;
        string? website = null;
        string? jobTitle = null;
        string? companyName = null;
        string? fullName = null;
        string? address = null;

        var remainingLines = new List<string>();
        var nameFromEmail = false;

        foreach (var line in lines)
        {
            var matchEmail = EmailRegex.Match(line);
            if (matchEmail.Success && email == null)
            {
                email = matchEmail.Value;
                continue;
            }

            var matchWeb = WebRegex.Match(line);
            if (matchWeb.Success && website == null)
            {
                website = matchWeb.Value;
                continue;
            }

            var lower = line.ToLowerInvariant();
            var hasCompanyWord = CompanyKeywords.Any(k => lower.Contains(k));

            // Address evidence (landmark, street words, a PIN such as "560 001", an "Address:" label) wins before the phone pattern,
            // which would otherwise take "560 001" for a phone number and drop the whole line.
            if (CardLineClassifier.IsAddressLine(line, hasCompanyWord))
            {
                address = address == null ? line : $"{address}, {line}";
                continue;
            }

            var matchPhone = PhoneRegex.Match(line);
            if (matchPhone.Success && phone == null && matchPhone.Value.Length >= 7)
            {
                phone = NormalizePhoneNumber(matchPhone.Value);
                continue;
            }

            if (jobTitle == null && TitleKeywords.Any(k => lower.Contains(k)))
            {
                jobTitle = line;
                continue;
            }

            if (companyName == null && hasCompanyWord)
            {
                companyName = line;
                continue;
            }

            remainingLines.Add(line);
        }

        // Person name and company come from the lines nothing else claimed. A name is only taken from a line that cannot be an address.
        foreach (var line in remainingLines)
        {
            if (fullName == null && CardLineClassifier.IsLikelyPersonName(line))
            {
                fullName = line;
            }
            else if (companyName == null)
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length >= 1 && words.Length <= 6)
                {
                    companyName = line;
                }
            }
        }

        // First & Last name split
        string? firstName = null;
        string? lastName = null;
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                firstName = parts[0];
                lastName = string.Join(" ", parts.Skip(1));
            }
            else
            {
                firstName = fullName.Trim();
                lastName = string.Empty;
            }
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            var userPart = email.Split('@')[0];
            var segments = userPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                firstName = char.ToUpperInvariant(segments[0][0]) + segments[0][1..].ToLowerInvariant();
                lastName = char.ToUpperInvariant(segments[1][0]) + segments[1][1..].ToLowerInvariant();
                fullName = $"{firstName} {lastName}";
                nameFromEmail = true;
            }
        }

        // Script Detection
        var hasArabic = Regex.IsMatch(rawText, @"[\u0600-\u06FF]");
        var hasLatin = Regex.IsMatch(rawText, @"[a-zA-Z]");
        var script = (hasArabic && hasLatin) ? "Bilingual" : (hasArabic ? "Arabic" : "Latin");

        // Confidences calculation
        var emailConf = !string.IsNullOrWhiteSpace(email) ? 0.95 : (double?)null;
        var phoneConf = !string.IsNullOrWhiteSpace(phone) ? 0.90 : (double?)null;
        var companyConf = !string.IsNullOrWhiteSpace(companyName)
            ? (CompanyKeywords.Any(k => companyName.ToLowerInvariant().Contains(k)) ? 0.90 : 0.70)
            : (double?)null;
        var nameConf = string.IsNullOrWhiteSpace(fullName)
            ? (double?)null
            : nameFromEmail ? 0.50 : CardLineClassifier.NameConfidence(fullName, email);
        var addressConf = string.IsNullOrWhiteSpace(address) ? (double?)null : CardLineClassifier.AddressConfidence(address);
        var titleConf = !string.IsNullOrWhiteSpace(jobTitle) ? 0.88 : (double?)null;

        var scores = new[] { emailConf, phoneConf, companyConf, nameConf, titleConf, addressConf }.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var extractionAvg = scores.Count > 0 ? Math.Round(scores.Average(), 2) : 0.70;
        var overallConfidence = engineConfidence.HasValue
            ? Math.Round((engineConfidence.Value * 0.5) + (extractionAvg * 0.5), 2)
            : extractionAvg;

        // Duplicate Candidate Check in CRM Database
        LeadDuplicateCandidateDto? duplicate = null;
        if (db != null)
        {
            duplicate = await FindDuplicateInCrmAsync(email, phone, companyName, ct);
        }

        return new ExtractedLeadCardDto(
            CompanyName: companyName ?? string.Empty,
            FirstName: firstName,
            LastName: lastName,
            FullName: fullName ?? (!string.IsNullOrWhiteSpace(firstName) ? $"{firstName} {lastName}".Trim() : null),
            Email: email,
            Phone: phone,
            JobTitle: jobTitle,
            Website: website,
            Address: address,
            RawText: rawText,
            OverallConfidence: overallConfidence,
            CompanyConfidence: companyConf,
            NameConfidence: nameConf,
            EmailConfidence: emailConf,
            PhoneConfidence: phoneConf,
            TitleConfidence: titleConf,
            DetectedScript: script,
            Duplicate: duplicate,
            EngineConfidence: engineConfidence,
            Evidence: evidence,
            AddressConfidence: addressConf
        );
    }

    private async Task<LeadDuplicateCandidateDto?> FindDuplicateInCrmAsync(
        string? email,
        string? phone,
        string? companyName,
        CancellationToken ct)
    {
        if (db == null) return null;

        var normEmail = email?.Trim().ToLowerInvariant();
        var normPhone = phone != null ? new string(phone.Where(char.IsDigit).ToArray()) : null;

        try
        {
            if (!string.IsNullOrEmpty(normEmail))
            {
                var lead = await db.Set<Lead>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => !l.IsDeleted && (l.NormalizedEmail == normEmail || (l.Email != null && l.Email.ToLower() == normEmail)), ct);

                if (lead != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: lead.Id,
                        Name: $"{lead.FirstName} {lead.LastName}".Trim(),
                        Company: lead.CompanyName,
                        Email: lead.Email,
                        Phone: lead.Phone,
                        RecordType: "Lead"
                    );
                }

                var contact = await db.Set<Contact>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => !c.IsDeleted && (c.NormalizedEmail == normEmail || (c.Email != null && c.Email.ToLower() == normEmail)), ct);

                if (contact != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: contact.Id,
                        Name: $"{contact.FirstName} {contact.LastName}".Trim(),
                        Company: null,
                        Email: contact.Email,
                        Phone: contact.Phone,
                        RecordType: "Contact"
                    );
                }
            }

            if (!string.IsNullOrEmpty(normPhone) && normPhone.Length >= 7)
            {
                var phoneSuffix = normPhone[^7..];
                var lead = await db.Set<Lead>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => !l.IsDeleted && l.Phone != null && l.Phone.Contains(phoneSuffix), ct);

                if (lead != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: lead.Id,
                        Name: $"{lead.FirstName} {lead.LastName}".Trim(),
                        Company: lead.CompanyName,
                        Email: lead.Email,
                        Phone: lead.Phone,
                        RecordType: "Lead"
                    );
                }

                var contact = await db.Set<Contact>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => !c.IsDeleted && c.Phone != null && c.Phone.Contains(phoneSuffix), ct);

                if (contact != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: contact.Id,
                        Name: $"{contact.FirstName} {contact.LastName}".Trim(),
                        Company: null,
                        Email: contact.Email,
                        Phone: contact.Phone,
                        RecordType: "Contact"
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(companyName) && companyName.Trim().Length >= 3)
            {
                var normComp = companyName.Trim().ToLowerInvariant();
                var account = await db.Set<Account>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => !a.IsDeleted && a.Name.ToLower() == normComp, ct);

                if (account != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: account.Id,
                        Name: account.Name,
                        Company: account.Name,
                        Email: null,
                        Phone: account.Phone,
                        RecordType: "Account"
                    );
                }

                var leadComp = await db.Set<Lead>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(l => !l.IsDeleted && l.CompanyName != null && l.CompanyName.ToLower() == normComp, ct);

                if (leadComp != null)
                {
                    return new LeadDuplicateCandidateDto(
                        Id: leadComp.Id,
                        Name: $"{leadComp.FirstName} {leadComp.LastName}".Trim(),
                        Company: leadComp.CompanyName,
                        Email: leadComp.Email,
                        Phone: leadComp.Phone,
                        RecordType: "Lead"
                    );
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to execute CRM duplicate query for scanned lead.");
        }

        return null;
    }

    private static string DetectExtension(byte[] bytes, string? fallbackExt)
    {
        if (bytes != null && bytes.Length >= 8)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
            if (bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return ".webp";
            if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A)) return ".tiff";
        }

        if (!string.IsNullOrWhiteSpace(fallbackExt) && fallbackExt.StartsWith("."))
        {
            return fallbackExt.ToLowerInvariant();
        }

        return ".jpg";
    }

    public static string NormalizeArabicIndicDigits(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
            else if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string NormalizePhoneNumber(string raw)
    {
        var cleaned = raw.Trim();
        var digitsOnly = new string(cleaned.Where(char.IsDigit).ToArray());

        // Format Gulf numbers e.g. UAE 0501234567 -> +971 50 123 4567
        if (digitsOnly.StartsWith("05") && digitsOnly.Length == 10)
        {
            return $"+971 {digitsOnly[1..3]} {digitsOnly[3..6]} {digitsOnly[6..]}";
        }
        if (digitsOnly.StartsWith("00971") && digitsOnly.Length == 14)
        {
            return $"+971 {digitsOnly[5..7]} {digitsOnly[7..10]} {digitsOnly[10..]}";
        }
        if (digitsOnly.StartsWith("9715") && digitsOnly.Length == 12)
        {
            return $"+971 {digitsOnly[3..5]} {digitsOnly[5..8]} {digitsOnly[8..]}";
        }

        return cleaned;
    }

    private static void ValidateImageHeader(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8)
        {
            throw new ValidationException("invalid_image", "The uploaded file is empty or too small to be a valid image.");
        }

        // Magic bytes check
        bool isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        bool isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        bool isWebp = bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        bool isTiff = (bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) ||
                      (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A);

        if (!isJpeg && !isPng && !isWebp && !isTiff)
        {
            throw new ValidationException("unsupported_image_format", "Only JPEG, PNG, WEBP, and TIFF images are supported for card scanning.");
        }
    }

    /// <summary>
    /// Merges what was read from the front and the back of a card.
    /// For each field the better-trusted side wins (the front on a tie). Email, phone and website that match once formatting is ignored count as one.
    /// When the two sides disagree on a field, the winner is returned and both readings are listed in <c>Conflicts</c> so the person can choose.
    /// Addresses from both sides are combined without repeating a line. Evidence boxes are tagged "front" or "back".
    /// </summary>
    public async Task<ExtractedLeadCardDto> MergeFrontAndBackAsync(ExtractedLeadCardDto front, ExtractedLeadCardDto back, CancellationToken ct = default)
    {
        var conflicts = new List<CardFieldConflictDto>();

        (string? Value, double? Confidence) Pick(string field, string? f, double? fc, string? b, double? bc, Func<string, string> normalise)
        {
            var hasF = !string.IsNullOrWhiteSpace(f);
            var hasB = !string.IsNullOrWhiteSpace(b);
            if (!hasF && !hasB) return (null, null);
            if (hasF && !hasB) return (f, fc);
            if (!hasF) return (b, bc);
            if (normalise(f!) == normalise(b!)) return (f, Math.Max(fc ?? 0, bc ?? 0));
            conflicts.Add(new CardFieldConflictDto(field, f!, b!));
            return (bc ?? 0) > (fc ?? 0) ? (b, bc) : (f, fc);
        }

        var company = Pick("Company", front.CompanyName, front.CompanyConfidence, back.CompanyName, back.CompanyConfidence, Letters);
        var name = Pick("Name", front.FullName, front.NameConfidence, back.FullName, back.NameConfidence, Letters);
        var title = Pick("Job title", front.JobTitle, front.TitleConfidence, back.JobTitle, back.TitleConfidence, Letters);
        var email = Pick("Email", front.Email, front.EmailConfidence, back.Email, back.EmailConfidence, e => e.Trim().ToLowerInvariant());
        var phone = Pick("Phone", front.Phone, front.PhoneConfidence, back.Phone, back.PhoneConfidence, PhoneKey);
        var website = Pick("Website", front.Website, null, back.Website, null, WebsiteKey);

        // First and last name follow whichever side the full name came from.
        var nameFromBack = name.Value is not null && !string.Equals(name.Value, front.FullName, StringComparison.Ordinal);
        var firstName = nameFromBack ? back.FirstName : front.FirstName ?? back.FirstName;
        var lastName = nameFromBack ? back.LastName : front.LastName ?? back.LastName;

        var address = MergeAddress(front.Address, back.Address);
        double? addressConf = null;
        if (!string.IsNullOrWhiteSpace(address))
        {
            var best = Math.Max(front.AddressConfidence ?? 0, back.AddressConfidence ?? 0);
            addressConf = best > 0 ? best : CardLineClassifier.AddressConfidence(address);
        }

        var tagged = new List<CardEvidenceBoxDto>();
        if (front.Evidence != null) tagged.AddRange(front.Evidence.Select(e => e with { Side = "front" }));
        if (back.Evidence != null) tagged.AddRange(back.Evidence.Select(e => e with { Side = "back" }));

        // A back that carries little (a logo, a tagline) must not drag a good front down.
        var backFields = new[] { back.Email, back.Phone, back.FullName, back.CompanyName, back.Address, back.Website }.Count(v => !string.IsNullOrWhiteSpace(v));
        var overall = backFields <= 1 ? front.OverallConfidence : Math.Round((front.OverallConfidence + back.OverallConfidence) / 2.0, 2);
        var engine = front.EngineConfidence.HasValue && back.EngineConfidence.HasValue
            ? Math.Round((front.EngineConfidence.Value + back.EngineConfidence.Value) / 2.0, 2)
            : front.EngineConfidence ?? back.EngineConfidence;

        var script = front.DetectedScript == back.DetectedScript ? front.DetectedScript : "Bilingual";

        // The duplicate check runs again on the merged values: the back may hold the email that finds the existing record.
        var duplicate = front.Duplicate ?? back.Duplicate;
        if (db != null)
        {
            duplicate = await FindDuplicateInCrmAsync(email.Value, phone.Value, company.Value, ct) ?? duplicate;
        }

        return new ExtractedLeadCardDto(
            CompanyName: company.Value ?? string.Empty,
            FirstName: firstName,
            LastName: lastName,
            FullName: name.Value,
            Email: email.Value,
            Phone: phone.Value,
            JobTitle: title.Value,
            Website: website.Value,
            Address: address,
            RawText: $"[Front]\n{front.RawText}\n[Back]\n{back.RawText}".Trim(),
            OverallConfidence: overall,
            CompanyConfidence: company.Confidence,
            NameConfidence: name.Confidence,
            EmailConfidence: email.Confidence,
            PhoneConfidence: phone.Confidence,
            TitleConfidence: title.Confidence,
            DetectedScript: script,
            Duplicate: duplicate,
            EngineConfidence: engine,
            Evidence: tagged.Count > 0 ? tagged : null,
            AddressConfidence: addressConf,
            Conflicts: conflicts.Count > 0 ? conflicts : null);
    }

    private static string? MergeAddress(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? null : b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        var pieces = new List<string>();
        foreach (var piece in a.Split(',').Concat(b.Split(',')).Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            if (!pieces.Any(existing => string.Equals(Letters(existing), Letters(piece), StringComparison.Ordinal))) pieces.Add(piece);
        }

        return string.Join(", ", pieces);
    }

    private static string Letters(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string PhoneKey(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length > 9 ? digits[^9..] : digits;
    }

    private static string WebsiteKey(string site)
        => Regex.Replace(site.Trim().ToLowerInvariant(), @"^(https?://)?(www\.)?|/+$", string.Empty);
}
