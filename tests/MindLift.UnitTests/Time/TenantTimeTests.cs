using MindLift.Application.Abstractions;
using MindLift.Application.Common.Time;
using Shouldly;

namespace MindLift.UnitTests.Time;

public sealed class TenantTimeTests
{
    private sealed class FixedClock(DateTime utc) : IClock
    {
        public DateTime UtcNow { get; } = utc;
    }

    [Fact]
    public void Converts_utc_to_dubai_local_time_and_back()
    {
        var clock = new FixedClock(new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc));
        var tenantTime = new TenantTime(TenantTime.FindZone("Asia/Dubai"), clock);

        var local = tenantTime.ToLocal(clock.UtcNow);
        local.ShouldBe(new DateTime(2026, 11, 1, 2, 30, 0));

        tenantTime.ToUtc(local).ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Exposes_the_clock_time_as_now_utc()
    {
        var utc = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc);
        var tenantTime = new TenantTime(TenantTime.FindZone("UTC"), new FixedClock(utc));

        tenantTime.NowUtc.ShouldBe(utc);
    }
}
