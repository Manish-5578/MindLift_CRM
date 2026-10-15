using Microsoft.Playwright;

namespace MindLift.E2E;

/// <summary>Screenshots for the UI checklist (plan 18.16). Each is saved with the page width in its name.</summary>
public static class Shots
{
    public static async Task SaveAsync(IPage page, string name)
    {
        Directory.CreateDirectory(E2EHost.ShotsDirectory);
        var width = page.ViewportSize?.Width ?? 0;
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(E2EHost.ShotsDirectory, $"{name}-{width}.png"),
            FullPage = true,
        });
    }
}
