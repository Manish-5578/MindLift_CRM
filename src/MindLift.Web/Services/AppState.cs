using System.Net.Http.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Web.Services;

/// <summary>
/// The workspace configuration loaded from /api/v1/app/bootstrap after sign-in (plan 7.2).
/// Pages read modules, labels and permissions from here, never hard-coded.
/// </summary>
public sealed class AppState(HttpClient http, AuthSession session)
{
    public BootstrapDto? Bootstrap { get; private set; }

    /// <summary>Where the workspace is in the setup wizard. Only loaded for admins, who are the only people who can run it.</summary>
    public OnboardingDto? Onboarding { get; private set; }

    /// <summary>True when an admin has not finished or skipped through the setup wizard yet.</summary>
    public bool SetupPending => IsAdmin && Onboarding is { CurrentStep: < OnboardingSteps.Completed };

    /// <summary>Arabic layout (right to left) when the signed-in user's language is Arabic.</summary>
    public bool IsArabic => Bootstrap?.User is { } user && user.Language == "ar";

    public event Action? Changed;

    public async Task<bool> LoadAsync()
    {
        var response = await http.GetAsync("api/v1/app/bootstrap");
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        Bootstrap = await response.Content.ReadFromJsonAsync<BootstrapDto>();
        if (Bootstrap is not null)
        {
            session.SetMe(Bootstrap.User);
            Onboarding = await LoadOnboardingAsync();
        }

        Changed?.Invoke();
        return Bootstrap is not null;
    }

    private async Task<OnboardingDto?> LoadOnboardingAsync()
    {
        if (!IsAdmin)
        {
            return null;
        }

        try
        {
            var response = await http.GetAsync("api/v1/onboarding");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<OnboardingDto>() : null;
        }
        catch (HttpRequestException)
        {
            // If this cannot be read, do not trap the user in the wizard.
            return null;
        }
    }

    public void Reset()
    {
        Bootstrap = null;
        Onboarding = null;
        Changed?.Invoke();
    }

    /// <summary>True when the module is enabled and the user may view it.</summary>
    public bool CanView(AppModule module)
    {
        var setting = Bootstrap?.Modules.FirstOrDefault(m => m.Module == module.ToString());
        var permission = Bootstrap?.Permissions.FirstOrDefault(p => p.Module == module.ToString());
        return setting is { IsEnabled: true } && permission is { CanView: true };
    }

    public string ModuleLabel(AppModule module, bool plural = true)
    {
        var setting = Bootstrap?.Modules.FirstOrDefault(m => m.Module == module.ToString());
        if (setting is null)
        {
            return module.ToString();
        }

        return plural ? setting.PluralLabel : setting.SingularLabel;
    }

    public bool IsAdmin => Bootstrap?.User.IsAdmin == true;
}
