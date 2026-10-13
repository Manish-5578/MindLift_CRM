using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Leads;

/// <summary>Plan 7.3 web forms: the public endpoint is anonymous, so it has to be strict about what it accepts and what it stores.</summary>
public sealed class WebFormTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static readonly string[] AllFields = ["first_name", "last_name", "email", "phone", "company_name", "description"];

    private static async Task<WebFormDto> FormAsync(Session admin, string[]? fields = null, bool active = true, string source = "website")
    {
        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/web-forms",
            new SaveWebFormRequest("Contact us", source, "Thanks, we will call you.", active, fields ?? AllFields));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WebFormDto>())!;
    }

    private static string NewEmail() => $"visitor-{Guid.NewGuid():N}@example.test";

    private HttpResponseMessage? _last;

    private async Task<PublicFormResult> SubmitOkAsync(string token, PublicFormSubmission submission)
    {
        _last = await Workspaces.MobileClient(factory).PostAsJsonAsync($"/api/v1/public/forms/{token}", submission);
        _last.StatusCode.ShouldBe(HttpStatusCode.OK, await _last.Content.ReadAsStringAsync());
        return (await _last.Content.ReadFromJsonAsync<PublicFormResult>())!;
    }

    private static async Task<List<LeadListItemDto>> FindAsync(Session admin, string search)
    {
        var page = await (await admin.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery(search, null, null, null, 1, 50))).Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        return page!.Items.ToList();
    }

    [Fact]
    public async Task A_submission_creates_a_lead_with_the_forms_source_and_returns_its_message()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin, source: "campaign");
        var email = NewEmail();

        var result = await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "Visitor", email, "+971 50 555 0100", "Visitor Co", "Interested in a demo", null));

        result.Message.ShouldBe("Thanks, we will call you.");
        result.Duplicate.ShouldBeFalse();
        var lead = (await FindAsync(admin, email)).ShouldHaveSingleItem();
        lead.FullName.ShouldBe("Noor Visitor");
        lead.Source.ShouldBe("campaign");
        lead.Phone.ShouldBe("+971 50 555 0100");
        lead.CompanyName.ShouldBe("Visitor Co");
        lead.Status.ShouldBe("new");
    }

    [Fact]
    public async Task The_message_is_stored_as_the_leads_notes()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);
        var email = NewEmail();

        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, null, null, "Please call after 5pm", null));

        var lead = (await FindAsync(admin, email)).ShouldHaveSingleItem();
        var full = await admin.Client.GetFromJsonAsync<LeadDto>($"/api/v1/leads/{lead.Id}");
        full!.Description.ShouldBe("Please call after 5pm");
    }

    [Fact]
    public async Task Fields_the_form_does_not_collect_are_ignored_even_if_the_visitor_sends_them()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin, fields: ["first_name", "email"]);
        var email = NewEmail();

        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, "+971505550111", "Sneaky Co", "Sneaky notes", null));

        var lead = (await FindAsync(admin, email)).ShouldHaveSingleItem();
        lead.Phone.ShouldBeNull();
        lead.CompanyName.ShouldBeNull();
        (await admin.Client.GetFromJsonAsync<LeadDto>($"/api/v1/leads/{lead.Id}"))!.Description.ShouldBeNull();
    }

    [Fact]
    public async Task A_filled_in_honeypot_looks_like_success_but_stores_nothing()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);
        var email = NewEmail();

        var result = await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Bot", "Bot", email, null, null, null, "http://spam.example"));

        result.Message.ShouldBe("Thanks, we will call you.");
        (await FindAsync(admin, email)).ShouldBeEmpty();
        (await admin.Client.GetFromJsonAsync<List<WebFormDto>>("/api/v1/settings/web-forms"))!.Single().SubmissionCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_visitor_who_submits_again_is_flagged_and_only_fills_blank_fields()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);
        var email = NewEmail();
        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, null, null, null, null));

        var second = await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Different", "Name", email.ToUpperInvariant() + " ", "+971505550222", "Late Co", null, null));

        second.Duplicate.ShouldBeTrue();
        var lead = (await FindAsync(admin, email)).ShouldHaveSingleItem();
        lead.FullName.ShouldBe("Noor V");
        lead.Phone.ShouldBe("+971505550222");
        lead.CompanyName.ShouldBe("Late Co");
    }

    [Fact]
    public async Task A_resubmission_never_overwrites_what_the_lead_already_has()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);
        var email = NewEmail();
        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, "+971505550333", "First Co", null, null));

        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, "+971505559999", "Second Co", null, null));

        var lead = (await FindAsync(admin, email)).ShouldHaveSingleItem();
        lead.Phone.ShouldBe("+971505550333");
        lead.CompanyName.ShouldBe("First Co");
    }

    [Fact]
    public async Task The_submission_count_includes_new_leads_and_repeat_visitors_but_not_bots()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);
        var email = NewEmail();

        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, null, null, null, null));
        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, null, null, null, null));
        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Bot", "B", NewEmail(), null, null, null, "spam"));

        (await admin.Client.GetFromJsonAsync<List<WebFormDto>>("/api/v1/settings/web-forms"))!.Single().SubmissionCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_form_that_is_switched_off_looks_like_a_form_that_does_not_exist()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin, active: false);

        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}",
            new PublicFormSubmission("Noor", "V", NewEmail(), null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
    {
        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync("/api/v1/public/forms/not-a-real-token",
            new PublicFormSubmission("Noor", "V", NewEmail(), null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_submission_without_a_first_name_is_refused_with_a_field_error()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);

        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}",
            new PublicFormSubmission("  ", "V", NewEmail(), null, null, null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_absurdly_long_value_is_refused_instead_of_stored()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(admin);

        var response = await Workspaces.MobileClient(factory).PostAsJsonAsync($"/api/v1/public/forms/{form.PublicToken}",
            new PublicFormSubmission(new string('A', 100_000), "V", NewEmail(), null, null, new string('B', 1_000_000), null));

        ((int)response.StatusCode).ShouldBeInRange(400, 499, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_lead_from_one_workspaces_form_never_appears_in_another()
    {
        var one = await Workspaces.SignupAsync(factory);
        var two = await Workspaces.SignupAsync(factory);
        var form = await FormAsync(one);
        var email = NewEmail();

        await SubmitOkAsync(form.PublicToken, new PublicFormSubmission("Noor", "V", email, null, null, null, null));

        (await FindAsync(one, email)).Count.ShouldBe(1);
        (await FindAsync(two, email)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Staff_cannot_create_or_change_forms()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "form-staff");
        var form = await FormAsync(admin);
        var body = new SaveWebFormRequest("Hijack", "website", "x", true, AllFields);

        (await staff.Client.PostAsJsonAsync("/api/v1/settings/web-forms", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.PutAsJsonAsync($"/api/v1/settings/web-forms/{form.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_form_must_collect_a_first_name_and_only_known_fields()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var noName = await admin.Client.PostAsJsonAsync("/api/v1/settings/web-forms", new SaveWebFormRequest("F", "website", "x", true, ["email"]));
        var unknown = await admin.Client.PostAsJsonAsync("/api/v1/settings/web-forms", new SaveWebFormRequest("F", "website", "x", true, ["first_name", "password"]));

        noName.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Each_form_gets_its_own_unguessable_token()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var a = await FormAsync(admin);
        var b = await FormAsync(admin);

        a.PublicToken.ShouldNotBe(b.PublicToken);
        a.PublicToken.Length.ShouldBeGreaterThanOrEqualTo(20);
        a.PublicToken.ShouldAllBe(c => char.IsLetterOrDigit(c));
    }
}
