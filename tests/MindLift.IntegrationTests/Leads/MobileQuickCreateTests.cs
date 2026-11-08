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
    public async Task Quick_create_lead_payload_is_accepted()
    {
        var s = await Workspaces.SignupAsync(factory);
        var res = await s.Client.PostAsync("/api/v1/leads", Json(
            """{"firstName":"Acme","lastName":"","name":"Acme","fullName":"Acme","companyName":"Acme","company":"Acme","phone":"+971501234567","source":"Mobile Quick Add","status":"new"}"""));
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }
}
