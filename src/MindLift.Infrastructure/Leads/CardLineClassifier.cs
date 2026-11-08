using System.Text.RegularExpressions;

namespace MindLift.Infrastructure.Leads;

/// <summary>
/// Decides what a line read from a business card is: a person's name, or part of an address. A name is only trusted when nothing about the line
/// looks like an address (landmark, PIN code, street words, a label such as "Landmark:"), so "Landmark: Near ASC" is never the contact person.
/// </summary>
public static partial class CardLineClassifier
{
    /// <summary>Words that only appear in addresses. One of them is enough.</summary>
    private static readonly HashSet<string> StrongTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "landmark", "near", "nxt", "opp", "opposite", "behind", "beside",
        "street", "st.", "road", "rd.", "avenue", "ave", "lane", "ln",
        "floor", "fl.", "building", "bldg", "tower", "block", "blk", "sector", "phase", "plot", "no.", "suite", "apt",
        "box", "p.o.", "po", "nagar", "layout", "colony", "district", "taluk", "taluka", "mandal", "pincode", "zipcode", "zip",
        "village", "enclave", "marg", "cross", "chowk", "gali", "highway", "hwy", "junction", "flat", "door",
        "شارع", "برج", "بناية", "صندوق", "طابق", "مكتب", "مبنى", "ص.ب",
    };

    /// <summary>Words that are usually address words but are also common in names or company names. They count only beside a digit or a comma.</summary>
    private static readonly HashSet<string> WeakTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "main", "area", "park", "town", "locality", "extension", "ext.", "estate", "circle", "next", "num", "pin", "city",
    };

    /// <summary>Cities, states and countries. A line made only of these is an address; a name that happens to share one ("Delhi Kumar") is not.</summary>
    private static readonly HashSet<string> PlaceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bengaluru", "bangalore", "mumbai", "delhi", "chennai", "hyderabad", "pune", "kolkata", "ahmedabad", "surat", "jaipur", "lucknow",
        "india", "karnataka", "maharashtra", "tamilnadu", "telangana", "kerala", "gujarat", "rajasthan", "pradesh", "nadu",
        "dubai", "abu", "dhabi", "uae", "riyadh", "doha", "sharjah", "jeddah", "kuwait", "manama", "muscat", "ajman", "fujairah",
        "دبي", "أبوظبي", "الرياض", "جدة", "الدوحة", "المنامة", "الكويت", "مسقط", "الشارقة", "عجمان",
    };

    private static readonly HashSet<string> AddressLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "addr", "add", "landmark", "location", "loc", "reg. office", "regd. office", "registered office", "head office", "branch", "po box", "p.o. box", "pin", "pincode",
        "العنوان",
    };

    private static readonly HashSet<string> OtherLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "contact", "mobile", "mob", "cell", "tel", "telephone", "phone", "ph", "fax", "email", "e-mail", "mail", "web", "website", "whatsapp", "office", "direct", "m", "t", "e", "w",
    };

    private static readonly string[] Honorifics = ["mr", "mrs", "ms", "miss", "dr", "eng", "prof", "sir", "shri", "smt", "sheikh", "السيد", "الدكتور", "المهندس"];

    [GeneratedRegex(@"(?<![\d+])[1-9]\d{2}\s?\d{3}(?!\d)")]
    private static partial Regex IndianPinCode();

    [GeneratedRegex(@"\d")]
    private static partial Regex AnyDigit();

    /// <summary>Splits "Label: value" at the first colon. Returns false when the line has no short label before a colon.</summary>
    public static bool TrySplitLabel(string line, out string label, out string value)
    {
        label = string.Empty;
        value = line;
        var colon = line.IndexOf(':');
        if (colon <= 0 || colon > 24) return false;
        label = line[..colon].Trim().TrimEnd('.');
        value = line[(colon + 1)..].Trim();
        return label.Length > 0;
    }

    /// <summary>True when the line carries a phone label ("Tel:", "Mob") or starts like a phone number, so a six-digit group in it is not a PIN code.</summary>
    public static bool LooksLikePhoneLine(string line)
    {
        if (TrySplitLabel(line, out var label, out _) && OtherLabels.Contains(label)) return true;
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('+') || Regex.IsMatch(trimmed, @"^\(?\d{2,4}\)?[\s\-.]\d{3,5}[\s\-.]\d{3,5}");
    }

    public static bool HasPinCode(string line) => IndianPinCode().IsMatch(line);

    /// <summary>
    /// True when the line is part of an address. <paramref name="hasCompanyWord"/> says the line also holds a company word (LLC, Ltd, Trading...):
    /// such a line is a company unless it has clearly address-only evidence, so "Dubai Gold Trading LLC" stays a company.
    /// </summary>
    public static bool IsAddressLine(string line, bool hasCompanyWord = false)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        if (TrySplitLabel(line, out var label, out _))
        {
            if (AddressLabels.Contains(label)) return true;
            if (OtherLabels.Contains(label)) return false;
        }

        var tokens = Tokens(line);
        var hasDigit = AnyDigit().IsMatch(line);
        var hasStrong = tokens.Any(StrongTokens.Contains);
        var hasPin = HasPinCode(line) && !LooksLikePhoneLine(line);

        if (hasStrong || hasPin) return true;
        if (hasCompanyWord) return false;

        var commas = line.Count(c => c == ',' || c == '،');
        if (tokens.Any(WeakTokens.Contains) && (hasDigit || commas > 0)) return true;

        // "Bengaluru, Karnataka" or "Dubai - UAE": nothing but place names and joining words.
        var placeTokens = tokens.Count(PlaceNames.Contains);
        if (placeTokens > 0 && placeTokens == tokens.Count(t => !IsJoiner(t))) return true;
        if (placeTokens > 0 && (hasDigit || commas > 0)) return true;

        // "12, 3rd Cross, HSR": several comma-separated pieces with a number in them.
        return hasDigit && commas >= 1 && tokens.Count >= 3;
    }

    /// <summary>True when the line can be a person's name: 2 to 5 word-like tokens, no digits or symbols, and nothing that marks it as an address.</summary>
    public static bool IsLikelyPersonName(string line, bool hasCompanyWord = false)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.Contains(':') || line.Contains('@') || AnyDigit().IsMatch(line)) return false;
        if (IsAddressLine(line, hasCompanyWord)) return false;

        var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2 || tokens.Length > 5) return false;

        foreach (var token in tokens)
        {
            var cleaned = token.Trim('.', '-', '\'', ',');
            if (cleaned.Length == 0) continue;
            if (!cleaned.All(c => char.IsLetter(c) || c == '-' || c == '\'' || c == '.')) return false;
        }

        return true;
    }

    /// <summary>
    /// How far a name guess can be trusted. A name that lines up with the email address (zaid@... for Zaid Al-Mansoor) or carries an honorific is
    /// strong; a name taken only because it was the left-over line is weak, so the person is asked to check it.
    /// </summary>
    public static double NameConfidence(string fullName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var local = Letters(email.Split('@')[0]);
            foreach (var part in fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Letters(part);
                if (p.Length >= 3 && local.Contains(p, StringComparison.Ordinal)) return 0.85;
                if (p.Length >= 1 && local.Length >= 3 && p.Length >= 1 && local.StartsWith(p, StringComparison.Ordinal) && p.Length >= 3) return 0.85;
            }
        }

        var first = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.').ToLowerInvariant();
        return first is not null && Honorifics.Contains(first) ? 0.80 : 0.60;
    }

    /// <summary>How far an address can be trusted: a PIN code or several address words make it solid, a single weak hint does not.</summary>
    public static double AddressConfidence(string address)
    {
        var tokens = Tokens(address);
        var evidence = tokens.Count(StrongTokens.Contains) + tokens.Count(PlaceNames.Contains) + (HasPinCode(address) ? 2 : 0);
        return evidence >= 3 ? 0.90 : evidence == 2 ? 0.80 : 0.60;
    }

    private static List<string> Tokens(string line)
        => line.ToLowerInvariant().Split([' ', ',', '،', ';', '-', '/', '\\', '|', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool IsJoiner(string token) => token is "and" or "&" or "of" or "the" or "و";

    private static string Letters(string s) => new(s.Where(char.IsLetter).ToArray().Select(char.ToLowerInvariant).ToArray());
}
