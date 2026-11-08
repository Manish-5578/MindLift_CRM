using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Platform;

/// <summary>The "My devices" list and "Sign out everywhere" used by the mobile Profile.</summary>
public sealed class MobileDevicesAndSessionsTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task RegisterAsync(Session s, string installation)
        => (await s.Client.PutAsync("/api/v1/mobile/devices/current", Json(
            $$"""{"installationId":"{{installation}}","platform":"android","environment":"production","deviceToken":"token-{{installation}}","provider":"fcm","appVersion":"1.2.0"}"""))).StatusCode.ShouldBe(HttpStatusCode.OK);

    [Fact]
    public async Task A_person_sees_and_removes_only_their_own_devices()
    {
        var s = await Workspaces.SignupAsync(factory);
        await RegisterAsync(s, "phone-a");
        await RegisterAsync(s, "phone-b");

        var list = await s.Client.GetFromJsonAsync<JsonElement>("/api/v1/mobile/devices");
        list.GetArrayLength().ShouldBe(2);
        var id = list.EnumerateArray().First(d => d.GetProperty("installationId").GetString() == "phone-b").GetProperty("id").GetString();

        // Someone else cannot remove it.
        var other = await Workspaces.SignupAsync(factory);
        (await other.Client.DeleteAsync($"/api/v1/mobile/devices/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await s.Client.DeleteAsync($"/api/v1/mobile/devices/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await s.Client.GetFromJsonAsync<JsonElement>("/api/v1/mobile/devices");
        after.GetArrayLength().ShouldBe(1);
        after[0].GetProperty("installationId").GetString().ShouldBe("phone-a");
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_the_session()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await s.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await s.Client.PostAsync("/api/v1/auth/logout-all", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The old access token no longer works, and neither does the refresh token.
        (await s.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var refresh = await factory.CreateClient().PostAsync("/api/v1/auth/refresh", Json($$"""{"refreshToken":"{{s.RefreshToken}}"}"""));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
