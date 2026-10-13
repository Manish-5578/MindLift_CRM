using System.Text.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.UI.Components;
using Shouldly;

namespace MindLift.UITests;

public sealed class FilterDescriberTests
{
    private static FieldDto Field(string key, string label, FieldType type, string? picklist = null)
        => new(key, label, null, type, false, false, false, true, picklist, null);

    private static FilterCondition Cond(string field, FilterOperator op, object? value = null)
        => new() { Field = field, Op = op, Value = value is null ? null : JsonSerializer.SerializeToElement(value) };

    private static readonly IReadOnlyList<PicklistDto> Picklists =
    [
        new("lead_status",
        [
            new(Guid.NewGuid(), "new", "New", null, null, null, 1, true, true, true),
            new(Guid.NewGuid(), "working", "Working", null, null, null, 2, false, true, true),
        ]),
    ];

    [Fact]
    public void A_text_condition_reads_like_a_sentence()
    {
        FilterDescriber.Describe(Cond("first_name", FilterOperator.Contains, "sam"), Field("first_name", "First name", FieldType.Text))
            .ShouldBe("First name contains sam");
    }

    [Fact]
    public void Is_empty_has_no_value()
    {
        FilterDescriber.Describe(Cond("email", FilterOperator.IsEmpty), Field("email", "Email", FieldType.Email))
            .ShouldBe("Email is empty");
    }

    [Fact]
    public void Picklist_codes_are_shown_as_their_labels()
    {
        FilterDescriber.Describe(Cond("status", FilterOperator.In, new[] { "new", "working" }), Field("status", "Status", FieldType.Picklist, "lead_status"), Picklists)
            .ShouldBe("Status is any of New, Working");
    }

    [Fact]
    public void An_unknown_picklist_code_is_shown_as_it_is()
    {
        FilterDescriber.Describe(Cond("status", FilterOperator.In, new[] { "gone" }), Field("status", "Status", FieldType.Picklist, "lead_status"), Picklists)
            .ShouldBe("Status is any of gone");
    }

    [Fact]
    public void At_me_is_shown_as_me()
    {
        FilterDescriber.Describe(Cond("owner_id", FilterOperator.Eq, "@me"), Field("owner_id", "Owner", FieldType.User))
            .ShouldBe("Owner is Me");
    }

    [Fact]
    public void A_user_id_is_shown_as_the_persons_name()
    {
        var sara = new UserListItem(Guid.NewGuid(), "sara@x.test", "Sara Khan", true, false, null, null, null, null);

        FilterDescriber.Describe(Cond("owner_id", FilterOperator.Eq, sara.Id.ToString()), Field("owner_id", "Owner", FieldType.User), users: [sara])
            .ShouldBe("Owner is Sara Khan");
    }

    [Fact]
    public void Numbers_drop_trailing_zeros()
    {
        FilterDescriber.Describe(Cond("budget", FilterOperator.Gte, 1500000m), Field("budget", "Budget", FieldType.Currency))
            .ShouldBe("Budget is at least 1500000");
    }

    [Fact]
    public void A_range_shows_both_ends()
    {
        FilterDescriber.Describe(Cond("budget", FilterOperator.Between, new { from = 10, to = 20 }), Field("budget", "Budget", FieldType.Currency))
            .ShouldBe("Budget is between 10 and 20");
    }

    [Fact]
    public void Dates_are_shown_as_day_month_year()
    {
        FilterDescriber.Describe(Cond("created_at", FilterOperator.Gte, "2026-10-09"), Field("created_at", "Created", FieldType.DateTime))
            .ShouldBe("Created is at least 09 Oct 2026");
    }

    [Fact]
    public void A_date_preset_is_shown_by_its_label()
    {
        FilterDescriber.Describe(Cond("created_at", FilterOperator.DatePreset, "LastQuarter"), Field("created_at", "Created", FieldType.DateTime))
            .ShouldBe("Created is in Last quarter");
    }

    [Fact]
    public void Yes_and_no_are_shown_for_booleans()
    {
        FilterDescriber.Describe(Cond("is_vip", FilterOperator.Eq, true), Field("is_vip", "VIP", FieldType.Boolean)).ShouldBe("VIP is Yes");
        FilterDescriber.Describe(Cond("is_vip", FilterOperator.Eq, false), Field("is_vip", "VIP", FieldType.Boolean)).ShouldBe("VIP is No");
    }
}
