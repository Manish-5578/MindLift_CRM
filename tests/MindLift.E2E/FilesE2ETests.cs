using System.Net.Http.Json;
using System.Text;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>The Files card on record pages: upload, list, download and delete.</summary>
[Collection(E2ECollection.Name)]
public sealed class FilesE2ETests(E2EHost host)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");

    private async Task<(IPage Page, ContactDto Contact)> StartAsync()
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync();
        await E2EHost.LoginAsync(page, workspace);
        var api = host.ApiFor(workspace);
        var contact = await (await api.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Files", "Tester", null, null, null, null, null, null, null, null))).Content.ReadFromJsonAsync<ContactDto>();
        return (page, contact!);
    }

    [E2EFact]
    public async Task A_file_can_be_uploaded_to_a_contact_downloaded_and_deleted()
    {
        var (page, contact) = await StartAsync();
        await page.GotoAsync($"/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=no-files]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=record-files] [data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload { Name = "contract.pdf", MimeType = "application/pdf", Buffer = Pdf });
        await Expect(page.Locator("[data-testid=record-file]")).ToHaveCountAsync(1);
        await Expect(page.Locator("[data-testid=record-file]")).ToContainTextAsync("contract.pdf");

        var download = await page.RunAndWaitForDownloadAsync(() => page.Locator("[data-testid=file-download]").ClickAsync());
        Assert.Equal("contract.pdf", download.SuggestedFilename);
        Assert.StartsWith("%PDF-", await File.ReadAllTextAsync((await download.PathAsync())!));

        await page.Locator("[data-testid=file-delete]").ClickAsync();
        await page.Locator("[data-testid=confirm-ok]").ClickAsync();
        await Expect(page.Locator("[data-testid=no-files]")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task A_file_whose_content_does_not_match_its_name_is_refused_with_a_message()
    {
        var (page, contact) = await StartAsync();
        await page.GotoAsync($"/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=no-files]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=record-files] [data-testid=file-dropzone] input[type=file]").SetInputFilesAsync(new FilePayload { Name = "fake.pdf", MimeType = "application/pdf", Buffer = Encoding.ASCII.GetBytes("this is not a pdf") });

        await Expect(page.Locator(".mud-snackbar").First).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=no-files]")).ToBeVisibleAsync();
    }
}
