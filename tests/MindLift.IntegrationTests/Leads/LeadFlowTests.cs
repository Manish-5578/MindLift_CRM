using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>Plan 7.3 (M6): leads, filters, scope, conversion, duplicates, web forms, assignment, import and export.</summary>
public sealed class LeadFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest Lead(string first, string? email = null, string? phone = null, Guid? owner = null, string? company = null,
        IReadOnlyDictionary<string, JsonElement>? custom = null) =>
        new(first, "Tester", email, phone, company, "new", "website", "warm", null, null, owner, custom);

    private static ListQuery Query(FilterGroup? filter = null, string? search = null, int pageSize = 200) =>
        new(search, filter, null, null, 1, pageSize);

    private static FilterGroup Cond(string field, FilterOperator op, object value) => new()
    {
        Conditions = [new FilterCondition { Field = field, Op = op, Value = JsonSerializer.SerializeToElement(value) }],
    };

    [Fact]
    public async Task Leads_can_be_filtered_on_a_custom_field()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        await owner.Client.PostAsJsonAsync("/api/v1/settings/custom-fields", new SaveCustomFieldRequest(
            "leads", "cf_budget", "Budget", null, MindLift.Shared.Filtering.FieldType.Currency, false, true, true, null, 0, null));

        var big = Lead($"Big-{Guid.NewGuid():N}".Substring(0, 12), owner: me!.UserId) with
        {
            CustomFields = new Dictionary<string, JsonElement> { ["cf_budget"] = JsonSerializer.SerializeToElement(2_500_000) },
        };
        var small = Lead($"Small-{Guid.NewGuid():N}".Substring(0, 12), owner: me.UserId) with
        {
            CustomFields = new Dictionary<string, JsonElement> { ["cf_budget"] = JsonSerializer.SerializeToElement(90_000) },
        };
        (await owner.Client.PostAsJsonAsync("/api/v1/leads", big)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await owner.Client.PostAsJsonAsync("/api/v1/leads", small)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await owner.Client.PostAsJsonAsync("/api/v1/leads/query", Query(Cond("cf_budget", FilterOperator.Gt, 1_000_000)));
        var page = await result.Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();

        page!.Items.Select(i => i.FullName).ShouldContain(n => n.StartsWith(big.FirstName, StringComparison.Ordinal));
        page.Items.ShouldNotContain(i => i.FullName.Contains(small.FirstName));
    }

    [Fact]
    public async Task Staff_only_see_and_edit_their_own_leads()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "lead-owner");
        var staffMe = await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var adminMe = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        var mine = await (await staff.Client.PostAsJsonAsync("/api/v1/leads", Lead("Mine", owner: staffMe!.UserId))).Content.ReadFromJsonAsync<LeadDto>();
        var theirs = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", Lead("Theirs", owner: adminMe!.UserId))).Content.ReadFromJsonAsync<LeadDto>();

        var list = await (await staff.Client.PostAsJsonAsync("/api/v1/leads/query", Query())).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        list!.Items.Select(i => i.Id).ShouldContain(mine!.Id);
        list.Items.Select(i => i.Id).ShouldNotContain(theirs!.Id);

        (await staff.Client.GetAsync($"/api/v1/leads/{theirs!.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conversion_creates_contact_account_and_deal_and_locks_the_lead()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        var created = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("Convert", email: $"c-{Guid.NewGuid():N}@example.test", company: $"Co {Guid.NewGuid():N}"))).Content.ReadFromJsonAsync<LeadDto>();

        var convert = await owner.Client.PostAsJsonAsync($"/api/v1/leads/{created!.Id}/convert", new ConvertLeadRequest(
            "create", null, null, "create", null, true, "First deal", null, null, 250000m, "AED", null));

        convert.StatusCode.ShouldBe(HttpStatusCode.OK, await convert.Content.ReadAsStringAsync());
        var result = await convert.Content.ReadFromJsonAsync<ConvertLeadResult>();
        result!.AccountId.ShouldNotBeNull();
        result.DealId.ShouldNotBeNull();

        var after = await owner.Client.GetFromJsonAsync<LeadDto>($"/api/v1/leads/{created.Id}");
        after!.Status.ShouldBe("converted");
        after.ConvertedContactId.ShouldBe(result.ContactId);

        var edit = await owner.Client.PutAsJsonAsync($"/api/v1/leads/{created.Id}", Lead("Changed"));
        edit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Workspaces.ErrorCodeAsync(edit)).ShouldBe("lead_converted");

        var list = await (await owner.Client.PostAsJsonAsync("/api/v1/leads/query", Query())).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        list!.Items.Select(i => i.Id).ShouldNotContain(created.Id);
    }

    [Fact]
    public async Task Converting_with_a_company_name_that_exists_asks_to_link_it()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await owner.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"));
        var company = $"Dup Co {Guid.NewGuid():N}";
        var first = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("One", company: company))).Content.ReadFromJsonAsync<LeadDto>();
        await owner.Client.PostAsJsonAsync($"/api/v1/leads/{first!.Id}/convert", new ConvertLeadRequest("create", null, null, "create", null, false, null, null, null, null, null, null));

        var second = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("Two", company: company))).Content.ReadFromJsonAsync<LeadDto>();
        var refused = await owner.Client.PostAsJsonAsync($"/api/v1/leads/{second!.Id}/convert", new ConvertLeadRequest("create", null, null, "create", null, false, null, null, null, null, null, null));

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Workspaces.ErrorCodeAsync(refused)).ShouldBe("account_exists");
    }

    [Fact]
    public async Task Duplicate_check_finds_a_lead_with_the_same_email_or_phone()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("Original", email: email, phone: "+971 50 123 4567"));

        var matches = await (await owner.Client.PostAsJsonAsync("/api/v1/leads/check-duplicates",
            new CheckDuplicatesRequest("Someone", "Else", email.ToUpperInvariant(), "0501234567", null))).Content.ReadFromJsonAsync<List<DuplicateCandidateDto>>();

        matches!.Select(m => m.Reason).ShouldContain("Same email");
    }

    [Fact]
    public async Task Web_form_creates_a_lead_once_and_ignores_honeypot_submissions()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var form = await (await owner.Client.PostAsJsonAsync("/api/v1/settings/web-forms",
            new SaveWebFormRequest("Contact us", "website", "Thanks!", true, ["first_name", "email", "phone"]))).Content.ReadFromJsonAsync<WebFormDto>();
        var anonymous = Workspaces.MobileClient(factory);
        var email = $"web-{Guid.NewGuid():N}@example.test";

        var first = await anonymous.PostAsJsonAsync($"/api/v1/public/forms/{form!.PublicToken}", new PublicFormSubmission("Web", "Visitor", email, null, null, "Hello", null));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<PublicFormResult>())!.Duplicate.ShouldBeFalse();

        var again = await anonymous.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}", new PublicFormSubmission("Web", "Visitor", email, null, null, null, null));
        (await again.Content.ReadFromJsonAsync<PublicFormResult>())!.Duplicate.ShouldBeTrue();

        var bot = await anonymous.PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}", new PublicFormSubmission("Bot", "Spam", "bot@example.test", null, null, null, "filled"));
        bot.StatusCode.ShouldBe(HttpStatusCode.OK);

        var list = await (await owner.Client.PostAsJsonAsync("/api/v1/leads/query", Query(search: email))).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        list!.Total.ShouldBe(1);
        var none = await (await owner.Client.PostAsJsonAsync("/api/v1/leads/query", Query(search: "bot@example.test"))).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        none!.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Round_robin_assignment_alternates_between_the_listed_users()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var a = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rr-a");
        var b = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "rr-b");
        var aMe = await a.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        var bMe = await b.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");

        var rule = await admin.Client.PostAsJsonAsync("/api/v1/settings/assignment-rules",
            new SaveAssignmentRuleRequest("Round robin", true, 1, "RoundRobin", [aMe!.UserId, bMe!.UserId], null));
        rule.StatusCode.ShouldBe(HttpStatusCode.OK, await rule.Content.ReadAsStringAsync());

        var owners = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", Lead($"RR{i}-{Guid.NewGuid():N}".Substring(0, 10)))).Content.ReadFromJsonAsync<LeadDto>();
            owners.Add(lead!.OwnerId);
        }

        owners.ShouldBe([aMe.UserId, bMe.UserId, aMe.UserId]);
    }

    [Fact]
    public async Task Disqualifying_needs_a_reason_and_keeps_the_lead_visible()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var lead = await (await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("Disq"))).Content.ReadFromJsonAsync<LeadDto>();

        var missing = await owner.Client.PostAsJsonAsync($"/api/v1/leads/{lead!.Id}/disqualify", new DisqualifyLeadRequest(" ", null));
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var done = await owner.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/disqualify", new DisqualifyLeadRequest("No budget", "Call back next year"));
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await done.Content.ReadFromJsonAsync<LeadDto>())!.DisqualifyReason.ShouldContain("No budget");
    }

    [Fact]
    public async Task Csv_import_skips_duplicates_and_export_returns_the_list()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var existing = $"imp-{Guid.NewGuid():N}@example.test";
        await owner.Client.PostAsJsonAsync("/api/v1/leads", Lead("Existing", email: existing));

        var csv = $"First name,Last name,Email,Phone,Company\nAna,Q,ana-{Guid.NewGuid():N}@example.test,,Acme\n\"Ben, Jr\",Z,{existing},,\n,Missing,,,\n";
        var imported = await owner.Client.PostAsync("/api/v1/leads/import", new StringContent(csv, System.Text.Encoding.UTF8, "text/csv"));

        imported.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await imported.Content.ReadFromJsonAsync<ImportResultDto>();
        result!.Total.ShouldBe(3);
        result.Created.ShouldBe(1);
        result.Skipped.ShouldBe(1);
        result.Failed.ShouldBe(1);

        var export = await owner.Client.PostAsJsonAsync("/api/v1/leads/export", Query());
        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await export.Content.ReadAsStringAsync()).ShouldContain("id,first_name,last_name");
    }

    [Fact]
    public async Task Staff_cannot_import_leads_by_default()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "no-import");

        var response = await staff.Client.PostAsync("/api/v1/leads/import", new StringContent("First name\nX", System.Text.Encoding.UTF8, "text/csv"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
