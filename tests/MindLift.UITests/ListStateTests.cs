using System.Text.Json;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using Shouldly;

namespace MindLift.UITests;

public sealed class ListStateTests
{
    private static readonly string[] Views = ["all", "mine", "new"];

    private static FilterGroup SomeFilter() => new()
    {
        Conditions = [new FilterCondition { Field = "status", Op = FilterOperator.In, Value = JsonSerializer.SerializeToElement(new[] { "new", "working" }) }],
    };

    [Fact]
    public void An_empty_address_gives_the_default_state()
    {
        var state = ListState.FromQuery(null, "all");

        state.View.ShouldBe("all");
        state.Search.ShouldBeNull();
        state.Page.ShouldBe(1);
        state.Filter.ShouldBeNull();
        state.SortField.ShouldBeNull();
        state.IsNarrowed.ShouldBeFalse();
    }

    [Fact]
    public void The_view_search_and_page_are_read_from_the_address()
    {
        var state = ListState.FromQuery("?view=mine&q=sam%20khan&page=3", "all", Views);

        state.View.ShouldBe("mine");
        state.Search.ShouldBe("sam khan");
        state.Page.ShouldBe(3);
        state.IsNarrowed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-4")]
    [InlineData("?page=abc")]
    [InlineData("?page=")]
    public void A_page_that_is_not_a_positive_number_becomes_page_one(string query)
    {
        ListState.FromQuery(query, "all").Page.ShouldBe(1);
    }

    [Fact]
    public void An_unknown_view_falls_back_to_the_default()
    {
        ListState.FromQuery("?view=deleted-view", "all", Views).View.ShouldBe("all");
    }

    [Fact]
    public void Plus_signs_and_escapes_in_the_search_are_decoded()
    {
        ListState.FromQuery("?q=a+b%26c", "all").Search.ShouldBe("a b&c");
    }

    [Fact]
    public void Blank_search_text_is_treated_as_no_search()
    {
        ListState.FromQuery("?q=%20%20", "all").Search.ShouldBeNull();
    }

    [Fact]
    public void A_plain_list_has_a_plain_address()
    {
        var query = new ListState("all").ToQuery("all");

        query.Values.ShouldAllBe(v => v == null);
    }

    [Fact]
    public void Only_the_values_that_differ_from_the_defaults_are_written()
    {
        var query = new ListState("mine", "sam", 2).ToQuery("all");

        query["view"].ShouldBe("mine");
        query["q"].ShouldBe("sam");
        query["page"].ShouldBe("2");
        query["f"].ShouldBeNull();
        query["sort"].ShouldBeNull();
    }

    [Fact]
    public void A_filter_survives_a_trip_through_the_address()
    {
        var state = new ListState("all", Filter: SomeFilter());
        var encoded = state.ToQuery("all")["f"]!;

        var loaded = ListState.FromQuery("?f=" + encoded, "all");

        var condition = loaded.Filter!.Conditions.ShouldHaveSingleItem();
        condition.Field.ShouldBe("status");
        condition.Op.ShouldBe(FilterOperator.In);
        condition.Value!.Value.GetRawText().ShouldBe("[\"new\",\"working\"]");
        loaded.HasFilter.ShouldBeTrue();
    }

    [Fact]
    public void The_encoded_filter_is_safe_to_put_in_an_address()
    {
        var encoded = ListState.EncodeFilter(SomeFilter());

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
        Uri.EscapeDataString(encoded).ShouldBe(encoded);
    }

    [Theory]
    [InlineData("not base64 at all!!")]
    [InlineData("YWJj")]
    [InlineData("e30")]
    public void A_damaged_filter_is_ignored_instead_of_breaking_the_page(string bad)
    {
        ListState.FromQuery("?f=" + bad, "all").Filter.ShouldBeNull();
    }

    [Fact]
    public void A_sort_needs_a_field_and_defaults_to_ascending()
    {
        var sorted = ListState.FromQuery("?sort=created_at", "all");
        sorted.SortField.ShouldBe("created_at");
        sorted.SortDir.ShouldBe("asc");

        ListState.FromQuery("?sort=created_at&dir=desc", "all").SortDir.ShouldBe("desc");
        ListState.FromQuery("?dir=desc", "all").SortField.ShouldBeNull();
        ListState.FromQuery("?sort=created_at&dir=sideways", "all").SortDir.ShouldBe("asc");
    }

    [Fact]
    public void Changing_the_view_search_or_filter_goes_back_to_page_one()
    {
        var onPageFive = new ListState("all", "sam", 5, SomeFilter());

        onPageFive.Reset(view: "mine").Page.ShouldBe(1);
        onPageFive.Reset(search: "omar").Page.ShouldBe(1);
        onPageFive.Reset(filter: new FilterGroup()).Page.ShouldBe(1);
    }

    [Fact]
    public void Clearing_removes_the_search_and_the_filter_but_keeps_the_view()
    {
        var state = new ListState("mine", "sam", 4, SomeFilter(), "created_at", "desc");

        var cleared = state.Reset(clearSearch: true, clearFilter: true);

        cleared.View.ShouldBe("mine");
        cleared.Search.ShouldBeNull();
        cleared.Filter.ShouldBeNull();
        cleared.SortField.ShouldBe("created_at");
        cleared.IsNarrowed.ShouldBeFalse();
    }
}
