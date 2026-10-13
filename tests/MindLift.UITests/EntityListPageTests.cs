using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using MudBlazor;
using MudBlazor.Services;
using FieldType = MindLift.Shared.Filtering.FieldType;
using FilterOperator = MindLift.Shared.Filtering.FilterOperator;
using Shouldly;

namespace MindLift.UITests;

public sealed class EntityListPageTests : BunitContext, IAsyncLifetime
{
    private sealed record Row(Guid Id, string Name, string Status);

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public EntityListPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private static readonly IReadOnlyList<ListColumn<Row>> Columns =
    [
        new() { Header = "Name", SortField = "name", Cell = r => b => b.AddContent(0, r.Name) },
        new() { Header = "Status", HideOnTablet = true, Cell = r => b => b.AddContent(0, r.Status) },
    ];

    private static readonly IReadOnlyList<ListView> Views =
    [
        new("all", "All open"),
        new("mine", "My open", new FilterGroup { Conditions = [new FilterCondition { Field = "owner_id", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement("@me") }] }),
    ];

    private static readonly IReadOnlyList<FieldDto> Fields =
    [
        new("name", "Name", null, FieldType.Text, false, false, false, true, null, null),
        new("status", "Status", null, FieldType.Picklist, false, false, false, true, "s", [new PicklistOptionDto("new", "New", null, null)]),
    ];

    private sealed class Fake
    {
        public List<ListQuery> Calls { get; } = [];
        public int Total { get; set; } = 2;
        public Func<ListQuery, Task>? Before { get; set; }

        public async Task<PagedResult<Row>> LoadAsync(ListQuery query)
        {
            Calls.Add(query);
            if (Before is not null)
            {
                await Before(query);
            }

            var rows = Enumerable.Range(1, Math.Min(Total, 25)).Select(i => new Row(Guid.NewGuid(), $"Row {query.Page}-{i}", "new")).ToList();
            return new PagedResult<Row>(rows, Total, query.Page, query.PageSize);
        }
    }

