using MindLift.Infrastructure.Tenancy;
using Shouldly;

namespace MindLift.UnitTests.Tenancy;

/// <summary>When a workspace counts as lapsed, which every part of the app now uses.</summary>
public sealed class TenantAccessTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("PastDue")]
    [InlineData("Cancelled")]
    [InlineData("Suspended")]
    public void These_statuses_are_lapsed_whatever_the_trial_date(string status)
    {
        TenantAccess.IsLapsed(status, null, Now).ShouldBeTrue();
        TenantAccess.IsLapsed(status, Now.AddDays(10), Now).ShouldBeTrue();
    }

    [Fact]
    public void An_active_workspace_is_not_lapsed()
    {
        TenantAccess.IsLapsed("Active", null, Now).ShouldBeFalse();
        TenantAccess.IsLapsed("Active", Now.AddDays(-30), Now).ShouldBeFalse(); // An old trial date means nothing once it is Active.
    }

    [Fact]
    public void A_trial_is_lapsed_exactly_when_its_end_has_passed()
    {
        TenantAccess.IsLapsed("Trial", Now.AddMinutes(1), Now).ShouldBeFalse();
        TenantAccess.IsLapsed("Trial", Now, Now).ShouldBeTrue();
        TenantAccess.IsLapsed("Trial", Now.AddMinutes(-1), Now).ShouldBeTrue();
    }

    [Fact]
    public void A_trial_with_no_end_date_is_not_lapsed()
    {
        TenantAccess.IsLapsed("Trial", null, Now).ShouldBeFalse();
    }

    [Fact]
    public void An_unknown_or_missing_status_is_not_treated_as_lapsed_by_this_rule()
    {
        TenantAccess.IsLapsed(null, null, Now).ShouldBeFalse();
        TenantAccess.IsLapsed("Something else", null, Now).ShouldBeFalse();
    }

    [Fact]
    public void Only_suspended_counts_as_suspended()
    {
        TenantAccess.IsSuspended("Suspended").ShouldBeTrue();
        TenantAccess.IsSuspended("PastDue").ShouldBeFalse();
        TenantAccess.IsSuspended("Cancelled").ShouldBeFalse();
        TenantAccess.IsSuspended(null).ShouldBeFalse();
    }
}
