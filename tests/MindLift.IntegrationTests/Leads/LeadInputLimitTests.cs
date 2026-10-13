using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>No lead input may be unbounded, and too much is a field error (400), never a server error (500).</summary>
public sealed class LeadInputLimitTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest Lead(string first = "Ana", string? email = null, string? phone = null, string? company = null,
        string? description = null, IReadOnlyList<string>? tags = null)
        => new(first, "Tester", email, phone, company, "new", "website", "warm", description, tags, null, null);

    private static async Task<HashSet<string>> FieldsInErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToHashSet()
            : [];
    }

    [Theory]
    [InlineData("firstName", 101)]
    [InlineData("email", 321)]
    [InlineData("phone", 41)]
    [InlineData("company", 201)]
    [InlineData("description", 5001)]
    public async Task A_value_over_its_limit_is_refused_with_an_error_on_that_field(string field, int length)
    {
        var owner = await Workspaces.SignupAsync(factory);
        var text = new string('x', length);
        var request = field switch
        {
            "firstName" => Lead(first: text),
            "email" => Lead(email: text),
            "phone" => Lead(phone: text),
            "company" => Lead(company: text),
            _ => Lead(description: text),
        };

        var response = await owner.Client.PostAsJsonAsync("/api/v1/leads", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var inError = await FieldsInErrorAsync(response);
        inError.ShouldContain(f => f.Contains(field == "company" ? "company" : field.ToLowerInvariant()), $"fields in error: {string.Join(", ", inError)}");
    }

    [Fact]
    public async Task A_value_exactly_at_its_limit_is_accepted()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var request = Lead(first: new string('a', 100), email: new string('e', 300) + "@x.test", phone: new string('1', 40), company: new string('c', 200), description: new string('d', 5000));

        var response = await owner.Client.PostAsJsonAsync("/api/v1/leads", request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Too_many_or_too_long_tags_are_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var many = await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead(tags: Enumerable.Range(0, 21).Select(i => "t" + i).ToList()));
        var long1 = await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead(tags: [new string('t', 41)]));
        var fine = await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead(tags: Enumerable.Range(0, 20).Select(i => "t" + i).ToList()));

        many.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        long1.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fine.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Updating_a_lead_has_the_same_limits()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var created = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead())).Content.ReadFromJsonAsync<LeadDto>();

        var response = await owner.Client.PutAsJsonAsync($"/api/v1/leads/{created!.Id}", Lead(description: new string('x', 5001)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_long_disqualify_reason_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var created = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead())).Content.ReadFromJsonAsync<LeadDto>();

        var response = await owner.Client.PostAsJsonAsync($"/api/v1/leads/{created!.Id}/disqualify", new DisqualifyLeadRequest(new string('r', 501), null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_public_form_refuses_oversized_input_on_every_field()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var form = await (await owner.Client.PostAsJsonAsync("/api/v1/settings/web-forms",
            new SaveWebFormRequest("Contact", "website", "Thanks", true, ["first_name", "last_name", "email", "phone", "company_name", "description"]))).Content.ReadFromJsonAsync<WebFormDto>();
        var anonymous = Workspaces.MobileClient(factory);
        var url = $"/api/v1/public/forms/{form!.PublicToken}";
        var big = (int length) => new string('z', length);

        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission(big(101), "V", null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", big(101), null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", "V", big(321), null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", "V", null, big(41), null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", "V", null, null, big(201), null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", "V", null, null, null, big(5001), null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await anonymous.PostAsJsonAsync(url, new PublicFormSubmission("N", "V", null, null, null, null, big(201)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rule_and_form_settings_have_sensible_size_limits()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.UserId;

        (await owner.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules", new SaveAssignmentRuleRequest(new string('n', 101), true, 1, "SpecificUser", [me], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules", new SaveAssignmentRuleRequest("", true, 1, "SpecificUser", [me], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Client.PostAsJsonAsync("/api/v1/settings/web-forms", new SaveWebFormRequest(new string('n', 101), "website", "x", true, ["first_name"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Client.PostAsJsonAsync("/api/v1/settings/web-forms", new SaveWebFormRequest("F", "website", new string('m', 501), true, ["first_name"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Merging_needs_at_least_one_lead_and_not_too_many()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var created = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead())).Content.ReadFromJsonAsync<LeadDto>();

        (await owner.Client.PostAsJsonAsync($"/api/v1/leads/{created!.Id}/merge", new MergeLeadsRequest([]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Client.PostAsJsonAsync($"/api/v1/leads/{created.Id}/merge", new MergeLeadsRequest(Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
