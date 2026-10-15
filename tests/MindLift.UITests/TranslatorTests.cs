using System.Text.RegularExpressions;
using MindLift.UI.Localization;
using Shouldly;

namespace MindLift.UITests;

/// <summary>The translator and the Arabic file behind every screen's text.</summary>
public sealed class TranslatorTests
{
    [Fact]
    public void English_is_returned_as_written()
    {
        var translator = new Translator();

        translator["Save"].ShouldBe("Save");
        translator["A phrase nobody translated"].ShouldBe("A phrase nobody translated");
    }

    [Fact]
    public void Arabic_is_returned_when_there_is_an_entry_and_English_when_there_is_not()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");

        translator.IsArabic.ShouldBeTrue();
        translator["Save"].ShouldBe("حفظ");
        translator["A phrase nobody translated"].ShouldBe("A phrase nobody translated");
    }

    [Fact]
    public void Placeholders_are_filled_in_for_both_languages()
    {
        var translator = new Translator();

        translator["Delete {0}?", "Acme"].ShouldBe("Delete Acme?");
        translator.SetLanguage("ar");
        translator["Delete {0}?", "Acme"].ShouldContain("Acme");
    }

    [Fact]
    public void Switching_language_raises_Changed_once_and_does_nothing_when_the_language_is_the_same()
    {
        var translator = new Translator();
        var raised = 0;
        translator.Changed += () => raised++;

        translator.SetLanguage("ar");
        translator.SetLanguage("ar");
        translator.SetLanguage("en");
        translator.SetLanguage("fr");

        raised.ShouldBe(2);
        translator.Language.ShouldBe("en");
    }

    [Fact]
    public void An_unknown_language_is_treated_as_English()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");

        translator.SetLanguage(null);

        translator.IsArabic.ShouldBeFalse();
    }

    [Fact]
    public void The_Arabic_file_is_loaded_from_the_library()
    {
        Translator.ArabicEntries.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Every_Arabic_entry_has_text_and_keeps_the_placeholders_of_its_English_phrase()
    {
        var placeholder = new Regex(@"\{\d+\}");

        foreach (var (english, arabic) in Translator.ArabicEntries)
        {
            arabic.Trim().ShouldNotBeEmpty($"'{english}' has no Arabic text");
            var wanted = placeholder.Matches(english).Select(m => m.Value).OrderBy(v => v).ToList();
            var found = placeholder.Matches(arabic).Select(m => m.Value).OrderBy(v => v).ToList();
            found.ShouldBe(wanted, $"'{english}' and its Arabic must use the same placeholders");
        }
    }

    [Fact]
    public void Arabic_entries_contain_Arabic_letters_unless_the_phrase_is_a_name_or_a_symbol()
    {
        var arabicLetter = new Regex(@"[؀-ۿ]");

        foreach (var (english, arabic) in Translator.ArabicEntries)
        {
            if (english.Equals(arabic, StringComparison.Ordinal))
            {
                continue; // Brand names, abbreviations and symbols are the same in both languages.
            }

            arabicLetter.IsMatch(arabic).ShouldBeTrue($"'{english}' is translated to '{arabic}', which has no Arabic letters");
        }
    }

    [Fact]
    public void The_profile_language_applies_until_the_person_chooses_one()
    {
        var translator = new Translator();

        translator.UseDefault("ar");
        translator.IsArabic.ShouldBeTrue();

        translator.Choose("en");
        translator.IsArabic.ShouldBeFalse();

        // A later bootstrap (the profile loading again) must not undo what the person picked.
        translator.UseDefault("ar");
        translator.IsArabic.ShouldBeFalse();
    }

    [Fact]
    public void A_saved_choice_made_before_the_profile_loads_wins_over_the_profile()
    {
        var translator = new Translator();

        translator.Choose("ar");
        translator.UseDefault("en");

        translator.IsArabic.ShouldBeTrue();
    }
}
