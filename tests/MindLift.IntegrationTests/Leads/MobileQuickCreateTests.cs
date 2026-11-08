using System.Net;
using System.Net.Http.Json;
using System.Text;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Leads;

/// <summary>The exact JSON the mobile Quick Create sheet sends for a lead, task, call and note.</summary>
public sealed class MobileQuickCreateTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Theory]
    [InlineData("Task")]
    [InlineData("Call")]
    [InlineData("Note")]
    public async Task Quick_create_activity_payload_is_accepted(string type)
    {
        var s = await Workspaces.SignupAsync(factory);
        var due = DateTime.UtcNow.AddHours(2).ToString("o");
        var res = await s.Client.PostAsync("/api/v1/activities", Json(
            $$"""{"type":"{{type}}","subject":"Testing","description":"Testing the new features","dueAt":"{{due}}","priority":"Normal"}"""));
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Calling_a_lead_logs_a_call_with_its_outcome()
    {
        var s = await Workspaces.SignupAsync(factory);
        var leadRes = await s.Client.PostAsync("/api/v1/leads", Json("""{"firstName":"Zed","lastName":"Caller","phone":"+971500000001","status":"new"}"""));
        var lead = System.Text.Json.JsonDocument.Parse(await leadRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
        var due = DateTime.UtcNow.ToString("o");
        var created = await s.Client.PostAsync("/api/v1/activities", Json(
            $$"""{"type":"Call","subject":"Call with Zed Caller","leadId":"{{lead}}","dueAt":"{{due}}","priority":"Normal","direction":"Outbound","callOutcome":"connected"}"""));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("callOutcome").GetString().ShouldBe("connected");
    }

    [Fact]
    public async Task Saved_view_made_on_mobile_can_be_listed_edited_queried_and_deleted()
    {
        var s = await Workspaces.SignupAsync(factory);
        await s.Client.PostAsync("/api/v1/leads", Json("""{"firstName":"Hot","lastName":"One","status":"qualified"}"""));
        await s.Client.PostAsync("/api/v1/leads", Json("""{"firstName":"Cold","lastName":"One","status":"new"}"""));

        const string filter = """{"logic":"and","conditions":[{"field":"owner_id","op":0,"value":"@me"},{"field":"status","op":12,"value":["qualified"]}],"groups":[]}""";
        var created = await s.Client.PostAsync("/api/v1/views", Json(
            $$"""{"entityType":"leads","name":"Hot leads","isShared":false,"isDefault":false,"filter":{{filter}},"columns":[],"sortField":"created_at","sortDir":"desc"}"""));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var id = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var list = System.Text.Json.JsonDocument.Parse(await s.Client.GetStringAsync("/api/v1/views/leads")).RootElement;
        list.EnumerateArray().Any(v => v.GetProperty("name").GetString() == "Hot leads").ShouldBeTrue();

        // The view's own filter returns only what it should.
        var page = await s.Client.PostAsync("/api/v1/leads/query", Json($$"""{"filter":{{filter}},"page":1,"pageSize":30}"""));
        System.Text.Json.JsonDocument.Parse(await page.Content.ReadAsStringAsync()).RootElement.GetProperty("total").GetInt32().ShouldBe(1);

        var renamed = await s.Client.PutAsync($"/api/v1/views/{id}", Json(
            $$"""{"entityType":"leads","name":"Hottest","isShared":false,"isDefault":false,"filter":{{filter}},"columns":[],"sortField":"created_at","sortDir":"asc"}"""));
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK, await renamed.Content.ReadAsStringAsync());

        (await s.Client.DeleteAsync($"/api/v1/views/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = System.Text.Json.JsonDocument.Parse(await s.Client.GetStringAsync("/api/v1/views/leads")).RootElement;
        after.EnumerateArray().Any(v => v.GetProperty("name").GetString() == "Hottest").ShouldBeFalse();
    }

    [Fact]
    public async Task Quick_create_lead_payload_is_accepted()
    {
        var s = await Workspaces.SignupAsync(factory);
        var res = await s.Client.PostAsync("/api/v1/leads", Json(
            """{"firstName":"Acme","lastName":"","name":"Acme","fullName":"Acme","companyName":"Acme","company":"Acme","phone":"+971501234567","source":"Mobile Quick Add","status":"new"}"""));
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }
}
