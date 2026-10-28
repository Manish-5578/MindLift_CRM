using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Auth;

/// <summary>Plan 4.3 and 6.2: sign-up, sign-in with lockout, refresh rotation with reuse detection, and password reset.</summary>
public sealed class AuthFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Signup_creates_a_workspace_with_default_roles_and_an_admin_owner()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        me!.Email.ShouldBe(owner.Email);
        me.RoleName.ShouldBe("Admin");
        me.IsAdmin.ShouldBeTrue();
        me.IsTenantOwner.ShouldBeTrue();
        me.TenantStatus.ShouldBe("Trial");
        me.TrialEndsAtUtc.ShouldNotBeNull();

        var roles = await owner.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        roles!.Select(r => r.Name).OrderBy(n => n).ShouldBe(["Admin", "Manager", "Staff"]);
    }

    [Fact]
    public async Task Signing_up_twice_with_the_same_email_returns_uniform_success_without_enumerating()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync("/api/v1/auth/signup",
            new SignupRequest("Another company", "Sara", "Khan", owner.Email, Workspaces.Password, null, "AE", "en", true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        token.ShouldNotBeNull();
        token.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Weak_passwords_are_rejected_with_a_field_error()
    {
        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync("/api/v1/auth/signup",
            new SignupRequest("Weak Co", "Sara", "Khan", $"weak-{Guid.NewGuid():N}@example.test", "short", null, "AE", "en", true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var client = Workspaces.MobileClient(factory);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(owner.Email, "Wrong0Password"));
            wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await Workspaces.ErrorCodeAsync(wrong)).ShouldBe("invalid_credentials");
        }

        var fifth = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(owner.Email, "Wrong0Password"));
        (await Workspaces.ErrorCodeAsync(fifth)).ShouldBe("account_locked");

        var correct = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(owner.Email, Workspaces.Password));
        correct.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Workspaces.ErrorCodeAsync(correct)).ShouldBe("account_locked");
    }

    [Fact]
    public async Task Unknown_email_and_wrong_password_return_the_same_error()
    {
        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest($"nobody-{Guid.NewGuid():N}@example.test", "Wrong0Password"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Workspaces.ErrorCodeAsync(response)).ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reusing_an_old_one_ends_the_whole_session()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var client = Workspaces.MobileClient(factory);

        var first = owner.RefreshToken;
        var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first));
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = (await rotated.Content.ReadFromJsonAsync<TokenResponse>())!.RefreshToken!;
        second.ShouldNotBe(first);

        // Presenting the old token again means it was copied: the whole family is revoked.
        var reused = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(first));
        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Workspaces.ErrorCodeAsync(reused)).ShouldBe("refresh_token_reused");

        var newest = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(second));
        newest.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgot_password_always_returns_accepted_and_reset_changes_the_password()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var client = Workspaces.MobileClient(factory);

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest($"nobody-{Guid.NewGuid():N}@example.test"));
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var known = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(owner.Email));
        known.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var token = factory.Emails.LatestTokenFor(owner.Email);
        var reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new ResetPasswordRequest(owner.Email, token, "NewStr0ngPass"));
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var oldPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(owner.Email, Workspaces.Password));
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(owner.Email, "NewStr0ngPass"));
        newPassword.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Me_requires_a_valid_token()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
