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

    private static readonly string[] AddressKeywords =
    [
        "street", "road", "floor", "tower", "box", "p.o.", "building", "avenue", "suite",
        "dubai", "abu dhabi", "uae", "riyadh", "doha", "sharjah", "jeddah", "kuwait", "manama", "muscat",
        // Arabic Address terms
        "شارع", "برج", "بناية", "صندوق بريد", "طابق", "مكتب", "دبي", "أبوظبي", "الرياض", "جدة", "الدوحة", "المنامة", "الكويت", "مسقط"
    ];

    public async Task<ExtractedLeadCardDto> ExtractFromFileStreamAsync(
        Stream stream,
        string fileName,
        CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        return await ExtractFromBytesAsync(bytes, Path.GetExtension(fileName), null, ct);
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

        return await ParseTextAndCheckDuplicatesAsync(clientText, ct);
    }

    private async Task<ExtractedLeadCardDto> ExtractFromBytesAsync(
        byte[] bytes,
        string extension,
        string? clientText,
        CancellationToken ct)
    {
        ValidateImageHeader(bytes);

        string? serverText = null;
        if (pageReader != null)
        {
            var ext = DetectExtension(bytes, extension);
            var tempFile = Path.Combine(Path.GetTempPath(), $"card_{Guid.NewGuid():N}{ext}");
            try
            {
                await File.WriteAllBytesAsync(tempFile, bytes, ct);
                var pages = await pageReader.ReadAsync(tempFile, ct);
                var ocrLines = pages
                    .SelectMany(p => (p.Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0);

                serverText = string.Join("\n", ocrLines).Trim();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Server OCR engine failed to process image bytes.");
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch
                {
                    // cleanup ignore
                }
            }
        }

        // Server OCR is authoritative; fallback to clientText if server OCR produced nothing
        var chosenText = !string.IsNullOrWhiteSpace(serverText) ? serverText : (clientText ?? string.Empty);

        if (string.IsNullOrWhiteSpace(chosenText))
        {
            throw new ValidationException("empty_card_text", "No readable text could be recognized from the card. Please ensure the card is clear, well lit, and in focus.");
        }

        return await ParseTextAndCheckDuplicatesAsync(chosenText, ct);
    }

    private async Task<ExtractedLeadCardDto> ParseTextAndCheckDuplicatesAsync(
        string rawText,
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

            var matchPhone = PhoneRegex.Match(line);
            if (matchPhone.Success && phone == null && matchPhone.Value.Length >= 7)
            {
                phone = NormalizePhoneNumber(matchPhone.Value);
                continue;
            }

            var lower = line.ToLowerInvariant();
            if (jobTitle == null && TitleKeywords.Any(k => lower.Contains(k)))
            {
                jobTitle = line;
                continue;
            }

            if (companyName == null && CompanyKeywords.Any(k => lower.Contains(k)))
            {
                companyName = line;
                continue;
            }

            if (AddressKeywords.Any(k => lower.Contains(k)))
            {
                address = (address == null ? line : $"{address}, {line}");
                continue;
            }

            remainingLines.Add(line);
        }

        // Identify person name and company from remaining lines
        foreach (var line in remainingLines)
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fullName == null && words.Length >= 2 && words.Length <= 4 && !words.Any(w => char.IsDigit(w[0])))
            {
                fullName = line;
            }
            else if (companyName == null && words.Length >= 1 && words.Length <= 6)
            {
                companyName = line;
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
        var nameConf = !string.IsNullOrWhiteSpace(fullName) ? 0.85 : (double?)null;
        var titleConf = !string.IsNullOrWhiteSpace(jobTitle) ? 0.88 : (double?)null;

        var scores = new[] { emailConf, phoneConf, companyConf, nameConf, titleConf }.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var overallConfidence = scores.Count > 0 ? Math.Round(scores.Average(), 2) : 0.70;

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
            Duplicate: duplicate
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
}
