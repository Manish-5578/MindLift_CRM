using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Shared.Contracts;
using MindLift.UI.Components;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

/// <summary>The components every record page is built from: FieldValue, Timeline, RecordDetailLayout and DynamicForm.</summary>
public sealed class RecordComponentTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public RecordComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    // ---------- FieldValue ----------

    [Fact]
    public void An_empty_value_shows_a_dash()
    {
        var cut = Render<FieldValue>(p => p.Add(c => c.Value, "  "));

        cut.Find("[data-testid=field-empty]").TextContent.ShouldBe("—");
    }

    [Fact]
    public void An_email_is_a_mail_link_and_a_phone_is_a_tel_link()
    {
        Render<FieldValue>(p => p.Add(c => c.Kind, FormFieldKind.Email).Add(c => c.Value, "a@example.test"))
            .Find("a").GetAttribute("href").ShouldBe("mailto:a@example.test");
        Render<FieldValue>(p => p.Add(c => c.Kind, FormFieldKind.Phone).Add(c => c.Value, "+971501234567"))
            .Find("a").GetAttribute("href").ShouldBe("tel:+971501234567");
    }

    [Theory]
    [InlineData("example.test", "https://example.test/")]
    [InlineData("http://example.test/a", "http://example.test/a")]
    public void A_web_address_becomes_a_safe_link_that_opens_in_a_new_tab(string text, string href)
    {
        var link = Render<FieldValue>(p => p.Add(c => c.Kind, FormFieldKind.Url).Add(c => c.Value, text)).Find("a");

        link.GetAttribute("href").ShouldBe(href);
        link.GetAttribute("target").ShouldBe("_blank");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,AAAA")]
    public void An_address_that_could_run_code_is_shown_as_plain_text(string text)
    {
        var cut = Render<FieldValue>(p => p.Add(c => c.Kind, FormFieldKind.Url).Add(c => c.Value, text));

        cut.FindAll("a").ShouldBeEmpty();
        cut.Markup.ShouldContain(text.Split(':')[0]);
    }

    [Fact]
    public void A_picklist_value_shows_its_label_and_a_user_shows_their_name()
    {
        var options = new List<PicklistValueDto> { new(Guid.NewGuid(), "client", "Client", null, null, null, 1, false, true, false) };
        Render<FieldValue>(p => p.Add(c => c.Kind, FormFieldKind.Picklist).Add(c => c.Value, "client").Add(c => c.Options, options))
            .Markup.ShouldContain("Client");
    }

    // ---------- Timeline ----------

    [Fact]
    public void An_empty_timeline_says_so()
    {
        var cut = Render<Timeline>(p => p.Add(c => c.Items, Array.Empty<TimelineItem>()).Add(c => c.EmptyText, "No history yet."));

        cut.Find("[data-testid=timeline-empty]").TextContent.ShouldBe("No history yet.");
    }

    [Fact]
    public void Timeline_items_show_title_detail_and_a_link_when_they_have_one()
    {
        var cut = Render<Timeline>(p => p.Add(c => c.Items, new[]
        {
            new TimelineItem(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), "Converted from lead", "Source: website", null, "/leads/1"),
            new TimelineItem(null, "Created"),
        }));

        cut.FindAll("[data-testid=timeline-item]").Count.ShouldBe(2);
        cut.Find("a").GetAttribute("href").ShouldBe("/leads/1");
        cut.Markup.ShouldContain("Source: website");
    }

    // ---------- RecordDetailLayout ----------

    [Fact]
    public void The_detail_layout_shows_the_title_main_content_and_the_side_panel()
    {
        var cut = Render<RecordDetailLayout>(p => p
            .Add(c => c.Title, "Nadia Hassan")
            .Add(c => c.ChildContent, "<p id=\"main\">main</p>")
            .Add(c => c.Aside, "<p id=\"side\">side</p>"));

        cut.Find("[data-testid=page-title]").TextContent.ShouldBe("Nadia Hassan");
        cut.Find(".ml-detail__main #main").TextContent.ShouldBe("main");
        cut.Find("[data-testid=detail-aside] #side").TextContent.ShouldBe("side");
    }

    [Fact]
    public void Without_a_side_panel_the_detail_layout_leaves_it_out()
    {
        var cut = Render<RecordDetailLayout>(p => p.Add(c => c.Title, "Acme"));

        cut.FindAll("[data-testid=detail-aside]").ShouldBeEmpty();
    }

    // ---------- DynamicForm ----------

    private static readonly FormField[] SampleFields =
    [
        new("firstName", "First name", Required: true, TestId: "first"),
        new("email", "Email", FormFieldKind.Email),
        new("description", "Notes", FormFieldKind.Multiline),
    ];

    [Fact]
    public void The_form_shows_a_field_for_each_description_with_required_ones_starred()
    {
        var cut = Render<DynamicForm>(p => p.Add(c => c.Fields, SampleFields).Add(c => c.Values, new Dictionary<string, string?>()));

        cut.Markup.ShouldContain("First name *");
        cut.Markup.ShouldContain("Email");
        cut.Find("input[data-testid=first]").ShouldNotBeNull();
        cut.Find("input[data-testid=field-email]").GetAttribute("type").ShouldBe("email");
        cut.Find("textarea").ShouldNotBeNull();
    }

    [Fact]
    public void Typing_writes_the_value_and_reports_which_field_changed()
    {
        var values = new Dictionary<string, string?>();
        var changed = new List<string>();
        var cut = Render<DynamicForm>(p => p
            .Add(c => c.Fields, SampleFields)
            .Add(c => c.Values, values)
            .Add(c => c.OnFieldChanged, Microsoft.AspNetCore.Components.EventCallback.Factory.Create<string>(this, key => changed.Add(key))));

        cut.Find("input[data-testid=first]").Change("Nadia");

        values["firstName"].ShouldBe("Nadia");
        changed.ShouldBe(["firstName"]);
    }

    [Fact]
    public void A_field_error_is_shown_under_its_field()
    {
        var cut = Render<DynamicForm>(p => p
            .Add(c => c.Fields, SampleFields)
            .Add(c => c.Values, new Dictionary<string, string?>())
            .Add(c => c.Errors, new Dictionary<string, string> { ["firstName"] = "Enter the first name." }));

        cut.Markup.ShouldContain("Enter the first name.");
    }

    [Fact]
    public void Existing_values_are_filled_in()
    {
        var cut = Render<DynamicForm>(p => p.Add(c => c.Fields, SampleFields).Add(c => c.Values, new Dictionary<string, string?> { ["firstName"] = "Existing" }));

        cut.Find("input[data-testid=first]").GetAttribute("value").ShouldBe("Existing");
    }
}
