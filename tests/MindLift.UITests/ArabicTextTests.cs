using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using MindLift.UI.Localization;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

/// <summary>The shared components show Arabic when the translator is switched to it, and stay in English otherwise.</summary>
public sealed class ArabicTextTests : BunitContext
{
    public ArabicTextTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddMindLiftLocalization();
    }

    private void UseArabic() => Services.GetRequiredService<Translator>().SetLanguage("ar");

    [Fact]
    public void An_empty_state_shows_its_title_in_arabic_and_in_english()
    {
        Render<EmptyState>(p => p.Add(c => c.Title, "Nothing here yet")).Markup.ShouldContain("Nothing here yet");

        UseArabic();

        Render<EmptyState>(p => p.Add(c => c.Title, "Nothing here yet")).Markup.ShouldContain("لا يوجد شيء هنا بعد");
    }

    [Fact]
    public void A_title_with_no_arabic_entry_is_shown_as_written_not_blank()
    {
        UseArabic();

        Render<EmptyState>(p => p.Add(c => c.Title, "No synthetic widgets yet")).Markup.ShouldContain("No synthetic widgets yet");
    }

    [Fact]
    public void The_error_state_defaults_and_retry_button_are_in_arabic()
    {
        UseArabic();

        var error = Render<ErrorState>(p => p.Add(c => c.OnRetry, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { })));

        error.Markup.ShouldContain("حدث خطأ ما");
        error.Find("[data-testid=error-retry]").TextContent.ShouldContain("حاول مرة أخرى");
    }

    [Fact]
    public void A_filter_chip_reads_in_arabic_when_given_the_translator()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");
        var field = new FieldDto("created_at", "Created", null, FieldType.Date, false, false, false, true, null, null);
        var condition = new FilterCondition { Field = "created_at", Op = FilterOperator.DatePreset, Value = JsonSerializer.SerializeToElement("ThisWeek") };

        var english = FilterDescriber.Describe(condition, field);
        var arabic = FilterDescriber.Describe(condition, field, translator: translator);

        english.ShouldBe("Created is in This week");
        arabic.ShouldContain("ضمن");
        arabic.ShouldContain("هذا الأسبوع");
    }

    [Fact]
    public void A_between_filter_joins_its_two_values_with_the_arabic_word_for_and()
    {
        var translator = new Translator();
        translator.SetLanguage("ar");
        var field = new FieldDto("amount", "Amount", null, FieldType.Number, false, false, false, true, null, null);
        var condition = new FilterCondition
        {
            Field = "amount",
            Op = FilterOperator.Between,
            Value = JsonSerializer.SerializeToElement(new { from = 10, to = 20 }),
        };

        FilterDescriber.Describe(condition, field, translator: translator).ShouldContain("10 و 20");
    }
}
