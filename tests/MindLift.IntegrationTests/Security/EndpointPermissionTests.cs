using System.Net;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

/// <summary>Admin and configuration endpoints now need a permission; everyday ones stay open to staff.</summary>
public sealed class EndpointPermissionTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Theory]
    [InlineData("/api/v1/backups/schedule")]
    [InlineData("/api/v1/backups/archives")]
    [InlineData("/api/v1/email-sync/accounts")]
    [InlineData("/api/v1/territories")]
    [InlineData("/api/v1/custom-objects/definitions")]
    [InlineData("/api/v1/recycle-bin")]
    public async Task Staff_cannot_open_admin_endpoints_but_the_owner_can(string path)
    {
        var owner = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff", "perm");

        (await staff.Client.GetAsync(path)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        var asOwner = await owner.Client.GetAsync(path);
        asOwner.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden, path);
        asOwner.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized, path);
    }

    [Fact]
    public async Task Staff_keep_the_everyday_endpoints()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff", "daily");

        (await staff.Client.PostAsync("/api/v1/chat/ping", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await staff.Client.GetAsync("/api/v1/mobile/devices")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await staff.Client.GetAsync("/api/v1/fx/rates")).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        (await staff.Client.GetAsync("/api/v1/chat/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
