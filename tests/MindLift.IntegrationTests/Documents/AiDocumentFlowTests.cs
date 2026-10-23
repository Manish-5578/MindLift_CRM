using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Documents;

/// <summary>
/// The whole path from defining fields to a saved record, against the real API and database with a scripted model: an administrator defines and describes
/// fields, uploads a document, it is read in the background, every value is checked against the text, a person reviews and approves, and the values land in the
/// linked contact's own fields. Also every way it can go wrong: AI off, model down, a made-up value, a value that does not fit, a record that is not linked.
/// </summary>
public sealed class AiDocumentFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private const string Receipt =
        "Passport Seva Online Application Receipt\n"
        + "Application Reference No. 26-1060599710\n"
        + "Given Name CHITRA\n"
        + "Surname UDHAYAKUMAR\n"
        + "Gender FEMALE\n"
        + "Date Of Birth 25/04/1984\n"
        + "Appointment Date 24/08/2026\n";

    private const string ReceiptAnswer = """{"given_name":"CHITRA","surname":"UDHAYAKUMAR","date_of_birth":"25/04/1984","gender":"FEMALE","reference":"26-1060599710"}""";

    private static DocumentFieldRuleDto Field(string key, string label, string type, string? description = null, string? example = null, string? anchor = null,
        string? saveTo = null, string mode = DocumentFieldModes.Auto, IReadOnlyList<string>? allowed = null, bool required = false)
        => new(key, label, type, anchor, null, required, false, description, example, allowed, mode, saveTo);

    private static SaveDocumentTypeRequest ReceiptType(string key = "appointment", string? dobTarget = null) => new(
        key, "Appointment receipt", "An appointment confirmation slip", true, 2,
        [new("Application Reference", 2), new("Appointment", 1)],
        [
            Field("given_name", "Given name", DocumentValueTypes.Text, "The applicant's first name", "CHITRA", saveTo: "contacts.first_name", required: true),
            Field("surname", "Surname", DocumentValueTypes.Text, "The applicant's surname", saveTo: "contacts.last_name", required: true),
            Field("date_of_birth", "Date of birth", DocumentValueTypes.Date, "The applicant's date of birth", saveTo: dobTarget),
            Field("gender", "Gender", DocumentValueTypes.Text, "The applicant's gender", allowed: ["Male", "Female"]),
            Field("reference", "Reference", DocumentValueTypes.Id, "The application reference number", anchor: "Application Reference No."),
        ]);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static MultipartFormDataContent FileForm(string text, string name, string? category = null, Guid? contactId = null, Guid? accountId = null, Guid? dealId = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", name);
        if (category is not null) form.Add(new StringContent(category), "category");
        if (contactId is { } c) form.Add(new StringContent(c.ToString()), "contactId");
        if (accountId is { } a) form.Add(new StringContent(a.ToString()), "accountId");
        if (dealId is { } d) form.Add(new StringContent(d.ToString()), "dealId");
        return form;
    }

    private async Task<Session> AdminAsync(bool aiOn = true)
    {
        factory.Llm.Reset();
        var admin = await Workspaces.SignupAsync(factory);
        if (aiOn)
        {
            await SendAsync<DocumentAiStatusDto>(await admin.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true)));
        }

        return admin;
    }

    private static async Task<DocumentTypeDto> CreateTypeAsync(Session s, SaveDocumentTypeRequest request)
        => await SendAsync<DocumentTypeDto>(await s.Client.PostAsJsonAsync("/api/v1/settings/document-types", request));

    private static async Task<ContactDto> CreateContactAsync(Session s, string first = "Pending", string last = "Applicant")
        => await SendAsync<ContactDto>(await s.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest(first, last, null, null, null, null, null, null, null, null)));

    private static async Task<BusinessDocumentDto> UploadAsync(Session s, string text, string? category = null, Guid? contactId = null, Guid? accountId = null, Guid? dealId = null)
        => await SendAsync<BusinessDocumentDto>(await s.Client.PostAsync("/api/v1/documents/upload", FileForm(text, "receipt.txt", category, contactId, accountId, dealId)), HttpStatusCode.Created);

    /// <summary>Queues the reading (the test host runs the job at once) and returns the document as it is afterwards.</summary>
    private static async Task<BusinessDocumentDto> ReadAsync(Session s, Guid id, string? type = null)
    {
        var queued = await SendAsync<BusinessDocumentDto>(await s.Client.PostAsJsonAsync($"/api/v1/documents/{id}/read", new ReadDocumentRequest(type)), HttpStatusCode.Accepted);
        queued.Status.ShouldBeOneOf("Queued", "NeedsReview", "Processing");
        return await GetAsync(s, id);
    }

    private static async Task<BusinessDocumentDto> GetAsync(Session s, Guid id)
        => (await s.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{id}"))!;

    private static OcrExtractedFieldDto Of(BusinessDocumentDto doc, string key) => doc.OcrResult!.ExtractedFields.Single(f => f.FieldName == key);

    // ---------- Defining fields ----------

    [Fact]
    public async Task An_administrator_adds_fields_with_descriptions_choices_and_a_record_to_save_to_and_they_are_kept()
    {
        var admin = await AdminAsync();

        var type = await CreateTypeAsync(admin, ReceiptType());

        var gender = type.Fields.Single(f => f.Key == "gender");
        gender.Description.ShouldBe("The applicant's gender");
        gender.AllowedValues.ShouldBe(["Male", "Female"]);
        gender.Mode.ShouldBe(DocumentFieldModes.Auto);
        type.Fields.Single(f => f.Key == "given_name").SaveTo.ShouldBe("contacts.first_name");
        type.Fields.Single(f => f.Key == "given_name").Example.ShouldBe("CHITRA");
    }

    [Fact]
    public async Task A_field_may_have_only_a_name_and_a_description_with_no_pattern_at_all()
    {
        var admin = await AdminAsync();

        var type = await CreateTypeAsync(admin, new("bare", "Bare type", null, true, 1, [new("x", 1)],
            [Field("anything", "Anything useful", DocumentValueTypes.Text, "Whatever the author wanted to note")]));

        type.Fields.Single().Anchor.ShouldBeNull();
        type.Fields.Single().Pattern.ShouldBeNull();
    }

    [Theory]
    [InlineData("Rules", null, null, "patterns only needs an anchor")]
    [InlineData("Sometimes", null, null, "Choose how the field is read")]
    [InlineData("Auto", "Wizard", null, "not a field a document value can be saved to")]
    [InlineData("Auto", "contacts.not_a_field", null, "not a field a document value can be saved to")]
    [InlineData("Auto", "contacts.email", "Amount", "cannot be saved to")]
    public async Task A_field_that_could_not_work_is_refused_with_the_reason(string mode, string? saveTo, string? type, string expected)
    {
        var admin = await AdminAsync();
        var field = Field("field_one", "A field", type ?? DocumentValueTypes.Text, saveTo: saveTo, mode: mode);

        var response = await admin.Client.PostAsJsonAsync("/api/v1/settings/document-types", new SaveDocumentTypeRequest("bad_one", "Bad", null, true, 1, [new("x", 1)], [field]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(expected);
    }

    [Fact]
    public async Task The_fields_a_value_can_be_saved_to_include_the_workspaces_own_custom_fields()
    {
        var admin = await AdminAsync();
        (await admin.Client.PostAsJsonAsync("/api/v1/settings/custom-fields",
            new SaveCustomFieldRequest("contacts", "cf_passport_no", "Passport number", null, FieldType.Text, false, true, true, null, 0, null))).EnsureSuccessStatusCode();
        (await admin.Client.PostAsJsonAsync("/api/v1/settings/custom-fields",
            new SaveCustomFieldRequest("contacts", "cf_vip", "VIP", null, FieldType.Boolean, false, true, true, null, 0, null))).EnsureSuccessStatusCode();

        var targets = await admin.Client.GetFromJsonAsync<List<RecordTargetDto>>("/api/v1/documents/targets");

        targets!.ShouldContain(t => t.Entity == "contacts" && t.Key == "first_name" && !t.IsCustom);
        targets.ShouldContain(t => t.Entity == "contacts" && t.Key == "cf_passport_no" && t.IsCustom && t.ValueType == DocumentValueTypes.Text);
        targets.ShouldNotContain(t => t.Key == "cf_vip");   // a yes/no field cannot take a value read from a document
        targets.ShouldContain(t => t.Entity == "deals" && t.Key == "amount");
    }

    [Fact]
    public async Task Starter_types_are_added_once_and_each_has_described_fields()
    {
        var admin = await AdminAsync(aiOn: false);

        var first = await SendAsync<List<DocumentTypeDto>>(await admin.Client.PostAsync("/api/v1/settings/document-types/starters", null));
        var second = await SendAsync<List<DocumentTypeDto>>(await admin.Client.PostAsync("/api/v1/settings/document-types/starters", null));

        first.Count.ShouldBeGreaterThanOrEqualTo(8);
        second.ShouldBeEmpty();
        first.ShouldAllBe(t => t.Fields.Count > 0 && t.Fields.All(f => !string.IsNullOrWhiteSpace(f.Description)));
        first.Select(t => t.Key).ShouldContain("appointment_receipt");
        first.Single(t => t.Key == "resume").Fields.Single(f => f.Key == "email").SaveTo.ShouldBe("contacts.email");
        (await admin.Client.GetFromJsonAsync<List<DocumentTypeChoiceDto>>("/api/v1/documents/types"))!.Select(c => c.Key).ShouldContain("resume");
    }

    // ---------- Switching AI on ----------

    [Fact]
    public async Task AI_reading_is_off_until_an_administrator_switches_it_on_and_the_status_says_whether_the_model_answers()
    {
        factory.Llm.Reset();
        var admin = await Workspaces.SignupAsync(factory);

        var before = await admin.Client.GetFromJsonAsync<DocumentAiStatusDto>("/api/v1/settings/document-types/ai");
        before!.Enabled.ShouldBeFalse();
        before.Configured.ShouldBeTrue();
        before.Reachable.ShouldBeTrue();

        var after = await SendAsync<DocumentAiStatusDto>(await admin.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true)));
        after.Enabled.ShouldBeTrue();

        factory.Llm.FailWith = "Ollama is not running at http://localhost:11434. Start it, then try again.";
        var down = await admin.Client.GetFromJsonAsync<DocumentAiStatusDto>("/api/v1/settings/document-types/ai");
        down!.Reachable.ShouldBeFalse();
        down.Message.ShouldContain("Ollama is not running");
    }

    [Fact]
    public async Task AI_reading_cannot_be_switched_on_when_no_model_is_set_up()
    {
        factory.Llm.Reset();
        factory.Llm.Configured = false;
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("No AI model is set up");
    }

    [Fact]
    public async Task Only_a_person_who_may_edit_settings_can_switch_AI_on()
    {
        var admin = await AdminAsync(aiOn: false);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "ai-switch");

        (await staff.Client.PutAsJsonAsync("/api/v1/settings/document-types/ai", new SaveDocumentAiRequest(true))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- The whole path ----------

    [Fact]
    public async Task A_document_is_read_in_the_background_checked_against_its_text_approved_and_saved_into_the_contacts_own_fields()
    {
        var admin = await AdminAsync();
        (await admin.Client.PostAsJsonAsync("/api/v1/settings/custom-fields",
            new SaveCustomFieldRequest("contacts", "cf_date_of_birth", "Date of birth", null, FieldType.Date, false, true, true, null, 0, null))).EnsureSuccessStatusCode();
        await CreateTypeAsync(admin, ReceiptType(dobTarget: "contacts.cf_date_of_birth"));
        var contact = await CreateContactAsync(admin);
        factory.Llm.Answer = (_, _, _) => ReceiptAnswer;

        // Upload does not read. Reading is queued and done in the background.
        var uploaded = await UploadAsync(admin, Receipt, contactId: contact.Id);
        uploaded.OcrStatus.ShouldBe(DocumentOcrStatus.Pending);
        factory.Llm.Calls.ShouldBeEmpty();
        var doc = await ReadAsync(admin, uploaded.Id);

        doc.Status.ShouldBe("NeedsReview");
        doc.Category.ShouldBe("appointment");
        doc.OcrResult!.ExtractionMethod.ShouldBe("ai");
        doc.OcrResult.ExtractionNote.ShouldBeNull();
        doc.OcrResult.ReadConfidence.ShouldBe(1.0);
        var given = Of(doc, "given_name");
        given.Value.ShouldBe("CHITRA");
        given.Method.ShouldBe("ai");
        given.Verified.ShouldBe(true);
        given.Label.ShouldBe("Given name");
        given.SaveTo.ShouldBe("contacts.first_name");
        given.Snippet!.ShouldContain("CHITRA");
        Of(doc, "date_of_birth").Value.ShouldBe("1984-04-25");
        Of(doc, "reference").Value.ShouldBe("26-1060599710");

        // The model was told what each field is, in the administrator's words.
        var call = factory.Llm.Calls.Single();
        call.User.ShouldContain("The applicant's first name");
        call.User.ShouldContain("Answer with one of: Male | Female");

        // A person approves, and the values go to the linked contact.
        var apply = doc.OcrResult.ExtractedFields.Where(f => f.SaveTo is not null).Select(f => new ApplyFieldToRecordItem(f.FieldName, f.SaveTo!, f.Value)).ToList();
        var approved = await SendAsync<BusinessDocumentDto>(await admin.Client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/review", new ReviewDocumentRequest("Approve", null, apply)));

        approved.Status.ShouldBe("Approved");
        var saved = (await admin.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!;
        saved.FirstName.ShouldBe("CHITRA");
        saved.LastName.ShouldBe("UDHAYAKUMAR");
        saved.CustomFields["cf_date_of_birth"].GetString().ShouldBe("1984-04-25");
    }

    [Fact]
    public async Task A_value_the_model_made_up_is_shown_as_unverified_with_low_confidence()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => """{"given_name":"RAMESH","surname":"UDHAYAKUMAR","date_of_birth":"25/04/1984","gender":"FEMALE","reference":"26-1060599710"}""";
        var uploaded = await UploadAsync(admin, Receipt);

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        var invented = Of(doc, "given_name");
        invented.Value.ShouldBe("RAMESH");
        invented.Verified.ShouldBe(false);
        invented.Confidence.ShouldBeLessThan(0.5);
        Of(doc, "surname").Verified.ShouldBe(true);
    }

    [Fact]
    public async Task Fields_the_document_does_not_contain_are_still_listed_empty_for_a_person_to_fill_in()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => """{"given_name":"CHITRA","surname":null,"date_of_birth":null,"gender":null,"reference":null}""";
        var uploaded = await UploadAsync(admin, "Application Reference No. 26-1060599710\nGiven Name CHITRA");

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        var surname = Of(doc, "surname");
        surname.Value.ShouldBeEmpty();
        surname.Method.ShouldBeNull();
        surname.Required.ShouldBeTrue();
        Of(doc, "reference").Value.ShouldStartWith("26-1060599710");   // no AI answer, so the anchor found it
        Of(doc, "reference").Method.ShouldBe("anchor");
        doc.OcrResult!.ExtractionMethod.ShouldBe("mixed");
    }

    [Fact]
    public async Task With_AI_off_the_model_is_never_called_and_patterns_read_the_document()
    {
        var admin = await AdminAsync(aiOn: false);
        await CreateTypeAsync(admin, ReceiptType());
        var uploaded = await UploadAsync(admin, Receipt);

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        factory.Llm.Calls.ShouldBeEmpty();
        doc.OcrResult!.ExtractionMethod.ShouldBe("rules");
        Of(doc, "reference").Method.ShouldBe("anchor");
        Of(doc, "given_name").Value.ShouldBeEmpty();   // only AI could read it: it has no anchor
    }

    [Fact]
    public async Task When_the_model_is_down_the_document_is_still_read_by_patterns_and_a_note_says_why()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.FailWith = "Ollama is not running at http://localhost:11434. Start it, then try again.";
        var uploaded = await UploadAsync(admin, Receipt);

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        doc.Status.ShouldBe("NeedsReview");
        doc.OcrResult!.ExtractionMethod.ShouldBe("rules");
        doc.OcrResult.ExtractionNote!.ShouldContain("Ollama is not running");
        Of(doc, "reference").Value.ShouldStartWith("26-1060599710");
    }

    [Fact]
    public async Task A_document_the_model_answers_badly_for_is_not_left_stuck()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => "I think the name is Chitra";
        var uploaded = await UploadAsync(admin, Receipt);

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        doc.Status.ShouldBe("NeedsReview");
        doc.OcrResult!.ExtractionNote!.ShouldContain("could not be read");
    }

    [Fact]
    public async Task The_type_can_be_chosen_when_reading_and_a_wrong_choice_is_refused()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        await CreateTypeAsync(admin, new("licence", "Licence", null, true, 5, [new("zzz-nothing", 1)], [Field("licence_no", "Licence number", DocumentValueTypes.Id, "The licence number")]));
        factory.Llm.Answer = (_, user, _) => user.Contains("licence_no") ? """{"licence_no":"26-1060599710"}""" : ReceiptAnswer;
        var uploaded = await UploadAsync(admin, Receipt);

        var asReceipt = await ReadAsync(admin, uploaded.Id);
        asReceipt.Category.ShouldBe("appointment");

        var asLicence = await ReadAsync(admin, uploaded.Id, "licence");
        asLicence.Category.ShouldBe("licence");
        Of(asLicence, "licence_no").Value.ShouldBe("26-1060599710");
        asLicence.OcrResult!.ExtractedFields.ShouldNotContain(f => f.FieldName == "given_name");

        (await admin.Client.PostAsJsonAsync($"/api/v1/documents/{uploaded.Id}/read", new ReadDocumentRequest("no_such_type"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_type_chosen_on_upload_is_used_even_when_the_words_in_the_document_would_not_match_it()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, new("memo", "Memo", null, true, 50, [new("MEMO", 1)], [Field("subject", "Subject", DocumentValueTypes.Text, "What the memo is about")]));
        factory.Llm.Answer = (_, _, _) => """{"subject":"Quarterly budget"}""";
        var uploaded = await UploadAsync(admin, "Re: Quarterly budget\nPlease review.", category: "memo");

        var doc = await ReadAsync(admin, uploaded.Id);

        doc.Category.ShouldBe("memo");
        Of(doc, "subject").Value.ShouldBe("Quarterly budget");
    }

    [Fact]
    public async Task A_document_of_no_known_type_gets_the_useful_facts_listed_and_an_invented_one_is_dropped()
    {
        var admin = await AdminAsync();
        factory.Llm.Answer = (_, _, _) => """{"details":[{"label":"Surname","value":"UDHAYAKUMAR"},{"label":"Blood group","value":"O positive"}]}""";
        var uploaded = await UploadAsync(admin, Receipt);

        var doc = await ReadAsync(admin, uploaded.Id);

        doc.OcrResult!.ExtractionMethod.ShouldBe("ai");
        var fields = doc.OcrResult.ExtractedFields;
        fields.Single(f => f.Label == "Surname").Value.ShouldBe("UDHAYAKUMAR");
        fields.ShouldNotContain(f => f.Value.Contains("positive"));
    }

    [Fact]
    public async Task A_document_with_no_type_and_AI_off_is_not_given_a_made_up_total()
    {
        var admin = await AdminAsync(aiOn: false);
        var uploaded = await UploadAsync(admin, "Curriculum vitae\nA. Person\nExperience: 91 projects delivered");

        var doc = await ReadAsync(admin, uploaded.Id);

        doc.OcrResult!.ExtractedFields.ShouldNotContain(f => f.FieldName == "Total");
        doc.DetectedTotal.ShouldBeNull();
    }

    // ---------- Reviewing and saving ----------

    [Fact]
    public async Task Editing_a_value_marks_it_as_edited_and_keeps_what_was_known_about_the_other_fields()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => ReceiptAnswer;
        var doc = await ReadAsync(admin, (await UploadAsync(admin, Receipt)).Id, "appointment");
        var fields = doc.OcrResult!.ExtractedFields;
        fields.Single(f => f.FieldName == "given_name").Value = "Chitra";

        (await admin.Client.PutAsJsonAsync($"/api/v1/documents/{doc.Id}/fields", new UpdateExtractedFieldsRequest(fields))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var after = await GetAsync(admin, doc.Id);
        Of(after, "given_name").Method.ShouldBe("edited");
        Of(after, "given_name").Value.ShouldBe("Chitra");
        Of(after, "surname").Method.ShouldBe("ai");
        Of(after, "surname").Snippet.ShouldNotBeNull();
        Of(after, "surname").SaveTo.ShouldBe("contacts.last_name");
    }

    [Fact]
    public async Task Approving_into_a_record_the_document_is_not_linked_to_is_refused_and_nothing_changes()
    {
        var admin = await AdminAsync();
        await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => ReceiptAnswer;
        var doc = await ReadAsync(admin, (await UploadAsync(admin, Receipt)).Id, "appointment");

        var response = await admin.Client.PostAsJsonAsync($"/api/v1/documents/{doc.Id}/review",
            new ReviewDocumentRequest("Approve", null, [new ApplyFieldToRecordItem("given_name", "contacts.first_name", "CHITRA")]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("not linked");
        (await GetAsync(admin, doc.Id)).Status.ShouldBe("NeedsReview");
    }

    [Fact]
    public async Task A_value_that_does_not_fit_its_field_stops_the_approval_and_earlier_changes_are_rolled_back()
    {
        var admin = await AdminAsync();
        var contact = await CreateContactAsync(admin, "Keep", "Name");
        var uploaded = await UploadAsync(admin, "note", contactId: contact.Id);

        var response = await admin.Client.PostAsJsonAsync($"/api/v1/documents/{uploaded.Id}/review", new ReviewDocumentRequest("Approve", null,
        [
            new ApplyFieldToRecordItem("a", "contacts.first_name", "Changed"),
            new ApplyFieldToRecordItem("b", "contacts.email", "not an email"),
        ]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unchanged = (await admin.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!;
        unchanged.FirstName.ShouldBe("Keep");
        (await GetAsync(admin, uploaded.Id)).Status.ShouldNotBe("Approved");
    }

    [Fact]
    public async Task Values_can_be_saved_to_an_account_and_a_deal_as_well_as_a_contact()
    {
        var admin = await AdminAsync();
        (await admin.Client.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
        var account = await SendAsync<AccountDto>(await admin.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest("Old name", null, null, null, "Keep this note", null, null, null)));
        var pipeline = (await admin.Client.GetFromJsonAsync<List<PipelineDto>>("/api/v1/settings/pipelines"))!.First(p => p.IsDefault);
        var deal = await SendAsync<DealDto>(await admin.Client.PostAsJsonAsync("/api/v1/deals", new SaveDealRequest("Deal", pipeline.Id, null, 100, null, null, null, account.Id, null, null, null, null)));
        var uploaded = await UploadAsync(admin, "note", accountId: account.Id, dealId: deal.Id);

        await SendAsync<BusinessDocumentDto>(await admin.Client.PostAsJsonAsync($"/api/v1/documents/{uploaded.Id}/review", new ReviewDocumentRequest("Approve", null,
        [
            new ApplyFieldToRecordItem("company", "accounts.name", "Gulf Trading LLC"),
            new ApplyFieldToRecordItem("total", "deals.amount", "12450.75"),
            new ApplyFieldToRecordItem("close", "deals.expected_close_date", "2027-01-31"),
        ])));

        var savedAccount = (await admin.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{account.Id}"))!;
        savedAccount.Name.ShouldBe("Gulf Trading LLC");
        savedAccount.Description.ShouldBe("Keep this note");
        var savedDeal = (await admin.Client.GetFromJsonAsync<DealDto>($"/api/v1/deals/{deal.Id}"))!;
        savedDeal.Amount.ShouldBe(12450.75m);
        savedDeal.ExpectedCloseDate!.Value.Date.ShouldBe(new DateTime(2027, 1, 31));
        savedDeal.Name.ShouldBe("Deal");
    }

    [Fact]
    public async Task A_person_who_cannot_edit_the_linked_record_cannot_use_a_document_to_change_it()
    {
        var admin = await AdminAsync();
        var contact = await CreateContactAsync(admin, "Private", "Person");
        var readOnly = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "doc-apply");
        var uploaded = await UploadAsync(admin, "note", contactId: contact.Id);

        // Staff may review documents but only see their own contacts, so a contact owned by someone else cannot be changed through a document.
        var response = await readOnly.Client.PostAsJsonAsync($"/api/v1/documents/{uploaded.Id}/review",
            new ReviewDocumentRequest("Approve", null, [new ApplyFieldToRecordItem("a", "contacts.first_name", "Hacked")]));

        response.IsSuccessStatusCode.ShouldBeFalse();
        (await admin.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}"))!.FirstName.ShouldBe("Private");
    }

    // ---------- Linking ----------

    [Fact]
    public async Task A_document_can_be_linked_to_a_contact_and_not_to_another_workspaces_contact()
    {
        var admin = await AdminAsync();
        var stranger = await AdminAsync(aiOn: false);
        var mine = await CreateContactAsync(admin);
        var theirs = await CreateContactAsync(stranger);
        var uploaded = await UploadAsync(admin, "note");

        var linked = await SendAsync<BusinessDocumentDto>(await admin.Client.PutAsJsonAsync($"/api/v1/documents/{uploaded.Id}/links", new LinkDocumentRequest(mine.Id, null, null)));
        linked.AssociatedContactId.ShouldBe(mine.Id);

        (await admin.Client.PutAsJsonAsync($"/api/v1/documents/{uploaded.Id}/links", new LinkDocumentRequest(theirs.Id, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{uploaded.Id}"))!.AssociatedContactId.ShouldBe(mine.Id);
    }

    [Fact]
    public async Task The_documents_of_one_contact_can_be_listed_and_another_contacts_are_not_in_it()
    {
        var admin = await AdminAsync(aiOn: false);
        var one = await CreateContactAsync(admin, "One", "Person");
        var two = await CreateContactAsync(admin, "Two", "Person");
        var first = await UploadAsync(admin, "for one", contactId: one.Id);
        await UploadAsync(admin, "for two", contactId: two.Id);
        await UploadAsync(admin, "for nobody");

        var listed = (await admin.Client.GetFromJsonAsync<List<BusinessDocumentDto>>($"/api/v1/documents?contactId={one.Id}"))!;

        listed.Select(d => d.Id).ShouldBe([first.Id]);
    }

    [Fact]
    public async Task A_document_type_that_matched_lists_only_its_own_fields_not_the_built_in_invoice_ones()
    {
        var admin = await AdminAsync(aiOn: false);
        await CreateTypeAsync(admin, ReceiptType());
        var uploaded = await UploadAsync(admin, Receipt + "Total Fee (Rs.) 1500.00\n");

        var doc = await ReadAsync(admin, uploaded.Id, "appointment");

        doc.OcrResult!.ExtractedFields.ShouldAllBe(f => !string.IsNullOrEmpty(f.Key));
        doc.OcrResult.ExtractedFields.ShouldNotContain(f => f.FieldName == "Total");
    }

    // ---------- Suggesting fields ----------

    [Fact]
    public async Task Fields_can_be_suggested_from_a_sample_document_when_AI_is_on()
    {
        var admin = await AdminAsync();
        factory.Llm.Answer = (_, _, _) => """{"fields":[{"key":"surname","label":"Surname","value_type":"Text","description":"The applicant's surname","example":"UDHAYAKUMAR"},{"key":"dob","label":"Date of birth","value_type":"Date","description":"Date of birth","example":"25/04/1984"}]}""";

        var result = await SendAsync<SuggestFieldsResultDto>(await admin.Client.PostAsync("/api/v1/settings/document-types/suggest-fields?typeName=Receipt", FileForm(Receipt, "sample.txt")));

        result.Fields.Select(f => f.Key).ShouldBe(["surname", "dob"]);
        result.Fields.Single(f => f.Key == "dob").ValueType.ShouldBe(DocumentValueTypes.Date);
        factory.Llm.Calls.Single().User.ShouldContain("Document type: Receipt");
    }

    [Fact]
    public async Task Suggesting_fields_needs_AI_to_be_on()
    {
        var admin = await AdminAsync(aiOn: false);

        var response = await admin.Client.PostAsync("/api/v1/settings/document-types/suggest-fields", FileForm(Receipt, "sample.txt"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Switch on AI reading");
        factory.Llm.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Testing_a_type_on_a_sample_uses_the_AI_and_reports_each_value_with_where_it_was_found()
    {
        var admin = await AdminAsync();
        var type = await CreateTypeAsync(admin, ReceiptType());
        factory.Llm.Answer = (_, _, _) => ReceiptAnswer;

        var result = await SendAsync<DocumentTypeTestResultDto>(await admin.Client.PostAsync($"/api/v1/settings/document-types/{type.Id}/test", FileForm(Receipt, "sample.txt")));

        result.Matched.ShouldBeTrue();
        result.ExtractionMethod.ShouldBe("ai");
        var surname = result.Fields.Single(f => f.Key == "surname");
        surname.Value.ShouldBe("UDHAYAKUMAR");
        surname.Method.ShouldBe("ai");
        surname.Verified.ShouldBe(true);
        surname.Snippet!.ShouldContain("UDHAYAKUMAR");
    }

    // ---------- Isolation ----------

    [Fact]
    public async Task One_workspace_switching_AI_on_does_not_switch_it_on_for_another()
    {
        var first = await AdminAsync(aiOn: true);
        var second = await AdminAsync(aiOn: false);
        await CreateTypeAsync(second, ReceiptType());
        var uploaded = await UploadAsync(second, Receipt);

        await ReadAsync(second, uploaded.Id, "appointment");

        factory.Llm.Calls.ShouldBeEmpty();
        (await first.Client.GetFromJsonAsync<DocumentAiStatusDto>("/api/v1/settings/document-types/ai"))!.Enabled.ShouldBeTrue();
        (await second.Client.GetFromJsonAsync<DocumentAiStatusDto>("/api/v1/settings/document-types/ai"))!.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task What_the_review_screen_shows_about_the_reading_is_kept_when_the_document_is_opened_again()
    {
        var admin = await AdminAsync(aiOn: false);
        var uploaded = await UploadAsync(admin, "Total Amount Due: 100.00");

        await ReadAsync(admin, uploaded.Id);

        var reopened = await GetAsync(admin, uploaded.Id);
        reopened.OcrResult!.ReadConfidence.ShouldBe(1.0);
        reopened.OcrResult.ExtractionMethod.ShouldBe("rules");
        reopened.OcrResult.LowConfidencePages.ShouldBeEmpty();
    }
}
