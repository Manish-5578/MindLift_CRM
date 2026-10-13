using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using MindLift.Infrastructure.Documents.Extraction;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Documents;

/// <summary>Workspace document types (plan 19.6): the fields a customer chooses are read from their documents.</summary>
public sealed class DocumentTypeTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private const string LicenceText = "TRADE LICENSE\nLicense No: CN-1234567\nExpiry Date: 31/12/2027\nTotal Capital: 250000 AED\nCompany: Gulf Trading LLC";

    private static SaveDocumentTypeRequest LicenceType(string key = "trade_licence_custom") => new(
        key, "Trade licence (custom)", "Our own layout", true, 3,
        [new("TRADE LICENSE", 3), new("License No", 2), new("رخصة", 2)],
        [
            new("license_number", "Licence number", DocumentValueTypes.Id, "License No:", null, true, false),
            new("expiry_date", "Expiry date", DocumentValueTypes.Date, "Expiry Date:", null, true, false),
            new("capital", "Capital", DocumentValueTypes.Amount, "Total Capital:", null, false, true),
            new("company", "Company", DocumentValueTypes.Text, null, @"Company:\s*([A-Za-z ]+LLC)", false, false),
        ]);

    private static async Task<T> SendAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        return form;
    }

    [Fact]
    public void Anchors_and_patterns_find_the_values_and_normalise_them()
    {
        var fields = CustomDocumentExtractor.Extract(LicenceType().Fields, LicenceText);

        fields.Single(f => f.Key == "license_number").Value.ShouldBe("CN-1234567");
        fields.Single(f => f.Key == "expiry_date").Value.ShouldBe("2027-12-31");
        fields.Single(f => f.Key == "capital").Value.ShouldBe("250000.00");
        fields.Single(f => f.Key == "company").Value.ShouldBe("Gulf Trading LLC");
        fields.All(f => f.Found).ShouldBeTrue();
    }

    [Fact]
    public void A_field_that_is_not_in_the_document_is_reported_as_not_found_and_not_guessed()
    {
        var rules = new[] { new DocumentFieldRuleDto("tax_id", "Tax ID", DocumentValueTypes.Id, "Tax ID:", null, true, false) };

        var fields = CustomDocumentExtractor.Extract(rules, LicenceText);

        fields[0].Found.ShouldBeFalse();
        fields[0].Value.ShouldBeNull();
    }

    [Fact]
    public void A_date_that_does_not_parse_is_not_stored()
    {
        var rules = new[] { new DocumentFieldRuleDto("expiry_date", "Expiry", DocumentValueTypes.Date, "Expiry Date:", null, false, false) };

        CustomDocumentExtractor.Extract(rules, "Expiry Date: not a date").Single().Found.ShouldBeFalse();
    }

    [Fact]
    public void Arabic_indic_digits_are_read_as_ordinary_digits()
    {
        var rules = new[] { new DocumentFieldRuleDto("capital", "Capital", DocumentValueTypes.Amount, "Total Capital:", null, false, true) };

        CustomDocumentExtractor.Extract(rules, "Total Capital: ٢٥٠٠٠٠ AED").Single().Value.ShouldBe("250000.00");
    }

    [Fact]
    public void A_document_scores_by_the_weight_of_the_keywords_it_contains()
    {
        var keywords = LicenceType().Keywords;

        CustomDocumentExtractor.Score(keywords, LicenceText).ShouldBe(3 + 2);
        CustomDocumentExtractor.Score(keywords, "An invoice for consulting").ShouldBe(0);
    }

    [Fact]
    public async Task A_type_is_created_listed_edited_and_deleted()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var created = await SendAsync<DocumentTypeDto>(await admin.Client.PostAsJsonAsync("/api/v1/settings/document-types", LicenceType()));
        created.Fields.Count.ShouldBe(4);
        created.Keywords.Count.ShouldBe(3);

        var edited = await SendAsync<DocumentTypeDto>(await admin.Client.PutAsJsonAsync($"/api/v1/settings/document-types/{created.Id}",
            LicenceType() with { Name = "Licence v2" }));
        edited.Name.ShouldBe("Licence v2");

        (await admin.Client.DeleteAsync($"/api/v1/settings/document-types/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<List<DocumentTypeDto>>(await admin.Client.GetAsync("/api/v1/settings/document-types"))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bad_definitions_are_refused_field_by_field()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var url = "/api/v1/settings/document-types";

        (await admin.Client.PostAsJsonAsync(url, LicenceType("Bad Key!"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync(url, LicenceType() with
        {
            Fields = [new("bad_pattern", "Bad", DocumentValueTypes.Text, null, "([unclosed", false, false)],
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync(url, LicenceType() with
        {
            Fields = [new("no_rule", "No rule", DocumentValueTypes.Text, null, null, false, false)],
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.Client.PostAsJsonAsync(url, LicenceType() with
        {
            Fields = [new("wrong_type", "Wrong", "Colour", "Label:", null, false, false)],
        })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await SendAsync<DocumentTypeDto>(await admin.Client.PostAsJsonAsync(url, LicenceType("taken_key")));
        (await admin.Client.PostAsJsonAsync(url, LicenceType("taken_key"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_sample_file_can_be_tested_against_a_type_without_saving_anything()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var type = await SendAsync<DocumentTypeDto>(await admin.Client.PostAsJsonAsync("/api/v1/settings/document-types", LicenceType("sample_test")));
        var sample = await File.ReadAllBytesAsync(Path.Combine(FindRepoRoot(), "tests", "fixtures", "documents", "trade-license-en.png"));

        var result = await SendAsync<DocumentTypeTestResultDto>(await admin.Client.PostAsync(
            $"/api/v1/settings/document-types/{type.Id}/test", FileForm(sample, "sample.png", "image/png")));

        result.Matched.ShouldBeTrue();
        result.TextSource.ShouldBe("Ocr");
        result.Fields.Single(f => f.Key == "license_number").Value.ShouldBe("CN-1234567");
        result.Fields.Single(f => f.Key == "expiry_date").Value.ShouldBe("2027-12-31");
    }

    [Fact]
    public async Task An_uploaded_document_of_a_custom_type_is_classified_and_its_chosen_fields_are_saved()
    {
        var admin = await Workspaces.SignupAsync(factory);
        await SendAsync<DocumentTypeDto>(await admin.Client.PostAsJsonAsync("/api/v1/settings/document-types", LicenceType("custom_licence")));

        var upload = await admin.Client.PostAsync("/api/v1/documents/upload", FileForm(Encoding.UTF8.GetBytes(LicenceText), "licence.txt", "text/plain"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var doc = (await upload.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;

        var ocr = await SendAsync<OcrResultDto>(await admin.Client.PostAsync($"/api/v1/documents/{doc.Id}/ocr", null));

        ocr.DocumentType.ShouldBe("custom_licence");
        ocr.ExtractedFields.Single(f => f.FieldName == "license_number").Value.ShouldBe("CN-1234567");
        ocr.ExtractedFields.Single(f => f.FieldName == "capital").Value.ShouldBe("250000.00");
        var stored = await admin.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/documents/{doc.Id}");
        stored!.ExpiryDate.ShouldBe(new DateOnly(2027, 12, 31));
    }

    [Fact]
    public async Task Another_workspace_cannot_see_or_test_these_types()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var type = await SendAsync<DocumentTypeDto>(await owner.Client.PostAsJsonAsync("/api/v1/settings/document-types", LicenceType("private_type")));
        var stranger = await Workspaces.SignupAsync(factory);

        (await stranger.Client.GetAsync($"/api/v1/settings/document-types/{type.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsync($"/api/v1/settings/document-types/{type.Id}/test", FileForm([1, 2, 3], "x.txt", "text/plain"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_renamed_executable_is_refused_as_a_document()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var executable = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 };

        var response = await admin.Client.PostAsync("/api/v1/documents/upload", FileForm(executable, "scan.png", "image/png"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Responses_carry_the_baseline_security_headers()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.GetAsync("/api/v1/users?pageSize=1");

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["strict-origin-when-cross-origin"]);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MindLift.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
