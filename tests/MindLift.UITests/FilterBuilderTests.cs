using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using MudBlazor;
using FieldType = MindLift.Shared.Filtering.FieldType;
using FilterOperator = MindLift.Shared.Filtering.FilterOperator;
using MudBlazor.Services;
using Shouldly;

namespace MindLift.UITests;

public sealed class FilterBuilderTests : BunitContext, IAsyncLifetime
{
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    public FilterBuilderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    private static FieldDto Field(string key, string label, FieldType type, bool filterable = true, bool hidden = false)
        => new(key, label, null, type, false, false, hidden, filterable, null, null);

    private static readonly IReadOnlyList<FieldDto> Fields =
    [
        Field("first_name", "First name", FieldType.Text),
        Field("budget", "Budget", FieldType.Currency),
        Field("secret", "Secret", FieldType.Text, filterable: false),
        Field("hidden_field", "Hidden", FieldType.Text, hidden: true),
    ];

    private (IRenderedComponent<FilterBuilder> Builder, List<FilterGroup> Emitted) Create(FilterGroup? value = null)
    {
        Render<MudPopoverProvider>();
        var emitted = new List<FilterGroup>();
        var builder = Render<FilterBuilder>(p => p
            .Add(c => c.Fields, Fields)
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<FilterGroup>(this, g => emitted.Add(g))));
        return (builder, emitted);
    }

    [Fact]
    public void With_no_filter_it_shows_only_the_add_button()
    {
        var (builder, _) = Create();

        builder.FindAll("[data-testid=filter-row]").ShouldBeEmpty();
        builder.Find("[data-testid=add-filter]").ShouldNotBeNull();
    }

    [Fact]
    public void Adding_a_filter_adds_a_row_for_the_first_filterable_field()
    {
        var (builder, emitted) = Create();

        builder.Find("[data-testid=add-filter]").Click();

        builder.FindAll("[data-testid=filter-row]").Count.ShouldBe(1);
        // The new row has no value yet, so it is not part of the filter.
        emitted.Last().Conditions.ShouldBeEmpty();
    }

    [Fact]
    public void An_existing_filter_is_shown_as_one_row_per_condition()
    {
        var value = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition { Field = "first_name", Op = FilterOperator.Contains, Value = JsonSerializer.SerializeToElement("sam") },
                new FilterCondition { Field = "budget", Op = FilterOperator.Gte, Value = JsonSerializer.SerializeToElement(1000) },
            ],
        };

        var (builder, _) = Create(value);

        builder.FindAll("[data-testid=filter-row]").Count.ShouldBe(2);
    }

    [Fact]
    public void The_match_all_or_any_choice_only_appears_with_two_or_more_rows()
    {
        var (builder, _) = Create();
        builder.Find("[data-testid=add-filter]").Click();
        builder.FindAll("input[data-testid=filter-logic]").ShouldBeEmpty();

        builder.Find("[data-testid=add-filter]").Click();

        builder.FindAll("input[data-testid=filter-logic]").Count.ShouldBe(1);
    }

    [Fact]
    public void Removing_a_row_removes_it_from_the_filter()
    {
        var value = new FilterGroup
        {
            Conditions = [new FilterCondition { Field = "first_name", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement("sam") }],
        };
        var (builder, emitted) = Create(value);

        builder.Find("[data-testid=filter-remove]").Click();

        builder.FindAll("[data-testid=filter-row]").ShouldBeEmpty();
        emitted.Last().Conditions.ShouldBeEmpty();
    }

    [Fact]
    public void Typing_a_value_reports_the_finished_condition()
    {
        var (builder, emitted) = Create();
        builder.Find("[data-testid=add-filter]").Click();

        builder.Find("input[data-testid=filter-value]").Input("sam");

        builder.WaitForAssertion(() =>
        {
            var condition = emitted.Last().Conditions.ShouldHaveSingleItem();
            condition.Field.ShouldBe("first_name");
            condition.Op.ShouldBe(FilterOperator.Contains);
            condition.Value!.Value.GetString().ShouldBe("sam");
        });
    }

    [Fact]
    public void Nested_groups_of_a_loaded_filter_are_kept()
    {
        var nested = new FilterGroup { Logic = "or", Conditions = [new FilterCondition { Field = "first_name", Op = FilterOperator.IsEmpty }] };
        var value = new FilterGroup
        {
            Conditions = [new FilterCondition { Field = "first_name", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement("sam") }],
            Groups = [nested],
        };
        var (builder, emitted) = Create(value);

        builder.Find("[data-testid=add-filter]").Click();

        emitted.Last().Groups.ShouldHaveSingleItem().ShouldBeSameAs(nested);
    }

    [Fact]
    public void A_filter_that_names_a_field_that_is_gone_shows_no_row_for_it()
    {
        var value = new FilterGroup
        {
            Conditions = [new FilterCondition { Field = "deleted_field", Op = FilterOperator.Eq, Value = JsonSerializer.SerializeToElement("x") }],
        };

        var (builder, _) = Create(value);

        builder.FindAll("[data-testid=filter-row]").ShouldBeEmpty();
    }

    [Fact]
    public void Replacing_the_filter_from_outside_reloads_the_rows()
    {
        var (builder, _) = Create();
        builder.Find("[data-testid=add-filter]").Click();
        builder.FindAll("[data-testid=filter-row]").Count.ShouldBe(1);

        builder.Render(p => p.Add(c => c.Value, new FilterGroup()));

        builder.FindAll("[data-testid=filter-row]").ShouldBeEmpty();
    }
}
