using System.Net.Http.Json;
using System.Text;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace MindLift.E2E;

/// <summary>
/// Document fields end to end, in a real browser against the real app (separate process, real PostgreSQL, real background queue):
/// an administrator defines and describes fields in Settings, a document is uploaded and read in the background, a person reviews it,
/// links a contact and approves, and the values arrive in the contact's own fields.
/// The first two tests need no model. The last uses the real Ollama model and the full path (image, Tesseract, language model); it runs when the model is installed.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DocumentAiE2ETests(E2EHost host)
{
    private async Task<(IPage Page, HttpClient Api)> StartAsync(int width = 1440)
    {
        var workspace = await host.NewWorkspaceAsync();
        var page = await host.NewPageAsync(width);
        await E2EHost.LoginAsync(page, workspace);
        return (page, host.ApiFor(workspace));
    }

    private static ILocator Drawer(IPage page) => page.Locator("[data-testid=form-drawer].mud-drawer--open");

    private static async Task<ContactDto> CreateContactAsync(HttpClient api, string first, string last)
    {
        var response = await api.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest(first, last, null, null, null, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    private static FilePayload TextFile(string name, string text) => new() { Name = name, MimeType = "text/plain", Buffer = Encoding.UTF8.GetBytes(text) };

    // ---------- Defining fields ----------

    [E2EFact]
    public async Task An_administrator_defines_fields_with_a_description_a_way_of_reading_and_a_record_field_to_save_to()
    {
        var (page, api) = await StartAsync();
        await page.GotoAsync("/settings/document-types");
        await Expect(page.Locator("[data-testid=ai-card]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=ai-card]")).ToContainTextAsync("Read documents with AI");

        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        var drawer = Drawer(page);
        await drawer.Locator("input[data-testid=dt-name]").FillAsync("Visa copy");
        await Expect(drawer.Locator("input[data-testid=dt-key]")).ToHaveValueAsync("visa_copy");
        await drawer.Locator("textarea[data-testid=dt-keywords]").FillAsync("VISA|3\nEntry permit|2");

        // First field: described in plain words, read automatically, saved to the contact's first name.
        var first = drawer.Locator("[data-testid=dt-field]").First;
        await first.Locator("input[data-testid=dt-field-label]").FillAsync("Holder first name");
        await first.Locator("textarea[data-testid=dt-field-description]").FillAsync("The visa holder's first name");
        await first.Locator("div[data-testid=dt-field-saveto]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Contact › First name" }).ClickAsync();

        // A second field added by hand, read only with an anchor, kept on the document.
        await drawer.Locator("[data-testid=add-dt-field]").ClickAsync();
        var second = drawer.Locator("[data-testid=dt-field]").Nth(1);
        await second.Locator("input[data-testid=dt-field-label]").FillAsync("Visa number");
        await second.Locator("div[data-testid=dt-field-mode]").ClickAsync();
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Patterns only" }).ClickAsync();
        await second.Locator("[data-testid=dt-field-more]").ClickAsync();
        await second.Locator("input[data-testid=dt-field-anchor]").FillAsync("Visa No:");
        await Shots.SaveAsync(page, "document-type-form");

        await drawer.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save", Exact = true }).ClickAsync();

        var card = page.Locator("[data-testid=doctype-visa_copy]");
        await Expect(card).ToContainTextAsync("Holder first name");
        await Expect(card).ToContainTextAsync("The visa holder's first name");
        await Expect(card).ToContainTextAsync("→ Contact › First name");
        await Expect(card).ToContainTextAsync("Visa number");

        // What was typed is what is stored.
        var stored = (await api.GetFromJsonAsync<List<DocumentTypeDto>>("/api/v1/settings/document-types"))!.Single(t => t.Key == "visa_copy");
        var holder = stored.Fields.Single(f => f.Label == "Holder first name");
        Assert.Equal("contacts.first_name", holder.SaveTo);
        Assert.Equal("The visa holder's first name", holder.Description);
        var number = stored.Fields.Single(f => f.Label == "Visa number");
        Assert.Equal(DocumentFieldModes.Rules, number.Mode);
        Assert.Equal("Visa No:", number.Anchor);
        Assert.Equal("holder_first_name", holder.Key);

        // And it is still there when the form is opened again.
        await card.Locator("[data-testid=edit-doctype]").ClickAsync();
        await Expect(drawer.Locator("textarea[data-testid=dt-field-description]").First).ToHaveValueAsync("The visa holder's first name");
    }

    [E2EFact]
    public async Task Ready_made_types_can_be_added_in_one_click()
    {
        var (page, api) = await StartAsync();
        await page.GotoAsync("/settings/document-types");

        await page.Locator("[data-testid=empty-add-starters]").ClickAsync();

        await Expect(page.Locator("[data-testid=doctype-appointment_receipt]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-testid=doctype-resume]")).ToContainTextAsync("→ Contact › Email");
        await Shots.SaveAsync(page, "document-types-starters");
        Assert.True((await api.GetFromJsonAsync<List<DocumentTypeDto>>("/api/v1/settings/document-types"))!.Count >= 8);
    }

    // ---------- Upload, background reading, review, approval (patterns, no model) ----------

    [E2EFact]
    public async Task A_document_uploaded_in_the_browser_is_read_in_the_background_and_approved_into_the_contact_it_is_linked_to()
    {
        var (page, api) = await StartAsync();
        var contact = await CreateContactAsync(api, "Pending", "Person");
        var type = new SaveDocumentTypeRequest("slip", "Appointment slip", null, true, 3,
            [new("Appointment Slip", 3), new("Given Name", 1)],
            [
                new("given_name", "Given name", DocumentValueTypes.Text, "Given Name", null, true, false, "The applicant's first name", null, null, DocumentFieldModes.Rules, "contacts.first_name"),
                new("surname", "Surname", DocumentValueTypes.Text, "Surname", null, true, false, "The applicant's surname", null, null, DocumentFieldModes.Rules, "contacts.last_name"),
                new("reference", "Reference", DocumentValueTypes.Id, "Reference No.", null, false, false, "The reference number", null, null, DocumentFieldModes.Rules, null),
            ]);
        (await api.PostAsJsonAsync("/api/v1/settings/document-types", type)).EnsureSuccessStatusCode();

        // Upload from the Documents page.
        await page.GotoAsync("/documents");
        await page.Locator("input[type=file]").SetInputFilesAsync(TextFile("slip.txt", "Appointment Slip\nReference No. REF-4471\nGiven Name Nadia\nSurname Hassan\n"));

        // It is queued and read in the background; the list updates by itself.
        var row = page.Locator("tr", new PageLocatorOptions { HasText = "slip.txt" });
        await Expect(row.Locator("[data-testid=doc-ocr-status]")).ToHaveTextAsync("Completed", new() { Timeout = 90_000 });
        await Expect(row.Locator("[data-testid=doc-key-fields]")).ToContainTextAsync("Given name:");
        await Expect(row.Locator("[data-testid=doc-key-fields]")).ToContainTextAsync("Nadia");
        await Shots.SaveAsync(page, "documents-list-read");

        // Review: every field is listed with where it came from.
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Review" }).ClickAsync();
        await Expect(page.Locator("[data-testid=doc-type-chip]")).ToHaveTextAsync("Appointment slip");
        var given = page.Locator("[data-testid=doc-field][data-field=given_name]");
        await Expect(given.Locator("input[data-testid=field-input]")).ToHaveValueAsync("Nadia");
        await Expect(given.Locator("[data-testid=field-method]")).ToHaveTextAsync("Pattern");
        await Expect(given.Locator("[data-testid=field-save-to]")).ToContainTextAsync("Contact › First name");

        // Nothing is linked yet, so the value cannot be saved to a record.
        await Expect(given.Locator("[data-testid=field-save-to]")).ToContainTextAsync("Link a Contact above to save this.");

        // Link the contact and approve.
        await page.Locator("input[data-testid=doc-link-contact]").FillAsync("Pend");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Pending Person" }).ClickAsync();
        await Expect(given.Locator("[data-testid=field-save-to]")).Not.ToContainTextAsync("Link a Contact above");
        await Shots.SaveAsync(page, "document-review-linked");

        await page.Locator("[data-testid=doc-approve]").ClickAsync();
        await Expect(page.Locator("[data-testid=approve-apply-list]")).ToContainTextAsync("Contact › First name");
        await Expect(page.Locator("[data-testid=approve-apply-list]")).ToContainTextAsync("Nadia");
        await page.Locator("[data-testid=approve-confirm]").ClickAsync();

        await Expect(page.Locator("[data-testid=doc-status]")).ToHaveTextAsync("Approved");
        var saved = (await api.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!;
        Assert.Equal("Nadia", saved.FirstName);
        Assert.Equal("Hassan", saved.LastName);
    }

    [E2EFact]
    public async Task A_document_uploaded_from_a_contact_page_is_linked_to_that_contact_and_saves_its_values_there()
    {
        var (page, api) = await StartAsync();
        var contact = await CreateContactAsync(api, "Pending", "Person");
        var type = new SaveDocumentTypeRequest("card", "Card", null, true, 2, [new("Member Card", 3)],
            [
                new("job", "Job title", DocumentValueTypes.Text, "Role:", null, false, false, "The member's role", null, null, DocumentFieldModes.Rules, "contacts.job_title"),
                new("mail", "Email", DocumentValueTypes.Email, "Email:", null, false, false, "The member's email", null, null, DocumentFieldModes.Rules, "contacts.email"),
            ]);
        (await api.PostAsJsonAsync("/api/v1/settings/document-types", type)).EnsureSuccessStatusCode();

        await page.GotoAsync($"/contacts/{contact.Id}");
        await Expect(page.Locator("[data-testid=no-record-documents]")).ToBeVisibleAsync();
        await page.Locator("input[data-testid=record-doc-file]").SetInputFilesAsync(TextFile("card.txt", "Member Card\nRole: Head of Sales\nEmail: Pending@Example.test\n"));

        var row = page.Locator("[data-testid=record-document]", new PageLocatorOptions { HasText = "card.txt" });
        await Expect(row.Locator("[data-testid=record-document-status]")).ToHaveTextAsync("Needs review", new() { Timeout = 90_000 });
        await Expect(row).ToContainTextAsync("Job title: Head of Sales");
        await Shots.SaveAsync(page, "contact-documents");

        await row.Locator("[data-testid=record-document-link]").ClickAsync();
        await Expect(page.Locator("[data-testid=doc-field][data-field=job] [data-testid=field-save-to]")).ToContainTextAsync("Contact › Job title");
        await Expect(page.Locator("[data-testid=doc-field][data-field=job] [data-testid=field-save-to]")).Not.ToContainTextAsync("Link a Contact");
        await page.Locator("[data-testid=doc-approve]").ClickAsync();
        await page.Locator("[data-testid=approve-confirm]").ClickAsync();
        await Expect(page.Locator("[data-testid=doc-status]")).ToHaveTextAsync("Approved");

        var saved = (await api.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!;
        Assert.Equal("Head of Sales", saved.JobTitle);
        Assert.Equal("pending@example.test", saved.Email);
    }

    [E2EFact]
    public async Task A_missing_field_can_be_filled_in_by_hand_on_the_review_screen_and_is_marked_as_edited()
    {
        var (page, api) = await StartAsync();
        var type = new SaveDocumentTypeRequest("slip2", "Slip two", null, true, 2, [new("Slip Two", 3)],
            [
                new("surname", "Surname", DocumentValueTypes.Text, "Surname", null, true, false, null, null, null, DocumentFieldModes.Rules, null),
                new("city", "City", DocumentValueTypes.Text, "City", null, false, false, null, null, null, DocumentFieldModes.Rules, null),
            ]);
        (await api.PostAsJsonAsync("/api/v1/settings/document-types", type)).EnsureSuccessStatusCode();
        await page.GotoAsync("/documents");
        await page.Locator("input[type=file]").SetInputFilesAsync(TextFile("two.txt", "Slip Two\nSurname Rao\n"));
        var row = page.Locator("tr", new PageLocatorOptions { HasText = "two.txt" });
        await Expect(row.Locator("[data-testid=doc-ocr-status]")).ToHaveTextAsync("Completed", new() { Timeout = 90_000 });
        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Review" }).ClickAsync();

        var city = page.Locator("[data-testid=doc-field][data-field=city]");
        await Expect(city.Locator("input[data-testid=field-input]")).ToHaveValueAsync(string.Empty);
        await city.Locator("input[data-testid=field-input]").FillAsync("Chennai");
        await page.Locator("[data-testid=doc-save-edits]").ClickAsync();

        await Expect(page.Locator("[data-testid=doc-field][data-field=city] [data-testid=field-method]")).ToHaveTextAsync("Edited");
    }

    // ---------- The whole path with the real model ----------

    private const string Reference = "26-1048822310";

    /// <summary>A picture of a printed appointment receipt, so the document goes through Tesseract before the language model reads its text.</summary>
    private static byte[] ReceiptPicture()
    {
        string[] lines =
        [
            "Passport Seva", "Online Application Receipt", $"Application Reference No. {Reference}", "Given Name ANITA", "Surname RAMANATHAN",
            "Gender FEMALE", "Date Of Birth 12/03/1990", "Place of Birth CHENNAI", "Appointment Date 24/08/2026", "Appointment Time 03:00 PM", "Total Fee (Rs.) 1500.00",
        ];
        using var bitmap = new SKBitmap(1700, 1100);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var font = new SKFont(SKTypeface.Default, 46))
        {
            canvas.Clear(SKColors.White);
            for (var i = 0; i < lines.Length; i++)
            {
                canvas.DrawText(lines[i], 60, 90 + i * 88, font, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [E2ELlmFact]
    public async Task The_real_model_reads_a_scanned_receipt_and_the_checked_values_end_up_in_the_contacts_own_fields()
    {
        var (page, api) = await StartAsync();
        var contact = await CreateContactAsync(api, "Pending", "Applicant");

        // Switch AI reading on in Settings and add the ready-made types.
        await page.GotoAsync("/settings/document-types");
        await Expect(page.Locator("[data-testid=ai-status]")).ToContainTextAsync("is ready");
        await page.Locator("[data-testid=ai-toggle]").First.ClickAsync();
        await Expect(page.Locator("[data-testid=ai-card]")).ToContainTextAsync("AI reading is on");
        await page.Locator("[data-testid=empty-add-starters]").ClickAsync();
        await Expect(page.Locator("[data-testid=doctype-appointment_receipt]")).ToBeVisibleAsync();

        // Upload the picture. It is queued, then read in the background: Tesseract for the text, the model for the fields.
        await page.GotoAsync("/documents");
        await page.Locator("input[type=file]").SetInputFilesAsync(new FilePayload { Name = "receipt.png", MimeType = "image/png", Buffer = ReceiptPicture() });
        var row = page.Locator("tr", new PageLocatorOptions { HasText = "receipt.png" });
        await Expect(row.Locator("[data-testid=doc-ocr-status]")).ToHaveTextAsync("Completed", new() { Timeout = 420_000 });
        await Shots.SaveAsync(page, "documents-list-ai-read");

        await row.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Review" }).ClickAsync();
        await Expect(page.Locator("[data-testid=doc-type-chip]")).ToHaveTextAsync("Appointment receipt");
        await Expect(page.Locator("[data-testid=doc-method]")).ToContainTextAsync("AI");

        async Task ShouldReadAsync(string key, string expected)
        {
            var input = page.Locator($"[data-testid=doc-field][data-field={key}] input[data-testid=field-input]");
            await Expect(input).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(expected), System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        await ShouldReadAsync("given_name", "ANITA");
        await ShouldReadAsync("surname", "RAMANATHAN");
        await ShouldReadAsync("application_reference_no", Reference);
        await ShouldReadAsync("date_of_birth", "1990-03-12");
        await Expect(page.Locator("[data-testid=doc-field][data-field=given_name] [data-testid=field-method]")).ToHaveTextAsync("AI");
        await Expect(page.Locator("[data-testid=doc-field][data-field=given_name] [data-testid=field-snippet]")).ToContainTextAsync("ANITA");
        await Shots.SaveAsync(page, "document-review-ai");

        // Link the contact, approve, and the names are saved into it.
        await page.Locator("input[data-testid=doc-link-contact]").FillAsync("Pend");
        await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = "Pending Applicant" }).ClickAsync();
        await page.Locator("[data-testid=doc-approve]").ClickAsync();
        await Expect(page.Locator("[data-testid=approve-apply-list]")).ToContainTextAsync("First name");
        await page.Locator("[data-testid=approve-confirm]").ClickAsync();
        await Expect(page.Locator("[data-testid=doc-status]")).ToHaveTextAsync("Approved");

        var saved = (await api.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!;
        Assert.Equal("ANITA", saved.FirstName, ignoreCase: true);
        Assert.Equal("RAMANATHAN", saved.LastName, ignoreCase: true);
    }
}
