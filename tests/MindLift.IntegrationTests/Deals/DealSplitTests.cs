using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Deals;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Tests revenue and overlay split configuration, 100% revenue validation,
/// dynamic recalculations, user performance reports, and multi-tenant isolation.
/// </summary>
public sealed class DealSplitTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/deal-splits", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static async Task<DealDto> SetupDealAsync(Session s, string dealName = "Mega Deal", decimal amount = 100000m)
    {
        (await s.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();

        var r = await s.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest(
            dealName,
            null,
            null,
            amount,
            null,
            20,
            DateTime.UtcNow.AddMonths(1),
            null,
            null,
            null,
            null,
            null
        ));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<DealDto>())!;
    }

    [Fact]
    public async Task Feature_flag_off_blocks_deal_splits_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var deal = await SetupDealAsync(owner);

        // GET splits blocked (404)
        var getRes = await owner.Client.GetAsync($"/api/v1/deals/{deal.Id}/splits");
        getRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // PUT splits blocked (404)
        var putRes = await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}/splits", new SetDealSplitsRequest([]));
        putRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // User splits report blocked (404)
        var userRes = await owner.Client.GetAsync($"/api/v1/deals/splits/users/{Guid.NewGuid()}");
        userRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Revenue_splits_crud_and_100_percent_validation()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(owner);

        var member = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff");

        var ownerMe = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var memberMe = await member.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        var deal = await SetupDealAsync(owner, "Cloud ERP Migration", 100000m);

        // 1. Initial splits list is empty (and IsValid = true)
        var initial = await owner.Client.GetFromJsonAsync<DealSplitSummaryDto>($"/api/v1/deals/{deal.Id}/splits");
        initial.ShouldNotBeNull();
        initial.Splits.ShouldBeEmpty();
        initial.IsValid.ShouldBeTrue();

        // 2. Setting invalid revenue splits (60% + 30% = 90% != 100%) -> fails (400)
        var invalidReq = new SetDealSplitsRequest([
            new(ownerMe!.UserId, "Revenue", 60m),
            new(memberMe!.UserId, "Revenue", 30m)
        ]);
        var invalidRes = await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}/splits", invalidReq);
        invalidRes.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // 3. Setting valid revenue splits (70% owner, 30% member = 100%) -> succeeds
        var validReq = new SetDealSplitsRequest([
            new(ownerMe.UserId, "Revenue", 70m, "Lead Closer"),
            new(memberMe.UserId, "Revenue", 30m, "Technical Discovery")
        ]);
        var validRes = await owner.Client.PutAsJsonAsync($"/api/v1/deals/{deal.Id}/splits", validReq);
        validRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await validRes.Content.ReadFromJsonAsync<DealSplitSummaryDto>();
        summary.ShouldNotBeNull();
        summary.IsValid.ShouldBeTrue();
        summary.TotalRevenuePercentage.ShouldBe(100.00m);
        summary.TotalRevenueAmount.ShouldBe(100000.00m);
        summary.Splits.Count.ShouldBe(2);

        var ownerSplit = summary.Splits.Single(s => s.UserId == ownerMe.UserId);
        ownerSplit.Percentage.ShouldBe(70m);
        ownerSplit.Amount.ShouldBe(70000.00m);
        ownerSplit.Note.ShouldBe("Lead Closer");

        var memberSplit = summary.Splits.Single(s => s.UserId == memberMe.UserId);
        memberSplit.Percentage.ShouldBe(30m);
        memberSplit.Amount.ShouldBe(30000.00m);
        memberSplit.Note.ShouldBe("Technical Discovery");

        // 4. Add Overlay split (e.g. 20% sales engineering credit) without violating revenue 100% rule
        var overlayRes = await owner.Client.PostAsJsonAsync($"/api/v1/deals/{deal.Id}/splits", new SaveDealSplitMemberRequest(
            memberMe.UserId,
            "Overlay",
            20m,
            "Solution Architect Credit"
        ));
        overlayRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var overlayDto = await overlayRes.Content.ReadFromJsonAsync<DealSplitMemberDto>();
        overlayDto.ShouldNotBeNull();
        overlayDto.SplitType.ShouldBe("Overlay");
        overlayDto.Percentage.ShouldBe(20m);
        overlayDto.Amount.ShouldBe(20000.00m);

        // 5. Verify deal summary includes all 3 splits and revenue is still valid
        var recheck = await owner.Client.GetFromJsonAsync<DealSplitSummaryDto>($"/api/v1/deals/{deal.Id}/splits");
        recheck.ShouldNotBeNull();
        recheck.IsValid.ShouldBeTrue();
        recheck.Splits.Count.ShouldBe(3);

        // 6. Check user performance split summary for member
        var memberReport = await owner.Client.GetFromJsonAsync<UserDealSplitsSummaryDto>($"/api/v1/deals/splits/users/{memberMe.UserId}");
        memberReport.ShouldNotBeNull();
        memberReport.UserId.ShouldBe(memberMe.UserId);
        memberReport.TotalSplitAmount.ShouldBe(30000.00m); // Revenue splits total
        memberReport.DealCount.ShouldBe(2); // 1 Revenue split + 1 Overlay split

        // 7. Remove overlay split
        var removeRes = await owner.Client.DeleteAsync($"/api/v1/deals/{deal.Id}/splits/{overlayDto.Id}");
        removeRes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemove = await removeRes.Content.ReadFromJsonAsync<DealSplitSummaryDto>();
        afterRemove.ShouldNotBeNull();
        afterRemove.Splits.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Multi_tenant_isolation_for_deal_splits()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        await EnableFeatureAsync(tenantA);
        await EnableFeatureAsync(tenantB);

        var dealA = await SetupDealAsync(tenantA, "Tenant A Private Deal", 50000m);
        var meA = await tenantA.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        // Tenant A configures splits
        var setResA = await tenantA.Client.PutAsJsonAsync($"/api/v1/deals/{dealA.Id}/splits", new SetDealSplitsRequest([
            new(meA!.UserId, "Revenue", 100m)
        ]));
        setResA.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tenant B attempts to read Tenant A's deal splits -> 404
        var getB = await tenantB.Client.GetAsync($"/api/v1/deals/{dealA.Id}/splits");
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to modify Tenant A's deal splits -> 404
        var putB = await tenantB.Client.PutAsJsonAsync($"/api/v1/deals/{dealA.Id}/splits", new SetDealSplitsRequest([]));
        putB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to access Tenant A's user report -> 404
        var userReportB = await tenantB.Client.GetAsync($"/api/v1/deals/splits/users/{meA.UserId}");
        userReportB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
