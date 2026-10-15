using System.Text;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>Settings > Document types: a person defines the values to read, tests them on a sample and sees them saved.</summary>
[Collection(E2ECollection.Name)]
public sealed class DocumentTypesE2ETests(E2EHost host)
{
    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open, .mud-drawer--open").First;

    [E2EFact]
    public async Task A_document_type_can_be_defined_tested_on_a_sample_and_saved()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/settings/document-types");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=dt-key]").FillAsync("e2e_licence");
        await Drawer(page).Locator("input[data-testid=dt-name]").FillAsync("E2E licence");
        await Drawer(page).Locator("textarea[data-testid=dt-keywords]").FillAsync("TRADE LICENSE|3\nLicense No|2");
        await Drawer(page).Locator("input[data-testid=dt-field-label]").FillAsync("Licence number");
        await Drawer(page).Locator("input[data-testid=dt-field-anchor]").FillAsync("License No:");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        // Saved with a key and a name, then the test panel appears when it is opened for editing again.
        await Expect(page.Locator("[data-testid=doctype-e2e_licence]")).ToBeVisibleAsync();
        await page.Locator("[data-testid=doctype-e2e_licence]").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit" }).ClickAsync();
        await Drawer(page).Locator("[data-testid=dt-test-file]").SetInputFilesAsync(new FilePayload
        {
            Name = "licence.txt",
            MimeType = "text/plain",
            Buffer = Encoding.UTF8.GetBytes("TRADE LICENSE\nLicense No: CN-7654321\n"),
        });

        await Expect(Drawer(page).Locator("[data-testid=dt-test-summary]")).ToContainTextAsync("Matched");
        await Expect(Drawer(page).Locator("[data-testid=dt-test-field]")).ToContainTextAsync("CN-7654321");
        await Shots.SaveAsync(page, "settings-document-types");
    }

    [E2EFact]
    public async Task A_definition_missing_its_key_is_refused_next_to_the_field()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        await page.GotoAsync("/settings/document-types");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await Drawer(page).Locator("input[data-testid=dt-name]").FillAsync("No key yet");
        await Drawer(page).GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("[data-testid=doctype-error]")).ToBeVisibleAsync();
    }
}
