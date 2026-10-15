using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MindLift.UI.Components;
using MindLift.UI.Localization;
using MudBlazor;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

public sealed class KanbanBoardTests : BunitContext, IAsyncLifetime
{
    private sealed record Item(string Name, bool Locked = false);

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public KanbanBoardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddMindLiftLocalization();
    }

    private static readonly IReadOnlyList<KanbanColumn> Columns =
    [
        new("new", "New", "#4F46E5", 3),
        new("working", "Working", "#F59E0B", 1, "AED 10K"),
        new("done", "Done", null, 0),
    ];

    private static IReadOnlyDictionary<string, IReadOnlyList<Item>> Items() => new Dictionary<string, IReadOnlyList<Item>>
    {
        ["new"] = [new("Ana"), new("Ben", Locked: true)],
        ["working"] = [new("Cy")],
    };

    private IRenderedComponent<MudPopoverProvider>? _popovers;

    private (IRenderedComponent<KanbanBoard<Item>> Board, List<KanbanMove<Item>> Moves) Create(Action<ComponentParameterCollectionBuilder<KanbanBoard<Item>>>? configure = null)
    {
        _popovers = Render<MudPopoverProvider>();
        var moves = new List<KanbanMove<Item>>();
        var board = Render<KanbanBoard<Item>>(p =>
        {
            p.Add(c => c.Columns, Columns)
             .Add(c => c.Items, Items())
             .Add(c => c.Card, item => b => b.AddMarkupContent(0, $"<span class=\"name\">{item.Name}</span>"))
             .Add(c => c.OnMove, EventCallback.Factory.Create<KanbanMove<Item>>(this, m => moves.Add(m)));
            configure?.Invoke(p);
        });
        return (board, moves);
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> CardsIn(IRenderedComponent<KanbanBoard<Item>> board, string column)
        => board.Find($"[data-testid=kanban-column-{column}]").QuerySelectorAll("[data-testid=kanban-card]").ToList();

    [Fact]
    public void Each_column_shows_its_name_count_and_cards()
    {
        var (board, _) = Create();

        board.Find("[data-testid=kanban-column-new]").TextContent.ShouldContain("New");
        CardsIn(board, "new").Count.ShouldBe(2);
        CardsIn(board, "working").Count.ShouldBe(1);
        CardsIn(board, "done").ShouldBeEmpty();
        board.Find("[data-testid=kanban-count-new]").TextContent.ShouldBe("3");
        board.Find("[data-testid=kanban-count-working]").TextContent.ShouldBe("1 · AED 10K");
    }

    [Fact]
    public void The_count_can_be_larger_than_the_cards_loaded()
    {
        var (board, _) = Create();

        // 3 in the column, 2 loaded. The header shows the real number.
        board.Find("[data-testid=kanban-count-new]").TextContent.ShouldBe("3");
        CardsIn(board, "new").Count.ShouldBe(2);
    }

    [Fact]
    public void An_empty_column_says_so()
    {
        var (board, _) = Create(p => p.Add(c => c.EmptyText, "No leads here"));

        board.Find("[data-testid=kanban-column-done]").TextContent.ShouldContain("No leads here");
    }

    [Fact]
    public void Dropping_a_card_on_another_column_reports_the_move()
    {
        var (board, moves) = Create();

        CardsIn(board, "new")[0].DragStart();
        board.Find("[data-testid=kanban-column-working]").DragOver();
        board.Find("[data-testid=kanban-column-working]").Drop();

        var move = moves.ShouldHaveSingleItem();
        move.Item.Name.ShouldBe("Ana");
        move.FromKey.ShouldBe("new");
        move.ToKey.ShouldBe("working");
    }

    [Fact]
    public void Dropping_a_card_back_on_its_own_column_does_nothing()
    {
        var (board, moves) = Create();

        CardsIn(board, "new")[0].DragStart();
        board.Find("[data-testid=kanban-column-new]").Drop();

        moves.ShouldBeEmpty();
    }

    [Fact]
    public void Dropping_when_nothing_is_being_dragged_does_nothing()
    {
        var (board, moves) = Create();

        board.Find("[data-testid=kanban-column-working]").Drop();

        moves.ShouldBeEmpty();
    }

    [Fact]
    public void A_drag_that_is_dropped_only_moves_one_card_once()
    {
        var (board, moves) = Create();

        CardsIn(board, "new")[0].DragStart();
        board.Find("[data-testid=kanban-column-working]").Drop();
        board.Find("[data-testid=kanban-column-done]").Drop();

        moves.Count.ShouldBe(1);
    }

    [Fact]
    public void The_column_under_a_dragged_card_is_highlighted_until_it_is_dropped()
    {
        var (board, _) = Create();
        var working = () => board.Find("[data-testid=kanban-column-working]");

        working().ClassList.ShouldNotContain("ml-kanban__col--over");
        CardsIn(board, "new")[0].DragStart();
        working().DragEnter();
        working().ClassList.ShouldContain("ml-kanban__col--over");

        working().Drop();
        working().ClassList.ShouldNotContain("ml-kanban__col--over");
    }

    [Fact]
    public void Leaving_a_column_or_ending_the_drag_removes_the_highlight_and_the_lifted_card()
    {
        var (board, moves) = Create();
        CardsIn(board, "new")[0].DragStart();
        board.Find("[data-testid=kanban-column-working]").DragEnter();
        CardsIn(board, "new")[0].ClassList.ShouldContain("ml-kanban__card--dragging");

        board.Find("[data-testid=kanban-column-working]").DragLeave();
        board.Find("[data-testid=kanban-column-working]").ClassList.ShouldNotContain("ml-kanban__col--over");

        CardsIn(board, "new")[0].DragEnd();
        CardsIn(board, "new")[0].ClassList.ShouldNotContain("ml-kanban__card--dragging");
        moves.ShouldBeEmpty();
    }

    [Fact]
    public void A_locked_card_cannot_be_dragged()
    {
        var (board, moves) = Create(p => p.Add(c => c.IsLocked, item => item.Locked));

        CardsIn(board, "new")[1].DragStart();
        board.Find("[data-testid=kanban-column-working]").Drop();

        moves.ShouldBeEmpty();
    }

    [Fact]
    public void A_card_can_be_moved_with_its_menu_for_phones()
    {
        var (board, moves) = Create();

        CardsIn(board, "new")[0].QuerySelector("button[aria-label='Move this card']")!.Click();
        _popovers!.WaitForElement("[data-testid=kanban-move-done]").Click();

        var move = moves.ShouldHaveSingleItem();
        move.Item.Name.ShouldBe("Ana");
        move.ToKey.ShouldBe("done");
    }

    [Fact]
    public void The_menu_does_not_offer_the_column_the_card_is_already_in()
    {
        var (board, _) = Create();

        CardsIn(board, "new")[0].QuerySelector("button[aria-label='Move this card']")!.Click();
        _popovers!.WaitForElement("[data-testid=kanban-move-working]");

        _popovers!.FindAll("[data-testid=kanban-move-new]").ShouldBeEmpty();
    }

    [Fact]
    public void On_a_phone_only_the_chosen_column_is_shown_and_the_picker_switches_it()
    {
        var (board, _) = Create();
        board.Find("[data-testid=kanban-column-new]").ClassList.ShouldNotContain("ml-hide-xs");
        board.Find("[data-testid=kanban-column-working]").ClassList.ShouldContain("ml-hide-xs");

        board.Find("[data-testid=kanban-pick-working]").Click();

        board.Find("[data-testid=kanban-column-working]").ClassList.ShouldNotContain("ml-hide-xs");
        board.Find("[data-testid=kanban-column-new]").ClassList.ShouldContain("ml-hide-xs");
    }

    [Fact]
    public void The_picker_shows_each_columns_total()
    {
        var (board, _) = Create();

        board.Find("[data-testid=kanban-pick-new]").TextContent.ShouldContain("New (3)");
    }

    [Fact]
    public void A_column_footer_is_shown_in_every_column()
    {
        var (board, _) = Create(p => p.Add(c => c.ColumnFooter, column => b => b.AddMarkupContent(0, $"<button class=\"more\">More {column.Label}</button>")));

        board.FindAll("button.more").Count.ShouldBe(3);
        board.Find("[data-testid=kanban-column-working]").QuerySelector("button.more")!.TextContent.ShouldBe("More Working");
    }
}
