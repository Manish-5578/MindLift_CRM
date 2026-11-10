namespace MindLift.Web.Services;

/// <summary>
/// Lets any page ask the app shell to switch between light and dark. The shell owns the theme, so pages (the profile page, for one)
/// send a request here and the shell applies it and tells the browser.
/// </summary>
public sealed class ThemeState
{
    /// <summary>Raised with true to switch to dark, false for light. The layout listens.</summary>
    public event Action<bool>? Requested;

    public void Request(bool dark) => Requested?.Invoke(dark);
}
