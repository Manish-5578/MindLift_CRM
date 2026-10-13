using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Portal;

/// <summary>Customer-to-customer isolation, the staff review loop, portal sharing and permissions for the portal endpoints.</summary>
public sealed class PortalIsolationAndReviewTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed record Customer(Guid ContactId, string Email, HttpClient Client);

    private async Task<string> SlugAsync(Session admin)
    {
        var me = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == me!.TenantId).Select(t => t.Slug).SingleAsync();
    }

    /// <summary>Creates a contact, invites it, accepts from the emailed link and returns a signed-in portal client.</summary>
    private async Task<Customer> CustomerAsync(Session admin, string first)
    {
        var email = $"{first.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var created = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest(first, "Synthetic", email, null, null, null, null, "client", null, null));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var contact = (await created.Content.ReadFromJsonAsync<ContactDto>())!;

        var invite = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite", new InvitePortalUserRequest(contact.Id, email));
        invite.StatusCode.ShouldBe(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());

        var slug = await SlugAsync(admin);
        var accept = await factory.CreateClient().PostAsJsonAsync($"/api/v1/portal/{slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(email), "Customer!Pass1"));
        accept.StatusCode.ShouldBe(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        var login = (await accept.Content.ReadFromJsonAsync<PortalLoginResponse>())!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return new Customer(contact.Id, email, client);
    }

    private static async Task<BusinessDocumentDto> StaffUploadAsync(Session admin, Guid contactId, bool? shared, string name = "Synthetic_Contract.pdf")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 synthetic test content"))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", name);
        form.Add(new StringContent("Contract"), "category");
        form.Add(new StringContent(contactId.ToString()), "contactId");
        if (shared.HasValue) form.Add(new StringContent(shared.Value ? "true" : "false"), "sharedWithPortal");

        var resp = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        resp.StatusCode.ShouldBe(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;
    }

    private static async Task<PortalDocumentRequestDto> RequestAsync(Session admin, Guid contactId, string title = "Synthetic trade licence")
    {
        var resp = await admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(contactId, null, null, title, null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))));
        resp.StatusCode.ShouldBe(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<PortalDocumentRequestDto>())!;
    }

    private static async Task<HttpResponseMessage> CustomerUploadAsync(Customer c, Guid requestId, string name = "Synthetic_Upload.pdf")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 synthetic customer upload"))
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", name);
        return await c.Client.PostAsync($"/api/v1/portal/requests/{requestId}/upload", form);
    }

    [Fact]
    public async Task Customer_B_cannot_see_or_upload_to_customer_A_records_in_the_same_workspace()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var a = await CustomerAsync(admin, "Amal");
        var b = await CustomerAsync(admin, "Bilal");

        var docA = await StaffUploadAsync(admin, a.ContactId, shared: true);
        var reqA = await RequestAsync(admin, a.ContactId);

        (await a.Client.GetAsync($"/api/v1/portal/documents/{docA.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await b.Client.GetAsync($"/api/v1/portal/documents/{docA.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Client.GetAsync($"/api/v1/portal/documents/{docA.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var bDocs = await b.Client.GetFromJsonAsync<List<BusinessDocumentDto>>("/api/v1/portal/documents");
        bDocs!.ShouldBeEmpty();
        var bReqs = await b.Client.GetFromJsonAsync<List<PortalDocumentRequestDto>>("/api/v1/portal/requests");
        bReqs!.ShouldBeEmpty();

        (await CustomerUploadAsync(b, reqA.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_cannot_reach_another_workspaces_documents_or_requests()
    {
        var adminA = await Workspaces.SignupAsync(factory);
        var adminB = await Workspaces.SignupAsync(factory);
        var a = await CustomerAsync(adminA, "Huda");
        var b = await CustomerAsync(adminB, "Omar");

        var docA = await StaffUploadAsync(adminA, a.ContactId, shared: true);
        var reqA = await RequestAsync(adminA, a.ContactId);

        (await b.Client.GetAsync($"/api/v1/portal/documents/{docA.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CustomerUploadAsync(b, reqA.Id)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Staff in workspace B cannot see workspace A's portal access, requests or sharing.
        (await adminB.Client.GetAsync($"/api/v1/contacts/{a.ContactId}/portal")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await adminB.Client.PostAsJsonAsync($"/api/v1/document-requests/{reqA.Id}/status", new UpdateDocumentRequestStatusRequest("Cancelled")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await adminB.Client.PutAsJsonAsync($"/api/v1/documents/{docA.Id}/portal-sharing", new SetDocumentSharingRequest(false)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_uploads_are_private_until_shared()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var c = await CustomerAsync(admin, "Layla");

        var doc = await StaffUploadAsync(admin, c.ContactId, shared: null);
        doc.SharedWithPortal.ShouldBeFalse();
        (await c.Client.GetAsync($"/api/v1/portal/documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var share = await admin.Client.PutAsJsonAsync($"/api/v1/documents/{doc.Id}/portal-sharing", new SetDocumentSharingRequest(true));
        share.StatusCode.ShouldBe(HttpStatusCode.NoContent, await share.Content.ReadAsStringAsync());
        (await c.Client.GetAsync($"/api/v1/portal/documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        await admin.Client.PutAsJsonAsync($"/api/v1/documents/{doc.Id}/portal-sharing", new SetDocumentSharingRequest(false));
        (await c.Client.GetAsync($"/api/v1/portal/documents/{doc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Document_request_needs_an_existing_contact_and_a_title()
    {
        var admin = await Workspaces.SignupAsync(factory);

        var noContact = await admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(Guid.Empty, null, null, "Anything", null, null));
        noContact.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var c = await CustomerAsync(admin, "Noor");
        var noTitle = await admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(c.ContactId, null, null, " ", null, null));
        noTitle.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Customer_upload_notifies_staff_and_approval_marks_it_ready()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var c = await CustomerAsync(admin, "Yusuf");
        var req = await RequestAsync(admin, c.ContactId, "Synthetic bank letter");

        var upload = await CustomerUploadAsync(c, req.Id);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var doc = (await upload.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;
        doc.Status.ShouldBe("NeedsReview");

        var page = await admin.Client.GetFromJsonAsync<NotificationPageDto>("/api/v1/notifications?unreadOnly=false&page=1&pageSize=50");
        page!.Items.ShouldContain(n => n.Type == NotificationTypes.PortalUpload && n.EntityId == c.ContactId);

        var approve = await admin.Client.PostAsJsonAsync($"/api/v1/document-requests/{req.Id}/status", new UpdateDocumentRequestStatusRequest("Completed"));
        approve.StatusCode.ShouldBe(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        (await approve.Content.ReadFromJsonAsync<PortalDocumentRequestDto>())!.Status.ShouldBe("Completed");

        var after = await c.Client.GetFromJsonAsync<BusinessDocumentDto>($"/api/v1/portal/documents/{doc.Id}");
        after!.Status.ShouldBe("Ready");

        // A closed request no longer takes uploads.
        (await CustomerUploadAsync(c, req.Id)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Request_status_rejects_unknown_values()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var c = await CustomerAsync(admin, "Rania");
        var req = await RequestAsync(admin, c.ContactId);

        var bad = await admin.Client.PostAsJsonAsync($"/api/v1/document-requests/{req.Id}/status", new UpdateDocumentRequestStatusRequest("Approved"));
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Inviting_an_email_already_used_by_another_contact_conflicts()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var c = await CustomerAsync(admin, "Sami");

        var other = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Other", "Person", null, null, null, null, null, "client", null, null));
        var otherContact = (await other.Content.ReadFromJsonAsync<ContactDto>())!;

        var dup = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{otherContact.Id}/portal/invite", new InvitePortalUserRequest(otherContact.Id, c.Email));
        dup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Staff_role_may_review_but_a_role_without_document_rights_is_denied()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var c = await CustomerAsync(admin, "Zaid");
        var req = await RequestAsync(admin, c.ContactId);
        var doc = await StaffUploadAsync(admin, c.ContactId, shared: false);

        // Allowed: the built-in Staff role can edit documents and view contacts.
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "portal-staff");
        (await staff.Client.GetAsync($"/api/v1/contacts/{c.ContactId}/portal")).StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);

        // Denied: a new role starts with no permissions.
        var roleName = $"Reception {Guid.NewGuid():N}";
        var created = await admin.Client.PostAsJsonAsync("/api/v1/roles", new SaveRoleRequest(roleName, null, DashboardView.Staff));
        created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var none = await Workspaces.InviteAndAcceptAsync(factory, admin, roleName, "portal-none");

        (await none.Client.GetAsync($"/api/v1/contacts/{c.ContactId}/portal")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await none.Client.PostAsJsonAsync($"/api/v1/contacts/{c.ContactId}/portal/invite", new InvitePortalUserRequest(c.ContactId, c.Email)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await none.Client.PostAsJsonAsync($"/api/v1/document-requests/{req.Id}/status", new UpdateDocumentRequestStatusRequest("Cancelled")))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await none.Client.PutAsJsonAsync($"/api/v1/documents/{doc.Id}/portal-sharing", new SetDocumentSharingRequest(true)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await none.Client.PutAsJsonAsync("/api/v1/settings/portal", new SavePortalSettingsRequest(true, null, null, true, true, true, null, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A portal customer token cannot call staff portal endpoints.
        (await c.Client.GetAsync($"/api/v1/contacts/{c.ContactId}/portal")).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }
}
