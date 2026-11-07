using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Mobile;

public sealed class MobileDeviceTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Can_register_heartbeat_and_revoke_current_mobile_device()
    {
        var staff = await Workspaces.SignupAsync(factory);

        var installId = Guid.NewGuid().ToString("N");
        var regRequest = new RegisterMobileDeviceRequest(
            InstallationId: installId,
            Platform: "android",
            Environment: "production",
            DeviceToken: "fcm_test_token_1234567890_abcdef",
            Provider: "fcm",
            AppVersion: "1.0.0",
            Locale: "en",
            TimeZoneId: "UTC",
            NotificationsAllowed: true);

        // 1. Register device
        var regResp = await staff.Client.PutAsJsonAsync("/api/v1/mobile/devices/current", regRequest);
        regResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deviceDto = await regResp.Content.ReadFromJsonAsync<MobileDeviceDto>();
        deviceDto.ShouldNotBeNull();
        deviceDto.InstallationId.ShouldBe(installId);
        deviceDto.Platform.ShouldBe("android");
        deviceDto.AppVersion.ShouldBe("1.0.0");
        deviceDto.RevokedAtUtc.ShouldBeNull();

        // 2. Heartbeat
        var hbResp = await staff.Client.PostAsJsonAsync("/api/v1/mobile/devices/current/heartbeat", new MobileDeviceHeartbeatRequest(installId, "1.0.1"));
        hbResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // 3. Re-register updates version and clears revocation
        var updateResp = await staff.Client.PutAsJsonAsync("/api/v1/mobile/devices/current", regRequest with { AppVersion = "1.0.2" });
        updateResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedDto = await updateResp.Content.ReadFromJsonAsync<MobileDeviceDto>();
        updatedDto.ShouldNotBeNull();
        updatedDto.AppVersion.ShouldBe("1.0.2");

        // 4. Revoke current device
        var revokeResp = await staff.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/mobile/devices/current")
        {
            Content = JsonContent.Create(new RevokeMobileDeviceRequest(installId))
        });
        revokeResp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Tenant_isolation_protects_mobile_devices()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        var installA = Guid.NewGuid().ToString("N");
        var regA = new RegisterMobileDeviceRequest(
            InstallationId: installA,
            Platform: "ios",
            Environment: "production",
            DeviceToken: "apns_token_tenant_a",
            Provider: "apns");

        (await tenantA.Client.PutAsJsonAsync("/api/v1/mobile/devices/current", regA)).EnsureSuccessStatusCode();

        // Tenant B attempting to revoke Tenant A's installation does not affect Tenant A
        var revokeFromB = await tenantB.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/mobile/devices/current")
        {
            Content = JsonContent.Create(new RevokeMobileDeviceRequest(installA))
        });
        revokeFromB.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Tenant A re-registers or heartbeats successfully
        var hbA = await tenantA.Client.PostAsJsonAsync("/api/v1/mobile/devices/current/heartbeat", new MobileDeviceHeartbeatRequest(installA));
        hbA.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}

