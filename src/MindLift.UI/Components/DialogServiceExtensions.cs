using MudBlazor;

namespace MindLift.UI.Components;

public static class DialogServiceExtensions
{
    /// <summary>
    /// Asks the user to confirm an action. Returns true only when they press the confirm button;
    /// Cancel, Esc and closing the dialog all return false. Use <paramref name="danger"/> for deletes and other risky actions.
    /// </summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirmText = "Confirm", bool danger = false)
    {
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { x => x.Title, title },
            { x => x.Message, message },
            { x => x.ConfirmText, confirmText },
            { x => x.Danger, danger },
        };

        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, BackdropClick = false, CloseButton = false };
        var reference = await dialogs.ShowAsync<ConfirmDialog>(title, parameters, options);
        var result = await reference.Result;
        return result is { Canceled: false } && result.Data is true;
    }

    /// <summary>
    /// Asks the user for a required reason, for example why a lead is disqualified. Returns the trimmed text,
    /// or null when they cancel. An empty reason is never returned.
    /// </summary>
    public static async Task<string?> PromptReasonAsync(this IDialogService dialogs, string title, string? message = null, string label = "Reason *", string confirmText = "Confirm", bool danger = false)
    {
        var parameters = new DialogParameters<ReasonDialog>
        {
            { x => x.Title, title },
            { x => x.Message, message },
            { x => x.Label, label },
            { x => x.ConfirmText, confirmText },
            { x => x.Danger, danger },
        };

        var options = new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true, BackdropClick = false, CloseButton = false };
        var reference = await dialogs.ShowAsync<ReasonDialog>(title, parameters, options);
        var result = await reference.Result;
        return result is { Canceled: false } && result.Data is string reason ? reason : null;
    }
}
