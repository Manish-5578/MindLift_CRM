using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Platform;

/// <summary>The exact requests the mobile contacts, accounts, deals, calendar, documents, avatar and quiet-hours screens make.</summary>
public sealed class MobileWorkspaceContractTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.ShouldBeTrue(text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Contacts_accounts_and_deals_lists_and_details_load()
    {
        var s = await Workspaces.SignupAsync(factory);
        var account = await Ok(await s.Client.PostAsync("/api/v1/accounts", Json("""{"name":"Acme Ltd","website":"acme.test","phone":"+971500000010","industry":"Logistics"}""")));
        var accountId = account.GetProperty("id").GetString();
        var contact = await Ok(await s.Client.PostAsync("/api/v1/contacts", Json(
            $$"""{"firstName":"Rita","lastName":"Shaw","email":"rita@acme.test","phone":"+971500000011","jobTitle":"Buyer","accountId":"{{accountId}}"}""")));
        var contactId = contact.GetProperty("id").GetString();

        var contacts = await Ok(await s.Client.PostAsync("/api/v1/contacts/query", Json("""{"search":"Rita","page":1,"pageSize":30,"sortField":"created_at","sortDir":"desc"}""")));
        contacts.GetProperty("total").GetInt32().ShouldBe(1);
        contacts.GetProperty("items")[0].GetProperty("fullName").GetString().ShouldBe("Rita Shaw");
        (await Ok(await s.Client.GetAsync($"/api/v1/contacts/{contactId}/summary"))).GetProperty("contact").GetProperty("jobTitle").GetString().ShouldBe("Buyer");

        var accounts = await Ok(await s.Client.PostAsync("/api/v1/accounts/query", Json("""{"search":"Acme","page":1,"pageSize":30}""")));
        accounts.GetProperty("items")[0].GetProperty("contactCount").GetInt32().ShouldBe(1);
        (await Ok(await s.Client.GetAsync($"/api/v1/accounts/{accountId}/summary"))).GetProperty("account").GetProperty("name").GetString().ShouldBe("Acme Ltd");

        // Pipelines (for the stage picker) and a deal.
        (await s.Client.PostAsync("/api/v1/onboarding/industry", Json("""{"industryKey":"general"}"""))).IsSuccessStatusCode.ShouldBeTrue();
        var pipelines = await Ok(await s.Client.GetAsync("/api/v1/settings/pipelines"));
        var pipeline = pipelines.EnumerateArray().First(p => p.GetProperty("isDefault").GetBoolean());
        var stages = pipeline.GetProperty("stages").EnumerateArray().Where(x => x.GetProperty("stageType").GetString() == "Open").OrderBy(x => x.GetProperty("sortOrder").GetInt32()).ToList();
        stages.Count.ShouldBeGreaterThan(1);
        var deal = await Ok(await s.Client.PostAsync("/api/v1/deals", Json(
            $$"""{"name":"Acme rollout","pipelineId":"{{pipeline.GetProperty("id").GetString()}}","stageId":"{{stages[0].GetProperty("id").GetString()}}","amount":12000,"currency":"AED","accountId":"{{accountId}}"}""")));
        var dealId = deal.GetProperty("id").GetString();

        var deals = await Ok(await s.Client.PostAsync("/api/v1/deals/query", Json("""{"search":"Acme","page":1,"pageSize":30}""")));
        deals.GetProperty("items")[0].GetProperty("stageName").GetString().ShouldNotBeNullOrEmpty();

        var moved = await s.Client.PatchAsync($"/api/v1/deals/{dealId}/stage", Json($$"""{"stageId":"{{stages[1].GetProperty("id").GetString()}}"}"""));
        (await Ok(moved)).GetProperty("stageId").GetString().ShouldBe(stages[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Lead_export_import_duplicates_merge_and_convert_work_as_the_app_calls_them()
    {
        var s = await Workspaces.SignupAsync(factory);

        // Import a file (multipart, as the file picker sends it), then export it back.
        var csv = "First name,Last name,Company,Address,Email,Phone\nAnna,Twin,Twin Co,1 Main Street,anna@twin.test,+971500000021\n";
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "leads.csv" } };
        var imported = await Ok(await s.Client.PostAsync("/api/v1/leads/import", form));
        imported.GetProperty("created").GetInt32().ShouldBe(1);

        var export = await s.Client.PostAsync("/api/v1/leads/export?format=csv", Json("""{"sortField":"created_at","sortDir":"desc","page":1,"pageSize":5000}"""));
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await export.Content.ReadAsStringAsync()).ShouldContain("Anna");
        (await s.Client.GetAsync("/api/v1/leads/template?format=csv")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A second lead with the same email is a duplicate of the first.
        var first = (await Ok(await s.Client.PostAsync("/api/v1/leads/query", Json("""{"search":"Anna","page":1,"pageSize":5}""")))).GetProperty("items")[0].GetProperty("id").GetString();
        var twin = await Ok(await s.Client.PostAsync("/api/v1/leads", Json("""{"firstName":"Anna","lastName":"Twin","email":"anna@twin.test","companyName":"Twin Co","status":"new"}""")));
        var check = await Ok(await s.Client.PostAsync("/api/v1/leads/check-duplicates", Json("""{"firstName":"Anna","lastName":"Twin","email":"anna@twin.test","phone":null,"companyName":"Twin Co"}""")));
        check.GetArrayLength().ShouldBeGreaterThan(0);

        var duplicates = await Ok(await s.Client.GetAsync($"/api/v1/leads/{twin.GetProperty("id").GetString()}/duplicates"));
        duplicates.EnumerateArray().Any(d => d.GetProperty("id").GetString() == first).ShouldBeTrue();
        (await s.Client.PostAsync($"/api/v1/leads/{twin.GetProperty("id").GetString()}/merge", Json($$"""{"duplicateIds":["{{first}}"],"fieldOverrides":null}"""))).IsSuccessStatusCode.ShouldBeTrue();

        // Convert options and a full convert with a new account and contact.
        var options = await Ok(await s.Client.GetAsync($"/api/v1/leads/{twin.GetProperty("id").GetString()}/convert-options"));
        options.GetProperty("leadName").GetString().ShouldNotBeNullOrEmpty();
        var converted = await Ok(await s.Client.PostAsync($"/api/v1/leads/{twin.GetProperty("id").GetString()}/convert", Json(
            """{"accountMode":"create","existingAccountId":null,"accountName":"Twin Co","contactMode":"create","existingContactId":null,"createDeal":false,"dealName":null,"pipelineId":null,"stageId":null,"amount":null,"currency":null,"expectedCloseDate":null}""")));
        converted.GetProperty("contactId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Calendar_and_my_day_use_the_query_and_my_day_endpoints()
    {
        var s = await Workspaces.SignupAsync(factory);
        var due = DateTime.UtcNow.AddHours(3).ToString("o");
        (await s.Client.PostAsync("/api/v1/activities", Json($$"""{"type":"Task","subject":"Send quote","dueAt":"{{due}}","priority":"Normal"}"""))).IsSuccessStatusCode.ShouldBeTrue();

        var myDay = await Ok(await s.Client.GetAsync("/api/v1/activities/my-day"));
        myDay.TryGetProperty("items", out _).ShouldBeTrue();
        (myDay.GetProperty("today").GetInt32() + myDay.GetProperty("upcoming").GetInt32()).ShouldBeGreaterThan(0);

        var page = await Ok(await s.Client.PostAsync("/api/v1/activities/query", Json("""{"tab":"all","mineOnly":true,"page":1,"pageSize":100}""")));
        page.GetProperty("items").EnumerateArray().Any(a => a.GetProperty("subject").GetString() == "Send quote").ShouldBeTrue();
    }

    [Fact]
    public async Task Documents_upload_list_and_download_on_a_lead()
    {
        var s = await Workspaces.SignupAsync(factory);
        var lead = await Ok(await s.Client.PostAsync("/api/v1/leads", Json("""{"firstName":"Doc","lastName":"Holder","status":"new"}""")));
        var leadId = lead.GetProperty("id").GetString()!;

        var form = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes("hello brochure");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "brochure.txt");
        form.Add(new StringContent("leads"), "entityType");
        form.Add(new StringContent(leadId), "entityId");
        var uploaded = await Ok(await s.Client.PostAsync("/api/v1/files", form));

        var list = await Ok(await s.Client.GetAsync($"/api/v1/files?entityType=leads&entityId={leadId}"));
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("fileName").GetString().ShouldBe("brochure.txt");

        var download = await s.Client.GetAsync($"/api/v1/files/{uploaded.GetProperty("id").GetString()}");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
    }

    [Fact]
    public async Task Avatar_is_uploaded_served_to_colleagues_and_removed()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "snap");
        var staffId = (await staff.Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("userId").GetString();

        // No photo yet.
        (await admin.Client.GetAsync($"/api/v1/avatars/{staffId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var form = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "me.png" } };
        (await staff.Client.PutAsync("/api/v1/avatars/me", form)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var seen = await admin.Client.GetAsync($"/api/v1/avatars/{staffId}");
        seen.StatusCode.ShouldBe(HttpStatusCode.OK);
        seen.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        // Something that is not an image is refused; another company cannot see it.
        var bad = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("<html>")), "file", "x.png" } };
        (await staff.Client.PutAsync("/api/v1/avatars/me", bad)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var outsider = await Workspaces.SignupAsync(factory);
        (await outsider.Client.GetAsync($"/api/v1/avatars/{staffId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await staff.Client.DeleteAsync("/api/v1/avatars/me")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.Client.GetAsync($"/api/v1/avatars/{staffId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Quiet_hours_are_saved_per_device_and_validated()
    {
        var s = await Workspaces.SignupAsync(factory);
        (await s.Client.PutAsync("/api/v1/mobile/devices/current", Json(
            """{"installationId":"quiet-1","platform":"android","environment":"production","deviceToken":"tok","provider":"fcm","appVersion":"1.0","timeZoneId":"UTC"}"""))).IsSuccessStatusCode.ShouldBeTrue();

        (await s.Client.PutAsync("/api/v1/mobile/devices/current/quiet-hours", Json("""{"installationId":"quiet-1","startMinute":1320,"endMinute":420}"""))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var list = await Ok(await s.Client.GetAsync("/api/v1/mobile/devices"));
        list[0].GetProperty("quietStartMinute").GetInt32().ShouldBe(1320);
        list[0].GetProperty("quietEndMinute").GetInt32().ShouldBe(420);

        (await s.Client.PutAsync("/api/v1/mobile/devices/current/quiet-hours", Json("""{"installationId":"quiet-1","startMinute":1320,"endMinute":null}"""))).IsSuccessStatusCode.ShouldBeFalse();
        (await s.Client.PutAsync("/api/v1/mobile/devices/current/quiet-hours", Json("""{"installationId":"quiet-1","startMinute":null,"endMinute":null}"""))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(1320, 420, 23, 0, true)]   // 22:00-07:00 at 23:00
    [InlineData(1320, 420, 3, 0, true)]    // ... at 03:00
    [InlineData(1320, 420, 12, 0, false)]  // ... at noon
    [InlineData(540, 1020, 10, 0, true)]   // 09:00-17:00 at 10:00
    [InlineData(540, 1020, 18, 0, false)]
    public void Quiet_window_handles_overnight_and_daytime_ranges(int start, int end, int hour, int minute, bool quiet)
    {
        var device = new MindLift.Domain.Entities.MobileDevice { QuietStartMinute = start, QuietEndMinute = end, TimeZoneId = "UTC" };
        MindLift.Infrastructure.Mobile.FcmPushNotificationSender.IsQuiet(device, new DateTime(2026, 11, 9, hour, minute, 0, DateTimeKind.Utc)).ShouldBe(quiet);
    }
}
