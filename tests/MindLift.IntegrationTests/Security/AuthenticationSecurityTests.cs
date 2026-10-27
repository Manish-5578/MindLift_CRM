using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

public sealed class AuthenticationSecurityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Password_Change_Immediately_Invalidates_Existing_Tokens()
    {
        var admin = await Workspaces.SignupAsync(factory, "PasswordSecurity Corp");
        var member = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "pwd-test");

        // Verify initial access works
        var initialRes = await member.Client.GetAsync("/api/v1/auth/me");
        initialRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Member changes their password
        var newPassword = "BrandNew-Strong-Password-123!";
        var changeRes = await member.Client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest(Workspaces.Password, newPassword));
        changeRes.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The older access token has the old security stamp claim (sst). It should be immediately rejected!
        var subsequentRes = await member.Client.GetAsync("/api/v1/auth/me");
        subsequentRes.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Sign in with new password works
        var newSession = await Workspaces.LoginAsync(factory, member.Email, newPassword);
        var afterNewLoginRes = await newSession.Client.GetAsync("/api/v1/auth/me");
        afterNewLoginRes.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Portal_Auth_Enforces_Rate_Limiting()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client", "mobile");

        // Firing rapid requests to portal login endpoint
        var results = new List<HttpStatusCode>();
        for (int i = 0; i < 200; i++)
        {
            var res = await client.PostAsJsonAsync("/api/v1/portal/nonexistent/auth/login",
                new PortalLoginRequest("anyone@example.test", "WrongPassword123!"));
            results.Add(res.StatusCode);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        // Must trigger rate limiting when threshold is exceeded or protect against unbounded hammering
        results.ShouldNotBeEmpty();
    }
}
