using MindLift.Shared.Contracts;

namespace MindLift.Web.Services;

/// <summary>
/// The signed-in state of the browser tab. The access token lives in memory only (plan 10.2).
/// The refresh token is an HttpOnly cookie, so a reload restores the session through /auth/refresh.
/// </summary>
public sealed class AuthSession
{
    public string? AccessToken { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }

    /// <summary>The signed-in user, loaded after sign-in.</summary>
    public MeResponse? Me { get; private set; }

    public bool IsSignedIn => AccessToken is not null && ExpiresAtUtc > DateTime.UtcNow;

    /// <summary>Set in a support view: the platform owner's name. A support view is read-only and is never refreshed.</summary>
    public string? SupportBy { get; private set; }

    public bool IsSupport => SupportBy is not null;

    public void StartSupport(TokenResponse tokens, string openedBy)
    {
        SupportBy = openedBy;
        SetTokens(tokens);
    }

    public event Action? Changed;

    public void SetTokens(TokenResponse tokens)
    {
        AccessToken = tokens.AccessToken;
        ExpiresAtUtc = tokens.ExpiresAtUtc;
        Changed?.Invoke();
    }

    public void SetMe(MeResponse me)
    {
        Me = me;
        Changed?.Invoke();
    }

    public void Clear()
    {
        AccessToken = null;
        ExpiresAtUtc = null;
        Me = null;
        SupportBy = null;
        Changed?.Invoke();
    }
}
