using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Pages that load their data show a skeleton while waiting, then the real content or a clear empty state.</summary>
[Collection(E2ECollection.Name)]
public sealed class LoadingStatesE2ETests(E2EHost host)
{
    [E2EFact]
    public async Task Audit_trail_recycle_bin_competitors_and_inbox_leave_the_skeleton_and_show_their_content()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(1440);
        await E2EHost.LoginAsync(page, workspace);

        foreach (var (path, loading) in new[]
        {
            ("/audit-trail", "audit-loading"),
            ("/settings/recycle-bin", "recycle-loading"),
            ("/settings/competitors", "competitors-loading"),
            ("/inbox", "inbox-loading"),
        })
        {
            await page.GotoAsync(path);
            await Expect(page.Locator("[data-testid=" + loading + "]")).ToHaveCountAsync(0, new() { Timeout = 20000 });
            await Expect(page.Locator("[data-testid=skeleton-table]")).ToHaveCountAsync(0);
            await Expect(page.Locator("body")).Not.ToContainTextAsync("We could not");
        }
    }
}
