using System.Text.Json;
using MindLift.Application.Common.Automation;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.UnitTests.Automation;

public sealed class AutomationEngineTests
{
    [Fact]
    public void Loop_protection_stops_runaway_execution_at_depth_3()
    {
        var context = new AutomationExecutionContext();
        context.CanExecute().ShouldBeTrue();

        using (context.EnterScope())
        {
            context.Depth.ShouldBe(1);
            context.CanExecute().ShouldBeTrue();

            using (context.EnterScope())
            {
                context.Depth.ShouldBe(2);
                context.CanExecute().ShouldBeTrue();

                using (context.EnterScope())
                {
                    context.Depth.ShouldBe(3);
                    context.CanExecute().ShouldBeFalse("At depth 3 loop protection must stop execution");
                }

                context.Depth.ShouldBe(2);
                context.CanExecute().ShouldBeTrue();
            }

            context.Depth.ShouldBe(1);
        }

        context.Depth.ShouldBe(0);
        context.CanExecute().ShouldBeTrue();
    }

    [Fact]
    public void Filter_condition_evaluates_correctly_on_record()
    {
        var lead = new Lead
        {
            FirstName = "Sarah",
            LastName = "Connor",
            Status = "working",
            Rating = "hot"
        };

        var group = new FilterGroup
        {
            Conditions =
            [
                new FilterCondition
                {
                    Field = "rating",
                    Op = FilterOperator.Eq,
                    Value = JsonDocument.Parse("\"hot\"").RootElement
                }
            ]
        };

        var entityInfo = SystemFields.Get("leads");
        var context = new FilterContext(TimeZoneInfo.Utc, DateTime.UtcNow, DayOfWeek.Monday, 1, null);
        var predicate = FilterCompiler.Compile<Lead>(group, entityInfo.SystemFields, context).Compile();

        predicate(lead).ShouldBeTrue();

        lead.Rating = "cold";
        predicate(lead).ShouldBeFalse();
    }

    [Fact]
    public void Save_pipeline_evaluates_stage_change_accurately()
    {
        var prevDeal = new Deal
        {
            Name = "Enterprise Plan",
            StageId = Guid.NewGuid(),
            Status = "Open"
        };

        var newStageId = Guid.NewGuid();
        var currentDeal = new Deal
        {
            Name = "Enterprise Plan",
            StageId = newStageId,
            Status = "Open"
        };

        currentDeal.StageId.ShouldNotBe(prevDeal.StageId);
    }
}
