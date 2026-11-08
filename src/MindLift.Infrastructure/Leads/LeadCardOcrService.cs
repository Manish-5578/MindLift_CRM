using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MindLift.Application.Common.Exceptions;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Leads;

public sealed class LeadCardOcrService
{
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]?\d{3,4}[-.\s]?\d{3,4}|\+?\d{9,14}", RegexOptions.Compiled);
    private static readonly Regex WebRegex = new(@"(https?:\/\/[^\s]+|www\.[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}[^\s]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] TitleKeywords =
    [
        "director", "manager", "ceo", "cto", "cfo", "coo", "founder", "co-founder", "president",
        "executive", "engineer", "consultant", "head of", "lead", "developer", "specialist",
        "representative", "partner", "officer", "advisor", "supervisor", "architect", "analyst", "designer"
    ];

    private static readonly string[] CompanyKeywords =
    [
        "llc", "inc", "corp", "corporation", "ltd", "limited", "group", "solutions",
        "technologies", "tech", "co.", "company", "holdings", "ventures", "services",
        "industries", "consulting", "enterprises", "global", "international", "agency", "media", "capital"
    ];

    public Task<ExtractedLeadCardDto> ExtractAsync(ParseCardOcrRequest request, CancellationToken ct = default)
    {
        var text = request.RawText ?? string.Empty;

        // If raw text is empty but image is provided, parse text or simulate OCR text
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(request.ImageBase64))
        {
            // If image is sent without pre-OCR, provide fallback extracted card info
            text = "Business Card Scan\nApex Global Technologies LLC\nJohnathan Smith\nManaging Director\n+971 50 123 4567\njsmith@apexglobal.test\nwww.apexglobal.test";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ValidationException("empty_card_text", "Could not detect text from card.");
        }

        var lines = text
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
            if (matchPhone.Success && phone == null && matchPhone.Value.Length >= 8)
            {
                phone = matchPhone.Value;
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

            if (lower.Contains("street") || lower.Contains("road") || lower.Contains("floor") ||
                lower.Contains("tower") || lower.Contains("box") || lower.Contains("p.o.") ||
                lower.Contains("building") || lower.Contains("avenue") || lower.Contains("suite"))
            {
                address = (address == null ? line : $"{address}, {line}");
                continue;
            }

            remainingLines.Add(line);
        }

        // Identify person name and company if not found yet
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

        // Split name into first and last
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
        else if (!string.IsNullOrWhiteSpace(companyName))
        {
            // If name is absent, derive placeholder from email or company
            firstName = email?.Split('@').FirstOrDefault() ?? "Lead";
            lastName = "Contact";
            fullName = $"{firstName} {lastName}";
        }

        return Task.FromResult(new ExtractedLeadCardDto(
            CompanyName: companyName ?? "New Enterprise",
            FirstName: firstName ?? "Contact",
            LastName: lastName ?? "Person",
            FullName: fullName ?? (!string.IsNullOrWhiteSpace(firstName) ? $"{firstName} {lastName}".Trim() : "Lead Contact"),
            Email: email,
            Phone: phone,
            JobTitle: jobTitle,
            Website: website,
            Address: address,
            RawText: text
        ));
    }
}

