using Microsoft.AspNetCore.Components;
using MindLift.Portal.Services;
using MindLift.Shared.Contracts;

namespace MindLift.Portal.Components;

/// <summary>
/// Base for every portal page. Loads the company (slug) branding, resumes the session from the refresh cookie,
/// and sends signed-out customers to the sign-in page when the page needs a session.
/// </summary>
public abstract class PortalPageBase : ComponentBase, IDisposable
{
    [Parameter] public string Slug { get; set; } = string.Empty;

    [Inject] protected PortalSession Session { get; set; } = default!;
    [Inject] protected PortalText T { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected HttpClient Http { get; set; } = default!;

    /// <summary>False for sign-in, magic link and invite pages.</summary>
    protected virtual bool RequiresSignIn => true;

    protected bool Ready { get; private set; }
    protected bool UnknownCompany => Ready && Session.Branding is null;

    protected string Api(string path) => $"api/v1/portal/{path}";
    protected string Link(string path = "") => string.IsNullOrEmpty(path) ? Slug : $"{Slug}/{path}";

    /// <summary>The "code" extension of an RFC 7807 problem response, or null.</summary>
    protected static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The "detail" field of an RFC 7807 problem response, or fallback.</summary>
    protected static async Task<string> DetailAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return detail.GetString() ?? fallback;
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return fallback;
    }

    /// <summary>Signs the customer in on this device and opens the portal home in their language.</summary>
    protected async Task CompleteSignInAsync(PortalLoginResponse login)
    {
        Session.SignIn(login);
        await T.SetLanguageAsync(login.Language);
        Nav.NavigateTo(Link());
    }

    protected override void OnInitialized()
    {
        T.Changed += Refresh;
    }

    protected override async Task OnParametersSetAsync()
    {
        Ready = false;
        await Session.EnsureCompanyAsync(Slug);
        if (Session.User is { Language: { } lang } && lang != T.Language)
        {
            await T.SetLanguageAsync(lang);
        }
        else if (Session.User is null && Session.Branding?.DefaultLanguage is { } def && def != T.Language)
        {
            await T.SetLanguageAsync(def);
        }

        Ready = true;
        if (Session.Branding is null)
        {
            return;
        }

        if (RequiresSignIn && !Session.IsSignedIn)
        {
            Nav.NavigateTo(Link("login"));
            return;
        }

        await LoadAsync();
    }

    /// <summary>Loads the page data once the company and session are known.</summary>
    protected virtual Task LoadAsync() => Task.CompletedTask;

    private void Refresh() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        T.Changed -= Refresh;
        GC.SuppressFinalize(this);
    }
}
