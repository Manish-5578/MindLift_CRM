using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Administration;
using MindLift.Infrastructure.Auth;
using MindLift.Shared.Contracts;

namespace MindLift.Api.Controllers;

/// <summary>
/// Sign-in, sign-up, sessions, passwords and email (plan 4.3, 6.2).
/// Self-service actions (me, profile, change-password) are open to any signed-in user and only ever touch the caller's own data.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("public")]
public sealed class AuthController(
    AuthService auth,
    InvitationService invitations,
    UserAdministrationService users,
    ITenantContext tenant) : ControllerBase
{
    private const string RefreshCookieName = "ml_refresh";
    private const string MobileHeader = "X-Client";

    [AllowAnonymous]
    [HttpPost("signup")]
    public async Task<ActionResult<TokenResponse>> SignUp([FromBody] SignupRequest request, CancellationToken cancellationToken)
        => Issue(await auth.SignUpAsync(request, cancellationToken));

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => Issue(await auth.LoginAsync(request.Email, request.Password, cancellationToken));

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<TokenResponse>> Refresh([FromBody] RefreshRequest? body, CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookieName] ?? body?.RefreshToken
            ?? throw new UnauthorizedException("invalid_refresh_token", "Your session has ended. Please sign in again.");
        return Issue(await auth.RefreshAsync(token, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? body, CancellationToken cancellationToken)
    {
        var token = Request.Cookies[RefreshCookieName] ?? body?.RefreshToken;
        if (!string.IsNullOrEmpty(token))
        {
            await auth.LogoutAsync(token, cancellationToken);
        }

        Response.Cookies.Delete(RefreshCookieName, RefreshCookieOptions(DateTimeOffset.UnixEpoch));
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
        => Ok(await auth.GetMeAsync(CurrentUserId(), cancellationToken));

    [Authorize]
    [HttpPut("profile")]
    public async Task<ActionResult<UserDetail>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
        => Ok(await users.UpdateProfileAsync(CurrentUserId(), request, cancellationToken));

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(CurrentUserId(), request.CurrentPassword, request.NewPassword, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("send-verification")]
    public async Task<IActionResult> SendVerification(CancellationToken cancellationToken)
    {
        await auth.SendVerificationEmailAsync(CurrentUserId(), cancellationToken);
        return Accepted();
    }

    /// <summary>Always returns 202, so the response never reveals whether the email belongs to an account.</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ForgotPasswordAsync(request.Email, cancellationToken);
        return Accepted();
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ResetPasswordAsync(request.Email, request.Token, request.NewPassword, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        await auth.VerifyEmailAsync(request.Email, request.Token, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("first-login-change-password")]
    public async Task<ActionResult<TokenResponse>> FirstLoginChangePassword([FromBody] FirstLoginChangePasswordRequest request, CancellationToken cancellationToken)
        => Issue(await auth.FirstLoginChangePasswordAsync(CurrentUserId(), request.NewPassword, cancellationToken));

    [AllowAnonymous]
    [HttpGet("invitations/{token}")]
    public async Task<ActionResult<InvitationPreview>> PreviewInvitation(string token, CancellationToken cancellationToken)
        => Ok(await invitations.PreviewAsync(token, cancellationToken));

    [AllowAnonymous]
    [HttpPost("accept-invite")]
    public async Task<ActionResult<TokenResponse>> AcceptInvitation([FromBody] AcceptInvitationRequest request, CancellationToken cancellationToken)
        => Issue(await invitations.AcceptAsync(request.Token, request.Password, cancellationToken));

    private Guid CurrentUserId() => tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");

    /// <summary>
    /// Web clients receive the refresh token as an HttpOnly cookie, which scripts cannot read.
    /// Mobile clients (header X-Client: mobile) receive it in the body and store it in secure storage.
    /// </summary>
    private TokenResponse Issue(AuthResult result)
    {
        var isMobile = string.Equals(Request.Headers[MobileHeader], "mobile", StringComparison.OrdinalIgnoreCase);
        if (!isMobile)
        {
            Response.Cookies.Append(RefreshCookieName, result.RefreshToken, RefreshCookieOptions(DateTimeOffset.UtcNow.AddDays(14)));
        }

        return new TokenResponse(result.AccessToken, result.ExpiresAtUtc, isMobile ? result.RefreshToken : null, result.MustChangePassword);
    }

    private static CookieOptions RefreshCookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        Expires = expires,
    };
}
