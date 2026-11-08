using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Leads;

/// <summary>The exact JSON the mobile leads workspace sends (views, search, status, notes, bulk, change view), against the real API.</summary>
public sealed class MobileLeadsWorkspaceTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.ShouldBeTrue(text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> CreateLeadAsync(Session s, string first, string company)
    {
        var res = await s.Client.PostAsync("/api/v1/leads", Json(
            $$"""{"firstName":"{{first}}","lastName":"Tester","email":null,"phone":"+971500000000","companyName":"{{company}}","status":"new","source":null,"rating":null,"description":null,"tags":[],"ownerId":null,"customFields":{},"address":null,"city":null,"state":null}"""));
        return (await ReadAsync(res)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> QueryAsync(Session s, string body)
        => await ReadAsync(await s.Client.PostAsync("/api/v1/leads/query", Json(body)));

    // Filters exactly as LeadView.builtIn() builds them (operator codes: Eq=0, In=12, NotIn=13).
    private const string AllOpen = """{"logic":"and","conditions":[{"field":"status","op":13,"value":["disqualified"]}],"groups":[]}""";
    private const string Mine = """{"logic":"and","conditions":[{"field":"owner_id","op":0,"value":"@me"},{"field":"status","op":12,"value":["new","working","qualified"]}],"groups":[]}""";
    private const string NotInterested = """{"logic":"and","conditions":[{"field":"status","op":0,"value":"disqualified"}],"groups":[]}""";

    [Fact]
    public async Task Views_search_status_notes_bulk_and_change_view_work_with_the_mobile_payloads()
    {
        var s = await Workspaces.SignupAsync(factory);
        s.Client.DefaultRequestHeaders.Add("X-Client", "mobile");
        var a = await CreateLeadAsync(s, "Alpha", "Alpha Trading");
        var b = await CreateLeadAsync(s, "Bravo", "Bravo Logistics");

        // Bootstrap carries the picklists and permissions the screen reads.
        var boot = await ReadAsync(await s.Client.GetAsync("/api/v1/app/bootstrap"));
        boot.GetProperty("picklists").EnumerateArray().Any(p => p.GetProperty("key").GetString() == "lead_status").ShouldBeTrue();
        boot.GetProperty("permissions").EnumerateArray().Any(p => p.GetProperty("module").GetString() == "Leads").ShouldBeTrue();

        // Built-in views.
        (await QueryAsync(s, $$"""{"filter":{{Mine}},"sortField":"created_at","sortDir":"desc","page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(2);
        (await QueryAsync(s, $$"""{"filter":{{AllOpen}},"sortField":"created_at","sortDir":"desc","page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(2);

        // Search.
        var found = await QueryAsync(s, $$"""{"search":"Bravo","filter":{{AllOpen}},"sortField":"created_at","sortDir":"desc","page":1,"pageSize":30}""");
        found.GetProperty("total").GetInt32().ShouldBe(1);
        found.GetProperty("items")[0].GetProperty("companyName").GetString().ShouldBe("Bravo Logistics");

        // Change status (POST, lower-case code).
        (await s.Client.PostAsync($"/api/v1/leads/{a}/status", Json("""{"status":"qualified"}"""))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var qualified = await QueryAsync(s, """{"filter":{"logic":"and","conditions":[{"field":"status","op":0,"value":"qualified"}],"groups":[]},"page":1,"pageSize":30}""");
        qualified.GetProperty("total").GetInt32().ShouldBe(1);

        // Disqualify with a reason: it shows under Not interested and leaves All open.
        (await s.Client.PostAsync($"/api/v1/leads/{b}/disqualify", Json("""{"reason":"Not interested","notes":null}"""))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await QueryAsync(s, $$"""{"filter":{{NotInterested}},"page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(1);
        (await QueryAsync(s, $$"""{"filter":{{AllOpen}},"page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(1);

        // Quick note (sticky note) shows in the list as the description.
        (await s.Client.PatchAsync($"/api/v1/leads/{a}/sticky-note", Json("""{"note":"Call back Monday"}"""))).StatusCode.ShouldBe(HttpStatusCode.OK);
        var withNote = await QueryAsync(s, """{"search":"Alpha","page":1,"pageSize":30}""");
        withNote.GetProperty("items")[0].GetProperty("description").GetString().ShouldBe("Call back Monday");

        // Notes are written and read as "content".
        var note = await ReadAsync(await s.Client.PostAsync($"/api/v1/leads/{a}/notes", Json("""{"content":"Spoke to the buyer"}""")));
        note.GetProperty("content").GetString().ShouldBe("Spoke to the buyer");
        var notes = await ReadAsync(await s.Client.GetAsync($"/api/v1/leads/{a}/notes"));
        // The quick note above is kept as a note too, so there are two.
        notes.EnumerateArray().Any(n => n.GetProperty("content").GetString() == "Spoke to the buyer").ShouldBeTrue();
        // Notes can be edited but never deleted.
        var noteId = note.GetProperty("id").GetString();
        var edited = await ReadAsync(await s.Client.PutAsync($"/api/v1/leads/{a}/notes/{noteId}", Json("""{"content":"Spoke to the buyer, wants a quote"}""")));
        edited.GetProperty("content").GetString().ShouldBe("Spoke to the buyer, wants a quote");
        (await s.Client.DeleteAsync($"/api/v1/leads/{a}/notes/{noteId}")).IsSuccessStatusCode.ShouldBeFalse();
        (await ReadAsync(await s.Client.GetAsync($"/api/v1/leads/{a}/notes")))
            .EnumerateArray().Any(n => n.GetProperty("content").GetString() == "Spoke to the buyer, wants a quote").ShouldBeTrue();

        // Everything done from the app is on the audit trail, tagged as mobile.
        var trail = await ReadAsync(await s.Client.GetAsync("/api/v1/audit-logs?pageSize=100"));
        trail.GetProperty("items").EnumerateArray().Count(i => (i.GetProperty("summary").GetString() ?? "").StartsWith("[Mobile]")).ShouldBeGreaterThan(3);

        // Edit: the full lead is read, changed and PUT back.
        var raw = await ReadAsync(await s.Client.GetAsync($"/api/v1/leads/{a}"));
        raw.GetProperty("status").GetString().ShouldBe("qualified");
        var put = await s.Client.PutAsync($"/api/v1/leads/{a}", Json(
            $$"""{"firstName":"Alpha","lastName":"Renamed","email":"a@example.test","phone":"+971500000000","companyName":"Alpha Trading","status":"qualified","source":null,"rating":null,"description":"Call back Monday","tags":[],"ownerId":"{{raw.GetProperty("ownerId").GetString()}}","customFields":{},"address":"Dubai","city":"Dubai","state":null}"""));
        (await ReadAsync(put)).GetProperty("lastName").GetString().ShouldBe("Renamed");

        // Bulk status and bulk delete.
        var c = await CreateLeadAsync(s, "Charlie", "Charlie Co");
        var d = await CreateLeadAsync(s, "Delta", "Delta Co");
        (await s.Client.PostAsync("/api/v1/leads/bulk", Json($$"""{"action":"status","ids":["{{c}}","{{d}}"],"status":"working"}"""))).IsSuccessStatusCode.ShouldBeTrue();
        (await QueryAsync(s, """{"filter":{"logic":"and","conditions":[{"field":"status","op":0,"value":"working"}],"groups":[]},"page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(2);
        (await s.Client.PostAsync("/api/v1/leads/bulk-delete", Json($$"""{"ids":["{{c}}","{{d}}"]}"""))).IsSuccessStatusCode.ShouldBeTrue();

        // Change view (move-record) with a built-in target.
        var move = await s.Client.PostAsync("/api/v1/views/move-record", Json(
            $$"""{"entityType":"leads","recordId":"{{a}}","targetViewKey":"new","sourceViewKey":"all","recordIds":["{{a}}"]}"""));
        move.IsSuccessStatusCode.ShouldBeTrue(await move.Content.ReadAsStringAsync());

        // Saved views list loads.
        (await s.Client.GetAsync("/api/v1/views/leads")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Quick_convert_to_engagement_explains_why_when_there_is_no_pipeline()
    {
        var s = await Workspaces.SignupAsync(factory);
        var id = await CreateLeadAsync(s, "Echo", "Echo Industries");
        var res = await s.Client.PostAsync($"/api/v1/leads/{id}/quick-convert-engagement", Json("{}"));
        // The app shows the server's own message; the lead stays open.
        res.StatusCode.ShouldBe((HttpStatusCode)422);
        (await res.Content.ReadAsStringAsync()).ShouldContain("pipeline");
        (await QueryAsync(s, $$"""{"filter":{{AllOpen}},"page":1,"pageSize":30}""")).GetProperty("total").GetInt32().ShouldBe(1);
    }
}
