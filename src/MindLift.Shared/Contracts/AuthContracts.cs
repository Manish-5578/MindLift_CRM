namespace MindLift.Shared.Contracts;

public sealed record SignupRequest(
    string CompanyName,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? Phone,
    string Country,
    string Language,
    bool AcceptTerms);

public sealed record LoginRequest(string Email, string Password);

/// <summary>Returned by login, signup, refresh and invitation acceptance.</summary>
/// <param name="AccessToken">Short-lived bearer token (15 minutes).</param>
/// <param name="ExpiresAtUtc">When the access token expires.</param>
/// <param name="RefreshToken">Only returned to mobile clients (header X-Client: mobile). Web clients receive it as an HttpOnly cookie.</param>
public sealed record TokenResponse(string AccessToken, DateTime ExpiresAtUtc, string? RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record VerifyEmailRequest(string Email, string Token);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MeResponse(
    Guid UserId,
    Guid TenantId,
    string TenantName,
    string Email,
    string FullName,
    string Language,
    bool EmailConfirmed,
    bool IsTenantOwner,
    bool IsPlatformAdmin,
    Guid? RoleId,
    string? RoleName,
    bool IsAdmin,
    string TenantStatus,
    DateTime? TrialEndsAtUtc);

/// <summary>Refresh token sent in the body by mobile clients. Web clients send the HttpOnly cookie instead.</summary>
public sealed record RefreshRequest(string? RefreshToken);
