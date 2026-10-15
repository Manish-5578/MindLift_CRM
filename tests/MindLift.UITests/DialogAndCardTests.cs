using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MindLift.UI.Components;
using MindLift.UI.Localization;
using MudBlazor;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

public sealed class DialogAndCardTests : BunitContext, IAsyncLifetime
{
    // MudBlazor registers a service that can only be disposed asynchronously, so the test context must be too.
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public DialogAndCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddMindLiftLocalization();
    }

    private IRenderedComponent<MudDialogProvider> StartProviders()
    {
        Render<MudPopoverProvider>();
        return Render<MudDialogProvider>();
    }

    [Fact]
    public async Task Pressing_confirm_returns_true()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync("Move the deal?", "It will leave this stage.", "Move");
        provider.WaitForElement("[data-testid=confirm-ok]").Click();

        (await answer).ShouldBeTrue();
    }

    [Fact]
    public async Task Pressing_cancel_returns_false()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync("Move the deal?", "It will leave this stage.", "Move");
        provider.WaitForElement("[data-testid=confirm-cancel]").Click();

        (await answer).ShouldBeFalse();
    }

    [Fact]
    public async Task The_dialog_shows_the_title_message_and_confirm_text()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.ConfirmAsync("Delete this lead?", "This cannot be undone.", "Delete lead", danger: true);
        provider.WaitForElement("[data-testid=confirm-ok]");

        provider.Markup.ShouldContain("Delete this lead?");
        provider.Markup.ShouldContain("This cannot be undone.");
        provider.Find("[data-testid=confirm-ok]").TextContent.Trim().ShouldBe("Delete lead");
        provider.Find("[data-testid=confirm-cancel]").Click();
        await answer;
    }

    [Fact]
    public async Task A_dangerous_action_uses_the_error_colour_and_a_normal_one_does_not()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var danger = dialogs.ConfirmAsync("Delete?", "Sure?", "Delete", danger: true);
        provider.WaitForElement("[data-testid=confirm-ok]").ClassList.ShouldContain("mud-button-filled-error");
        provider.Find("[data-testid=confirm-cancel]").Click();
        await danger;

        var normal = dialogs.ConfirmAsync("Continue?", "Sure?", "Continue");
        provider.WaitForElement("[data-testid=confirm-ok]").ClassList.ShouldContain("mud-button-filled-primary");
        provider.Find("[data-testid=confirm-cancel]").Click();
        await normal;
    }

    [Fact]
    public async Task A_reason_prompt_returns_the_trimmed_text()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.PromptReasonAsync("Disqualify this lead?", "Say why.", confirmText: "Disqualify", danger: true);
        provider.WaitForElement("input[data-testid=reason-input], textarea[data-testid=reason-input]").Input("  No budget  ");
        provider.Find("[data-testid=reason-ok]").Click();

        (await answer).ShouldBe("No budget");
    }

    [Fact]
    public async Task A_reason_prompt_refuses_an_empty_reason_and_explains_why()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.PromptReasonAsync("Disqualify this lead?");
        provider.WaitForElement("[data-testid=reason-ok]").Click();

        provider.Markup.ShouldContain("A reason is required.");
        answer.IsCompleted.ShouldBeFalse();

        provider.Find("[data-testid=reason-cancel]").Click();
        (await answer).ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_a_reason_prompt_returns_null()
    {
        var provider = StartProviders();
        var dialogs = Services.GetRequiredService<IDialogService>();

        var answer = dialogs.PromptReasonAsync("Mark as lost?");
        provider.WaitForElement("[data-testid=reason-cancel]").Click();

        (await answer).ShouldBeNull();
    }

    [Fact]
    public void Skeleton_cards_default_to_four_and_follow_the_count()
    {
        Render<SkeletonCards>().FindAll(".ml-card").Count.ShouldBe(4);
        Render<SkeletonCards>(p => p.Add(c => c.Count, 2)).FindAll(".ml-card").Count.ShouldBe(2);
    }

    [Fact]
    public void A_section_card_shows_its_title_description_and_content()
    {
        var card = Render<SectionCard>(p => p
            .Add(c => c.Title, "Branding")
            .Add(c => c.Description, "Logo and colour")
            .AddChildContent("<span id=\"inner\">fields</span>"));

        card.Markup.ShouldContain("Branding");
        card.Markup.ShouldContain("Logo and colour");
        card.Find("#inner").TextContent.ShouldBe("fields");
    }

    [Fact]
    public void A_card_with_a_click_handler_is_clickable_and_calls_it()
    {
        var clicks = 0;
        var card = Render<MlCard>(p => p
            .Add(c => c.Title, "Open deals")
            .Add(c => c.OnClick, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => clicks++)));

        card.Find(".ml-card").ClassList.ShouldContain("ml-card--clickable");
        card.Find(".ml-card").Click();

        clicks.ShouldBe(1);
    }

    [Fact]
    public void A_card_without_a_click_handler_is_not_marked_clickable()
    {
        Render<MlCard>(p => p.Add(c => c.Title, "Plain")).Find(".ml-card").ClassList.ShouldNotContain("ml-card--clickable");
    }

    [Fact]
    public void A_section_card_without_a_description_leaves_the_line_out()
    {
        var card = Render<SectionCard>(p => p.Add(c => c.Title, "Details"));

        card.FindAll(".ml-muted").ShouldBeEmpty();
    }
}
