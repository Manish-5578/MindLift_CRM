using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Files;

/// <summary>Plan 4.9: upload, download, validation, record permission and tenant isolation for files.</summary>
public sealed class FileEndpointTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string? entityType = null, Guid? entityId = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        if (entityType is not null)
        {
            content.Add(new StringContent(entityType), "entityType");
        }

        if (entityId is not null)
        {
            content.Add(new StringContent(entityId.Value.ToString()), "entityId");
        }

        return content;
    }

    private static async Task<StoredFileDto> UploadOkAsync(HttpClient client, byte[] bytes, string fileName, string? entityType = null, Guid? entityId = null)
    {
        var response = await client.PostAsync("/api/v1/files", Form(bytes, fileName, entityType, entityId));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;
    }

    private static async Task<Guid> CreateLeadAsync(HttpClient client)
    {
        var request = new SaveLeadRequest("File", "Holder", null, null, null, "new", "website", "warm", null, null, null, null);
        var response = await client.PostAsJsonAsync("/api/v1/leads", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LeadDto>())!.Id;
    }

    [Fact]
    public async Task An_uploaded_file_downloads_with_the_same_bytes_and_safe_headers()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var stored = await UploadOkAsync(owner.Client, Pdf, "contract.pdf");
        var download = await owner.Client.GetAsync($"/api/v1/files/{stored.Id}");

        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Pdf);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        download.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        stored.SizeBytes.ShouldBe(Pdf.Length);
    }

    [Fact]
    public async Task The_content_type_is_chosen_by_the_server_not_the_client()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/html");
        content.Add(file, "file", "claims-html.pdf");

        var response = await owner.Client.PostAsync("/api/v1/files", content);
        var stored = (await response.Content.ReadFromJsonAsync<StoredFileDto>())!;

        stored.ContentType.ShouldBe("application/pdf");
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("page.html")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    public async Task Disallowed_file_types_are_rejected(string fileName)
    {
        var owner = await Workspaces.SignupAsync(factory);

        var response = await owner.Client.PostAsync("/api/v1/files", Form(Pdf, fileName));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_file_whose_content_does_not_match_its_extension_is_rejected()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var notAPdf = Encoding.ASCII.GetBytes("MZ this is really an executable");

        var response = await owner.Client.PostAsync("/api/v1/files", Form(notAPdf, "invoice.pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_file_is_rejected()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var response = await owner.Client.PostAsync("/api/v1/files", Form([], "empty.pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_rejected()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var big = new byte[26 * 1024 * 1024];
        Pdf.CopyTo(big, 0);

        var response = await owner.Client.PostAsync("/api/v1/files", Form(big, "huge.pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_path_in_the_file_name_is_dropped()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var stored = await UploadOkAsync(owner.Client, Pdf, "..\\..\\windows\\evil.pdf");

        stored.FileName.ShouldBe("evil.pdf");
    }

    [Fact]
    public async Task Another_workspace_cannot_download_the_file()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);
        var stored = await UploadOkAsync(tenantA.Client, Pdf, "private.pdf");

        var response = await tenantB.Client.GetAsync($"/api/v1/files/{stored.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unattached_file_is_private_to_the_person_who_uploaded_it()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "file-staff");
        var stored = await UploadOkAsync(admin.Client, Pdf, "mine.pdf");

        var response = await staff.Client.GetAsync($"/api/v1/files/{stored.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_file_attached_to_a_lead_is_listed_and_readable_by_someone_who_can_view_leads()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "file-viewer");
        var leadId = await CreateLeadAsync(admin.Client);

        var stored = await UploadOkAsync(admin.Client, Pdf, "brief.pdf", "leads", leadId);

        stored.EntityType.ShouldBe("leads");
        stored.EntityId.ShouldBe(leadId);
        var list = await admin.Client.GetFromJsonAsync<List<StoredFileDto>>($"/api/v1/files?entityType=leads&entityId={leadId}");
        list!.Select(f => f.Id).ShouldContain(stored.Id);
        (await staff.Client.GetAsync($"/api/v1/files/{stored.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Attaching_to_a_record_that_does_not_exist_or_an_unknown_type_is_rejected()
    {
        var owner = await Workspaces.SignupAsync(factory);

        var missing = await owner.Client.PostAsync("/api/v1/files", Form(Pdf, "a.pdf", "leads", Guid.NewGuid()));
        var unknown = await owner.Client.PostAsync("/api/v1/files", Form(Pdf, "a.pdf", "secrets", Guid.NewGuid()));
        var half = await owner.Client.PostAsync("/api/v1/files", Form(Pdf, "a.pdf", "leads"));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        half.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_workspaces_record_cannot_be_used_to_attach_a_file()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);
        var leadInA = await CreateLeadAsync(tenantA.Client);

        var response = await tenantB.Client.PostAsync("/api/v1/files", Form(Pdf, "a.pdf", "leads", leadInA));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_uploader_can_delete_a_file_and_it_is_gone_afterwards_but_others_cannot_delete_it()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "file-deleter");
        var stored = await UploadOkAsync(staff.Client, Pdf, "temp.pdf");

        (await admin.Client.DeleteAsync($"/api/v1/files/{stored.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await staff.Client.DeleteAsync($"/api/v1/files/{stored.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await staff.Client.GetAsync($"/api/v1/files/{stored.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploads_require_a_signed_in_user()
    {
        using var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsync("/api/v1/files", Form(Pdf, "a.pdf"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
