using System.Net.Http.Json;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.UI.Localization;

namespace MindLift.Web.Services;

/// <summary>
/// The workspace configuration loaded from /api/v1/app/bootstrap after sign-in (plan 7.2).
/// Pages read modules, labels and permissions from here, never hard-coded.
/// </summary>
public sealed class AppState(HttpClient http, AuthSession session, Translator translator)
{
    public BootstrapDto? Bootstrap { get; private set; }

    /// <summary>Where the workspace is in the setup wizard. Only loaded for admins, who are the only people who can run it.</summary>
    public OnboardingDto? Onboarding { get; private set; }

    /// <summary>True when an admin has not finished or skipped through the setup wizard yet.</summary>
    public bool SetupPending => IsAdmin && Onboarding is { CurrentStep: < OnboardingSteps.Completed };

    /// <summary>Arabic text and layout (right to left). The language is the person's own choice, or their profile's language until they make one.</summary>
    public bool IsArabic => translator.IsArabic;

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
            translator.UseDefault(Bootstrap.User.Language);
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

    /// <summary>
    /// The label of a picklist choice. In Arabic it is the Arabic name the workspace stores for it, or the translated English name. An unknown code is shown as it is.
    /// </summary>
    public string PicklistLabel(string picklistKey, string? code)
    {
        var value = Bootstrap?.Picklists.FirstOrDefault(p => p.Key == picklistKey)?.Values.FirstOrDefault(v => v.Value == code);
        return value is null ? code ?? string.Empty : translator.Pick(value.Label, value.LabelAr);
    }

    public bool IsAdmin => Bootstrap?.User.IsAdmin == true;
}
