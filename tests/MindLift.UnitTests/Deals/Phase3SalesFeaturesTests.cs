using System;
using System.Collections.Generic;
using System.Linq;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.UnitTests.Deals;

public sealed class Phase3SalesFeaturesTests
{
    [Fact]
    public void DealSplits_Calculates_Percentage_And_Validates_100_Percent()
    {
        var dealId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var amount = 10000m;

        var member1 = new DealSplitMemberDto(
            Guid.NewGuid(), dealId, user1, "Alice", "alice@example.com", "Revenue", 60m, 6000m, "Lead Rep");
        var member2 = new DealSplitMemberDto(
            Guid.NewGuid(), dealId, user2, "Bob", "bob@example.com", "Revenue", 40m, 4000m, "SE Support");

        var summary = new DealSplitSummaryDto(
            dealId, amount, "USD", 100m, 10000m, true, new List<DealSplitMemberDto> { member1, member2 });

        summary.IsValid.ShouldBeTrue();
        summary.TotalRevenuePercentage.ShouldBe(100m);
        summary.TotalRevenueAmount.ShouldBe(10000m);
        summary.Splits.Count.ShouldBe(2);
        summary.Splits.First().Amount.ShouldBe(6000m);
    }

    [Fact]
    public void ForecastCategories_Contains_Standard_Categories()
    {
        ForecastCategories.All.ShouldContain("Pipeline");
        ForecastCategories.All.ShouldContain("BestCase");
        ForecastCategories.All.ShouldContain("Commit");
        ForecastCategories.All.ShouldContain("Closed");
        ForecastCategories.All.ShouldContain("Omitted");

        ForecastCategories.All.Contains("InvalidCategory").ShouldBeFalse();
    }

    [Fact]
    public void CompetitorTracking_Constructs_Valid_Deal_Association()
    {
        var dealId = Guid.NewGuid();
        var compId = Guid.NewGuid();

        var req = new AddDealCompetitorRequest(compId, "High", "Brand recognition", "Higher pricing", "Focus on ROI");
        req.CompetitorId.ShouldBe(compId);
        req.ThreatLevel.ShouldBe("High");
        req.StrengthsObserved.ShouldBe("Brand recognition");
        req.WeaknessesObserved.ShouldBe("Higher pricing");
        req.Notes.ShouldBe("Focus on ROI");
    }
}