    private IRenderedComponent<EntityListPage<Row>> Create(Fake fake, Action<ComponentParameterCollectionBuilder<EntityListPage<Row>>>? configure = null, IReadOnlyList<FieldDto>? fields = null)
    {
        Render<MudPopoverProvider>();
        return Render<EntityListPage<Row>>(p =>
        {
            p.Add(c => c.Title, "Leads")
             .Add(c => c.ItemLabel, "leads")
             .Add(c => c.Columns, Columns)
             .Add(c => c.Views, Views)
             .Add(c => c.Fields, fields ?? Fields)
             .Add(c => c.Loader, fake.LoadAsync)
             .Add(c => c.EmptyTitle, "No leads yet")
             .Add(c => c.EmptyMessage, "Leads appear here.");
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void It_loads_the_first_page_on_start_and_shows_the_rows_and_count()
    {
        var fake = new Fake { Total = 2 };

        var list = Create(fake);

        fake.Calls.Count.ShouldBe(1);
        fake.Calls[0].Page.ShouldBe(1);
        fake.Calls[0].Search.ShouldBeNull();
        fake.Calls[0].Filter.ShouldBeNull();
        list.FindAll("[data-testid=row]").Count.ShouldBe(2);
        list.Markup.ShouldContain("2 leads");
    }

    [Fact]
    public void The_count_uses_thousands_separators()
    {
        var list = Create(new Fake { Total = 1284 });

        list.Markup.ShouldContain("1,284 leads");
    }

    [Fact]
    public void Choosing_a_view_loads_it_with_the_views_filter_and_puts_it_in_the_address()
    {
        var fake = new Fake();
        var list = Create(fake);

        list.Find("[data-testid=view-mine]").Click();

        fake.Calls.Last().Filter!.Conditions.Single().Field.ShouldBe("owner_id");
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("view=mine");
    }

    [Fact]
    public void The_default_view_leaves_the_address_clean()
    {
        var fake = new Fake();
        var list = Create(fake);
        list.Find("[data-testid=view-mine]").Click();

        list.Find("[data-testid=view-all]").Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldNotContain("view=");
        fake.Calls.Last().Filter.ShouldBeNull();
    }

    [Fact]
    public void Typing_in_the_search_box_searches_and_goes_back_to_page_one()
    {
        var fake = new Fake { Total = 60 };
        var list = Create(fake);
        list.Find("[data-testid=page-next]").Click();
        fake.Calls.Last().Page.ShouldBe(2);

        list.Find("input[data-testid=search]").Input("sam");

        list.WaitForAssertion(() =>
        {
            fake.Calls.Last().Search.ShouldBe("sam");
            fake.Calls.Last().Page.ShouldBe(1);
        }, TimeSpan.FromSeconds(3));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("q=sam");
    }

    [Fact]
    public void The_page_buttons_move_between_pages_and_stop_at_the_ends()
    {
        var fake = new Fake { Total = 60 };
        var list = Create(fake);

        list.Find("button[data-testid=page-prev]").HasAttribute("disabled").ShouldBeTrue();
        list.Markup.ShouldContain("page 1 of 3");

        list.Find("[data-testid=page-next]").Click();
        list.Find("[data-testid=page-next]").Click();

        fake.Calls.Last().Page.ShouldBe(3);
        list.Find("button[data-testid=page-next]").HasAttribute("disabled").ShouldBeTrue();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("page=3");
    }

    [Fact]
    public void Clicking_a_sortable_header_cycles_ascending_descending_and_back_to_default()
    {
        var fake = new Fake();
        var list = Create(fake, p => p.Add(c => c.DefaultSortField, "created_at").Add(c => c.DefaultSortDir, "desc"));
        fake.Calls.Last().SortField.ShouldBe("created_at");

        list.Find("[data-testid=sort-name]").Click();
        fake.Calls.Last().SortField.ShouldBe("name");
        fake.Calls.Last().SortDir.ShouldBe("asc");

        list.Find("[data-testid=sort-name]").Click();
        fake.Calls.Last().SortDir.ShouldBe("desc");

        list.Find("[data-testid=sort-name]").Click();
        fake.Calls.Last().SortField.ShouldBe("created_at");
        fake.Calls.Last().SortDir.ShouldBe("desc");
    }

    [Fact]
    public void The_default_sort_is_not_written_to_the_address_but_a_chosen_one_is()
    {
        var fake = new Fake { Total = 60 };
        var list = Create(fake, p => p.Add(c => c.DefaultSortField, "created_at").Add(c => c.DefaultSortDir, "desc"));
        var nav = Services.GetRequiredService<NavigationManager>();

        list.Find("[data-testid=page-next]").Click();
        nav.Uri.ShouldContain("page=2");
        nav.Uri.ShouldNotContain("sort=");

        list.Find("[data-testid=sort-name]").Click();
        nav.Uri.ShouldContain("sort=name");
        nav.Uri.ShouldContain("dir=asc");
    }

    [Fact]
    public void A_column_without_a_sort_field_has_no_sort_button()
    {
        var list = Create(new Fake());

        list.FindAll("th button").Count.ShouldBe(1);
    }

    [Fact]
    public void With_no_records_and_no_search_it_shows_the_first_time_message()
    {
        var list = Create(new Fake { Total = 0 });

        list.Markup.ShouldContain("No leads yet");
        list.Markup.ShouldContain("Leads appear here.");
        list.Markup.ShouldNotContain("No results");
    }

    [Fact]
    public void With_no_matches_for_a_search_it_shows_no_results_and_a_way_to_clear()
    {
        var fake = new Fake { Total = 0 };
        var list = Create(fake);
        list.Find("input[data-testid=search]").Input("zzz");
        list.WaitForAssertion(() => list.Markup.ShouldContain("No results"), TimeSpan.FromSeconds(3));

        fake.Total = 2;
        list.Find("[data-testid=empty-clear]").Click();

        fake.Calls.Last().Search.ShouldBeNull();
        list.FindAll("[data-testid=row]").Count.ShouldBe(2);
    }

    [Fact]
    public void An_empty_non_default_view_says_no_results_rather_than_no_records_yet()
    {
        var list = Create(new Fake { Total = 0 });

        list.Find("[data-testid=view-mine]").Click();

        list.Markup.ShouldContain("No results");
        list.Markup.ShouldNotContain("No leads yet");
    }

    [Fact]
    public void A_failed_load_shows_the_error_state_and_retrying_works()
    {
        var attempts = 0;
        var fake = new Fake { Before = _ => ++attempts == 1 ? Task.FromException(new HttpRequestException("down")) : Task.CompletedTask };

        var list = Create(fake);

        list.Markup.ShouldContain("We could not reach MindLift");
        list.FindAll("[data-testid=row]").ShouldBeEmpty();

        list.Find("[data-testid=error-retry]").Click();

        list.FindAll("[data-testid=row]").Count.ShouldBe(2);
    }

    [Fact]
    public void Clicking_a_row_goes_to_its_address()
    {
        var fake = new Fake();
        var list = Create(fake, p => p.Add(c => c.RowHref, r => "/leads/" + r.Id));

        list.FindAll("[data-testid=row]")[0].Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/leads/");
    }

    [Fact]
    public void Clicking_a_row_can_run_a_callback_instead()
    {
        var clicked = new List<Row>();
        var list = Create(new Fake(), p => p.Add(c => c.OnRowClick, EventCallback.Factory.Create<Row>(this, r => clicked.Add(r))));

        list.FindAll("[data-testid=row]")[1].Click();

        clicked.ShouldHaveSingleItem().Name.ShouldBe("Row 1-2");
    }

    [Fact]
    public void The_state_in_the_address_is_used_for_the_first_load()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/leads?view=mine&q=sam&page=2&sort=name&dir=desc");
        var fake = new Fake { Total = 60 };

        Create(fake);

        var first = fake.Calls[0];
        first.Search.ShouldBe("sam");
        first.Page.ShouldBe(2);
        first.SortField.ShouldBe("name");
        first.SortDir.ShouldBe("desc");
        first.Filter!.Conditions.Single().Field.ShouldBe("owner_id");
    }

    [Fact]
    public void A_view_filter_and_the_users_filters_are_combined_with_and()
    {
        var user = new FilterGroup { Conditions = [new FilterCondition { Field = "status", Op = FilterOperator.In, Value = JsonSerializer.SerializeToElement(new[] { "new" }) }] };
        Services.GetRequiredService<NavigationManager>().NavigateTo("/leads?view=mine&f=" + ListState.EncodeFilter(user));
        var fake = new Fake();

        Create(fake);

        var sent = fake.Calls[0].Filter!;
        sent.Logic.ShouldBe("and");
        sent.Groups.Count.ShouldBe(2);
        sent.Groups[0].Conditions.Single().Field.ShouldBe("owner_id");
        sent.Groups[1].Conditions.Single().Field.ShouldBe("status");
    }

    [Fact]
    public void Active_filters_show_as_chips_with_readable_text_and_can_be_removed()
    {
        var user = new FilterGroup { Conditions = [new FilterCondition { Field = "status", Op = FilterOperator.In, Value = JsonSerializer.SerializeToElement(new[] { "new" }) }] };
        Services.GetRequiredService<NavigationManager>().NavigateTo("/leads?f=" + ListState.EncodeFilter(user));
        var fake = new Fake();
        var list = Create(fake);

        list.Find("[data-testid=filter-chip]").TextContent.ShouldContain("Status is any of New");

        list.Find("[data-testid=filter-chip] .mud-chip-close-button").Click();

        list.FindAll("[data-testid=filter-chip]").ShouldBeEmpty();
        fake.Calls.Last().Filter.ShouldBeNull();
    }

    [Fact]
    public void The_clear_button_only_shows_when_the_list_is_narrowed_and_resets_search_and_filters()
    {
        var fake = new Fake();
        var list = Create(fake);
        list.FindAll("[data-testid=clear-filters]").ShouldBeEmpty();

        list.Find("input[data-testid=search]").Input("sam");
        list.WaitForAssertion(() => list.FindAll("[data-testid=clear-filters]").Count.ShouldBe(1), TimeSpan.FromSeconds(3));

        // Wait for the search to finish loading first. A click in the instant the list is re-rendering targets a handler that is already replaced.
        list.WaitForAssertion(() => list.FindAll("[data-testid=row]").Count.ShouldBe(2), TimeSpan.FromSeconds(3));
        list.Find("[data-testid=clear-filters]").Click();

        fake.Calls.Last().Search.ShouldBeNull("calls: " + string.Join(" | ", fake.Calls.Select(c => c.Search ?? "null")));
        list.FindAll("[data-testid=clear-filters]").ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_that_arrives_late_does_not_replace_a_newer_one()
    {
        var slow = new TaskCompletionSource();
        var fake = new Fake { Total = 60 };
        var list = Create(fake);

        // Page 2 is slow. While it is still loading, a search is typed and answers at once.
        fake.Before = q => q.Page == 2 ? slow.Task : Task.CompletedTask;
        _ = list.InvokeAsync(() => list.Find("[data-testid=page-next]").Click());
        list.WaitForAssertion(() => fake.Calls.Last().Page.ShouldBe(2));
        list.Find("input[data-testid=search]").Input("sam");
        list.WaitForAssertion(() => fake.Calls.Last().Search.ShouldBe("sam"), TimeSpan.FromSeconds(3));
        list.WaitForAssertion(() => list.Markup.ShouldContain("Row 1-1"));

        // The slow answer for page 2 now arrives. It must not replace the search result.
        slow.SetResult();
        list.WaitForAssertion(() => list.Markup.ShouldContain("Row 1-1"));
        list.Markup.ShouldNotContain("Row 2-1");
    }

    [Fact]
    public void The_filters_button_only_shows_when_there_are_fields_to_filter_on()
    {
        Create(new Fake(), fields: []).FindAll("[data-testid=open-filters]").ShouldBeEmpty();
    }

    [Fact]
    public void The_filters_button_shows_how_many_filters_are_active()
    {
        var user = new FilterGroup { Conditions = [new FilterCondition { Field = "status", Op = FilterOperator.IsEmpty }] };
        Services.GetRequiredService<NavigationManager>().NavigateTo("/leads?f=" + ListState.EncodeFilter(user));

        var list = Create(new Fake());

        list.Find(".mud-badge").TextContent.Trim().ShouldBe("1");
    }

    [Fact]
    public void The_phone_card_list_uses_the_card_template_when_given()
    {
        var list = Create(new Fake(), p => p.Add(c => c.CardTemplate, r => b => b.AddMarkupContent(0, $"<b class=\"card-name\">{r.Name}</b>")));

        list.FindAll("[data-testid=card] .card-name").Count.ShouldBe(2);
    }
}
