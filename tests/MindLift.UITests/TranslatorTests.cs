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

    [Fact]
    public void The_Arabic_source_file_has_no_two_names_that_differ_only_by_letter_case()
    {
        // A resource file keeps just one of two such names and drops the other without a warning, so a translation would silently go missing.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull();
        var file = Path.Combine(dir.FullName, "src", "MindLift.UI", "Localization", "Arabic.resx");
        var names = System.Xml.Linq.XDocument.Load(file).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToList();

        names.Count.ShouldBeGreaterThan(0);
        names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => string.Join(" / ", g)).ShouldBeEmpty();
        Translator.ArabicEntries.Count.ShouldBe(names.Count, "every name in the file should reach the translator");
    }

    [Fact]
    public void A_phrase_is_found_whatever_its_letter_case()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");

        translator["contacts"].ShouldBe(translator["Contacts"]);
        translator["Contacts"].ShouldBe("جهات الاتصال");
    }

    [Fact]
    public void A_stored_arabic_name_wins_in_arabic_and_is_ignored_in_english()
    {
        var translator = new Translator();

        translator.Pick("Zebra stage", "مرحلة مخصصة").ShouldBe("Zebra stage");

        translator.SetLanguage("ar");
        translator.Pick("Zebra stage", "مرحلة مخصصة").ShouldBe("مرحلة مخصصة");
        translator.Pick("Zebra stage", "  ").ShouldBe("Zebra stage");
        translator.Pick("Zebra stage", null).ShouldBe("Zebra stage");
    }

    [Fact]
    public void Without_a_stored_arabic_name_the_dictionary_is_used_and_then_the_english_name()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");

        translator.Pick("Contacts", null).ShouldBe("جهات الاتصال");
        translator.Pick("A stage nobody translated", null).ShouldBe("A stage nobody translated");
    }

    [Fact]
    public void The_names_the_server_already_stores_in_arabic_are_in_the_dictionary_too()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");

        // From the built-in picklists and the industry templates.
        translator["Contacted"].ShouldBe("تم التواصل");
        translator["Referral"].ShouldBe("إحالة");
        translator["Viewing"].ShouldBe("معاينة");
    }
}
