using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Portal;

public sealed class PortalFlowTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private async Task<Tenant> GetTenantAsync(string tenantName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tenants.AsNoTracking().SingleAsync(t => t.Name == tenantName);
    }

    [Fact]
    public async Task Anonymous_can_view_branding_by_slug()
    {
        var companyName = $"PortalBranding_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);
        (await admin.Client.PutAsJsonAsync("/api/v1/settings/portal", new SavePortalSettingsRequest(true, null, null, true, true, true, null, null))).EnsureSuccessStatusCode();

        var anonClient = factory.CreateClient();
        var resp = await anonClient.GetAsync($"/api/v1/portal/{tenant.Slug}/branding");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var branding = await resp.Content.ReadFromJsonAsync<PortalBrandingDto>();
        branding.ShouldNotBeNull();
        branding.TenantName.ShouldBe(companyName);
        branding.Enabled.ShouldBeTrue();
        branding.PrimaryColor.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Can_invite_contact_and_accept_invite()
    {
        var companyName = $"PortalInvite_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);

        // 1. Create a contact in CRM
        var contactEmail = $"customer-{Guid.NewGuid():N}@example.test";
        var contactReq = new SaveContactRequest("Fatima", "Zahra", contactEmail, "+971501112233", "Director", null, null, "client", null, null);
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", contactReq);
        contactResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Staff invites contact to portal
        var inviteReq = new InvitePortalUserRequest(contact.Id, contactEmail);
        var inviteResp = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite", inviteReq);
        inviteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var access = await inviteResp.Content.ReadFromJsonAsync<PortalAccessStatusDto>();
        access!.Status.ShouldBe("Invited");
        (await inviteResp.Content.ReadAsStringAsync()).ShouldNotContain("token");
        var rawToken = factory.Emails.LatestTokenFor(contactEmail);
        rawToken.ShouldNotBeNullOrWhiteSpace();

        // 3. Customer accepts invite anonymously with a new password
        var anonClient = factory.CreateClient();
        var acceptReq = new AcceptPortalInviteRequest(rawToken, "SecurePass123!");
        var acceptResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/accept-invite", acceptReq);
        acceptResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var loginResp = await acceptResp.Content.ReadFromJsonAsync<PortalLoginResponse>();
        loginResp.ShouldNotBeNull();
        loginResp.CustomerName.ShouldBe("Fatima Zahra");
        loginResp.ContactId.ShouldBe(contact.Id);
        loginResp.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // 4. Customer can now authenticate on portal with the received token
        var portalClient = factory.CreateClient();
        portalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResp.AccessToken);

        var homeResp = await portalClient.GetAsync("/api/v1/portal/home");
        homeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var home = await homeResp.Content.ReadFromJsonAsync<PortalHomeDto>();
        home.ShouldNotBeNull();
        home.CustomerName.ShouldBe("Fatima Zahra");

        var after = await admin.Client.GetFromJsonAsync<PortalAccessStatusDto>($"/api/v1/contacts/{contact.Id}/portal");
        after!.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task Customer_can_login_with_password_and_magic_link()
    {
        var companyName = $"PortalLogin_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);

        // 1. Create and invite contact
        var email = $"loginuser-{Guid.NewGuid():N}@example.test";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Tariq", "Mansour", email, "+971509998877", "Owner", null, null, "client", null, null));
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        var inviteResp = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite",
            new InvitePortalUserRequest(contact.Id, email));
        inviteResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = factory.Emails.LatestTokenFor(email);

        var anonClient = factory.CreateClient();
        var acceptResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(token, "MyPortalPass123!"));
        acceptResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 2. Normal password sign-in
        var passLoginResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/login",
            new PortalLoginRequest(email, "MyPortalPass123!"));
        passLoginResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var passLogin = await passLoginResp.Content.ReadFromJsonAsync<PortalLoginResponse>();
        passLogin.ShouldNotBeNull();
        passLogin.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // 3. Magic link sign-in request
        var magicReqResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/magic-link",
            new PortalMagicLinkRequest(email));
        magicReqResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Find magic token in database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var portalUser = await db.PortalUsers.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Email == email);
        var magicTokenEntity = await db.PortalTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.PortalUserId == portalUser.Id && t.Purpose == "MagicLink" && t.UsedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();
        magicTokenEntity.ShouldNotBeNull();
    }

    [Fact]
    public async Task Customer_portal_home_aggregates_shared_documents_and_open_requests()
    {
        var companyName = $"PortalHome_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);

        // 1. Setup contact and portal user
        var email = $"homeuser-{Guid.NewGuid():N}@example.test";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Rashid", "Al-Nuaimi", email, "+971501234455", "CEO", null, null, "client", null, null));
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();

        var inviteResp = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact!.Id}/portal/invite",
            new InvitePortalUserRequest(contact.Id, email));
        inviteResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anonClient = factory.CreateClient();
        var acceptResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(email), "Pass4567!"));
        var portalAuth = await acceptResp.Content.ReadFromJsonAsync<PortalLoginResponse>();

        // 2. Staff creates a document request for this contact
        var reqDoc = new SaveDocumentRequest(
            ContactId: contact.Id,
            AccountId: null,
            DealId: null,
            Title: "Provide Trade License 2026",
            Description: "Please upload your valid trade license copy",
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14))
        );
        var createDocReqResp = await admin.Client.PostAsJsonAsync("/api/v1/document-requests", reqDoc);
        createDocReqResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Staff uploads a business document shared with portal
        var pdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 contract demo content for customer");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(pdfBytes)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", "Contract_Agreement.pdf");
        form.Add(new StringContent("Contract"), "category");
        form.Add(new StringContent(contact.Id.ToString()), "contactId");
        form.Add(new StringContent("true"), "sharedWithPortal");

        var docUploadResp = await admin.Client.PostAsync("/api/v1/documents/upload", form);
        docUploadResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 4. Customer calls /portal/home
        var portalClient = factory.CreateClient();
        portalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", portalAuth!.AccessToken);

        var homeResp = await portalClient.GetAsync("/api/v1/portal/home");
        homeResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var home = await homeResp.Content.ReadFromJsonAsync<PortalHomeDto>();
        home.ShouldNotBeNull();
        home.CustomerName.ShouldBe("Rashid Al-Nuaimi");
        home.OpenRequests.Count.ShouldBe(1);
        home.OpenRequests[0].Title.ShouldBe("Provide Trade License 2026");
        home.RecentDocuments.Count.ShouldBe(1);
        home.RecentDocuments[0].FileName.ShouldBe("Contract_Agreement.pdf");
        home.Metrics.ShouldNotBeNull();
    }

    [Fact]
    public async Task Customer_can_fulfill_document_request_via_upload()
    {
        var companyName = $"PortalReqUp_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);

        // 1. Setup contact and portal auth
        var email = $"fulfill-{Guid.NewGuid():N}@example.test";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Salim", "Qasimi", email, null, null, null, null, "client", null, null));
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();

        var inviteResp = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact!.Id}/portal/invite",
            new InvitePortalUserRequest(contact.Id, email));
        inviteResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anonClient = factory.CreateClient();
        var acceptResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(email), "PassWord999!"));
        acceptResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var portalAuth = await acceptResp.Content.ReadFromJsonAsync<PortalLoginResponse>();

        // 2. Staff creates document request
        var reqDoc = new SaveDocumentRequest(
            ContactId: contact.Id,
            AccountId: null,
            DealId: null,
            Title: "Bank Confirmation Letter",
            Description: "Official bank confirmation stamped within 30 days",
            DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))
        );
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/document-requests", reqDoc);
        createResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var createdRequest = await createResp.Content.ReadFromJsonAsync<PortalDocumentRequestDto>();
        createdRequest.ShouldNotBeNull();

        // 3. Customer uploads document to fulfill request
        var portalClient = factory.CreateClient();
        portalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", portalAuth!.AccessToken);

        var docBytes = Encoding.UTF8.GetBytes("%PDF-1.4 Bank confirmation letter sample");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(docBytes)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", "Bank_Confirmation_Letter.pdf");

        var uploadResp = await portalClient.PostAsync($"/api/v1/portal/requests/{createdRequest.Id}/upload", form);
        uploadResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var uploadedDoc = await uploadResp.Content.ReadFromJsonAsync<BusinessDocumentDto>();
        uploadedDoc.ShouldNotBeNull();
        uploadedDoc.FileName.ShouldBe("Bank_Confirmation_Letter.pdf");

        // 4. Verify request status is now Submitted
        var reqListResp = await portalClient.GetAsync("/api/v1/portal/requests");
        reqListResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reqList = await reqListResp.Content.ReadFromJsonAsync<List<PortalDocumentRequestDto>>();
        reqList.ShouldNotBeNull();
        var fulfilledReq = reqList.Single(r => r.Id == createdRequest.Id);
        fulfilledReq.Status.ShouldBe("Submitted");
    }

    [Fact]
    public async Task Security_isolation_between_staff_and_portal_tokens()
    {
        var companyName = $"PortalIso_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);
        var tenant = await GetTenantAsync(companyName);

        // 1. Staff token accessing portal-only endpoint (/api/v1/portal/home) -> should fail (401 or 403)
        var staffHomeResp = await admin.Client.GetAsync("/api/v1/portal/home");
        (staffHomeResp.StatusCode == HttpStatusCode.Unauthorized || staffHomeResp.StatusCode == HttpStatusCode.Forbidden).ShouldBeTrue();

        // 2. Create customer portal token
        var email = $"isouser-{Guid.NewGuid():N}@example.test";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Kareem", "Zaki", email, null, null, null, null, "client", null, null));
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();

        var inviteResp = await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact!.Id}/portal/invite",
            new InvitePortalUserRequest(contact.Id, email));
        inviteResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        var anonClient = factory.CreateClient();
        var acceptResp = await anonClient.PostAsJsonAsync($"/api/v1/portal/{tenant.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(email), "StrongCustomerPass!"));
        var portalAuth = await acceptResp.Content.ReadFromJsonAsync<PortalLoginResponse>();

        var portalClient = factory.CreateClient();
        portalClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", portalAuth!.AccessToken);

        // 3. Portal token accessing staff CRM endpoint (/api/v1/deals/{id}) -> should fail (401 or 403)
        var staffDealsResp = await portalClient.GetAsync($"/api/v1/deals/{Guid.NewGuid()}");
        (staffDealsResp.StatusCode == HttpStatusCode.Unauthorized || staffDealsResp.StatusCode == HttpStatusCode.Forbidden).ShouldBeTrue();
    }

    [Fact]
    public async Task Staff_can_manage_portal_settings()
    {
        var companyName = $"PortalSet_{Guid.NewGuid():N}"[..20];
        var admin = await Workspaces.SignupAsync(factory, companyName);

        // 1. Get default portal settings
        var getResp = await admin.Client.GetAsync("/api/v1/settings/portal");
        getResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await getResp.Content.ReadFromJsonAsync<PortalSettingsDto>();
        settings.ShouldNotBeNull();
        settings.IsEnabled.ShouldBeTrue();

        // 2. Update settings
        var updateReq = new SavePortalSettingsRequest(
            IsEnabled: true,
            WelcomeMessage: "Welcome to Acme Customer Space",
            WelcomeMessageAr: "مرحباً بكم في بوابة العملاء",
            ShowMetrics: true,
            AllowGeneralUploads: true,
            NotifyCustomerOnExpiry: false,
            SupportEmail: "support@acme.test",
            SupportPhone: "+9714440011"
        );

        var putResp = await admin.Client.PutAsJsonAsync("/api/v1/settings/portal", updateReq);
        putResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await putResp.Content.ReadFromJsonAsync<PortalSettingsDto>();
        updated.ShouldNotBeNull();
        updated.WelcomeMessage.ShouldBe("Welcome to Acme Customer Space");
        updated.AllowGeneralUploads.ShouldBeTrue();
        updated.SupportEmail.ShouldBe("support@acme.test");
    }
}
