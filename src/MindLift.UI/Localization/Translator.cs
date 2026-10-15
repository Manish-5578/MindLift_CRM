using System.Collections;
using System.Globalization;
using System.Resources;

namespace MindLift.UI.Localization;

/// <summary>
/// Looks up the text a screen shows. The English wording is the key, so a screen reads <c>T["Save"]</c> and English needs no entry at all.
/// Arabic text comes from <c>Localization/Arabic.resx</c>; a phrase with no Arabic entry yet is shown in English instead of a blank or a key.
/// </summary>
public sealed class Translator
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ArabicText = new(LoadArabic);

    private bool _chosen;

    public string Language { get; private set; } = "en";

    public bool IsArabic => Language == "ar";

    /// <summary>Raised when the language changes, so a screen showing text can draw it again.</summary>
    public event Action? Changed;

    /// <summary>Every Arabic entry, keyed by the English wording. Used by tests and by tools that check the translations.</summary>
    public static IReadOnlyDictionary<string, string> ArabicEntries => ArabicText.Value;

    /// <summary>The text for an English phrase in the current language.</summary>
    public string this[string english]
        => IsArabic && ArabicText.Value.TryGetValue(english, out var arabic) && arabic.Length > 0 ? arabic : english;

    /// <summary>The text for an English phrase with placeholders such as <c>{0}</c>, filled in with the given values.</summary>
    public string this[string english, params object?[] values]
        => string.Format(CultureInfo.InvariantCulture, this[english], values);

    /// <summary>The language a person picked themselves (the language switch, or a preference saved in the browser). It outranks the profile's language.</summary>
    public void Choose(string? language)
    {
        _chosen = true;
        SetLanguage(language);
    }

    /// <summary>The language saved on the person's profile. It applies only until they pick one themselves.</summary>
    public void UseDefault(string? language)
    {
        if (!_chosen)
        {
            SetLanguage(language);
        }
    }

    /// <summary>Switches between English ("en") and Arabic ("ar"). Anything else is treated as English.</summary>
    public void SetLanguage(string? language)
    {
        var next = language == "ar" ? "ar" : "en";
        if (next == Language)
        {
            return;
        }

        Language = next;
        Changed?.Invoke();
    }

    private static IReadOnlyDictionary<string, string> LoadArabic()
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var assembly = typeof(Translator).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith("Localization.Arabic.resources", StringComparison.Ordinal));
        if (resource is null)
        {
            return entries;
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                entries[key] = value;
            }
        }

        return entries;
    }
}
