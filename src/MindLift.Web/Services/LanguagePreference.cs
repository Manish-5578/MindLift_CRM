using Microsoft.JSInterop;
using MindLift.UI.Localization;

namespace MindLift.Web.Services;

/// <summary>The language a person chose in this browser, saved with the language switch.</summary>
public static class LanguagePreference
{
    private const string StorageKey = "ml.lang";

    /// <summary>Uses the saved choice, if there is one. A person who has not chosen keeps the language of their profile.</summary>
    public static async Task ApplySavedAsync(IJSRuntime js, Translator translator)
    {
        try
        {
            var saved = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (saved is "ar" or "en")
            {
                translator.Choose(saved);
            }
        }
        catch (JSException)
        {
            // Browser storage can be blocked. The profile language is used instead.
        }
    }

    /// <summary>Sets the page's language and direction so the browser lays text out right to left in Arabic.</summary>
    public static async Task ApplyDirectionAsync(IJSRuntime js, Translator translator)
    {
        try
        {
            await js.InvokeVoidAsync("mindlift.applyLanguage", translator.Language);
        }
        catch (JSException)
        {
            // The text still switches; only the page direction attribute is missed.
        }
    }
}
