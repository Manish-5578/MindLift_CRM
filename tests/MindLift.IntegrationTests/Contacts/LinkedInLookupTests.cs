using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Contacts;

public sealed class LinkedInLookupTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveContactRequest NewContact(string first, string last, string email)
        => new(first, last, email, "+1555123456", "Staff", null, null, null, null, null);

    private static SaveAccountRequest NewAccount(string name)
        => new(name, null, null, null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_linkedin_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // 1. Feature is OFF by default -> 404
        var offResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/person", new LookupLinkedInPersonRequest("john-doe"));
        offResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 2. Enable feature
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/linkedin-lookup", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // 3. Feature endpoint now succeeds -> 200 OK
        var onResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/person", new LookupLinkedInPersonRequest("john-doe"));
        onResp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lookup_person_and_company_by_url_and_handle()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/linkedin-lookup", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Person lookup with full LinkedIn URL
        var personUrl = "https://www.linkedin.com/in/satya-nadella/";
        var personResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/person", new LookupLinkedInPersonRequest(
            UrlOrHandle: personUrl,
            FullName: "Satya Nadella",
            Company: "Microsoft Corporation"));
        personResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var person = await personResp.Content.ReadFromJsonAsync<LinkedInPersonProfileDto>();

        person.ShouldNotBeNull();
        person.Handle.ShouldBe("satya-nadella");
        person.FullName.ShouldBe("Satya Nadella");
        person.CurrentCompany.ShouldBe("Microsoft Corporation");
        person.Skills.ShouldNotBeEmpty();
        person.PublicProfileUrl.ShouldBe("https://www.linkedin.com/in/satya-nadella");

        // 2. Company lookup with company URL
        var companyUrl = "https://www.linkedin.com/company/apple";
        var companyResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/company", new LookupLinkedInCompanyRequest(
            UrlOrHandle: companyUrl,
            CompanyName: "Apple Inc."));
        companyResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var company = await companyResp.Content.ReadFromJsonAsync<LinkedInCompanyProfileDto>();

        company.ShouldNotBeNull();
        company.Handle.ShouldBe("apple");
        company.CompanyName.ShouldBe("Apple Inc.");
        company.Industry.ShouldContain("Software");
        company.Specialties.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Apply_linkedin_profile_to_contact_and_account()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/linkedin-lookup", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Contact
        var contactEmail = $"tim_{Guid.NewGuid():N}@cupertino.org";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Tim", "Cook", contactEmail));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();
        contact.JobTitle.ShouldBe("Staff");

        // 2. Lookup & Apply to Contact
        var personResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/person", new LookupLinkedInPersonRequest(
            UrlOrHandle: "https://www.linkedin.com/in/tim-cook",
            FullName: "Tim Cook",
            Company: "Apple"));
        var personProfile = await personResp.Content.ReadFromJsonAsync<LinkedInPersonProfileDto>();
        personProfile.ShouldNotBeNull();

        var applyContactResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/apply/contact", new ApplyLinkedInToContactRequest(
            ContactId: contact.Id,
            Profile: personProfile));
        applyContactResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify updated contact
        var readContact = await admin.Client.GetFromJsonAsync<ContactDto>($"/api/v1/contacts/{contact.Id}");
        readContact.ShouldNotBeNull();
        readContact.JobTitle.ShouldBe(personProfile.CurrentRole);
        readContact.Description.ShouldBe(personProfile.Summary);

        // 3. Create Account
        var accResp = await admin.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Cupertino Hardware"));
        accResp.EnsureSuccessStatusCode();
        var acc = await accResp.Content.ReadFromJsonAsync<AccountDto>();
        acc.ShouldNotBeNull();
        acc.Industry.ShouldBeNull();

        // 4. Lookup & Apply to Account
        var compResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/company", new LookupLinkedInCompanyRequest(
            UrlOrHandle: "https://www.linkedin.com/company/cupertino-hardware",
            CompanyName: "Cupertino Hardware"));
        var compProfile = await compResp.Content.ReadFromJsonAsync<LinkedInCompanyProfileDto>();
        compProfile.ShouldNotBeNull();

        var applyAccResp = await admin.Client.PostAsJsonAsync("/api/v1/linkedin/apply/account", new ApplyLinkedInToAccountRequest(
            AccountId: acc.Id,
            Profile: compProfile));
        applyAccResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify updated account
        var readAcc = await admin.Client.GetFromJsonAsync<AccountDto>($"/api/v1/accounts/{acc.Id}");
        readAcc.ShouldNotBeNull();
        readAcc.Industry.ShouldBe(compProfile.Industry);
        readAcc.Website.ShouldBe(compProfile.WebsiteUrl);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_linkedin_apply()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/linkedin-lookup", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/linkedin-lookup", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var contactRespA = await tenantA.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("TenantA", "User", "secret@tenanta.com"));
        var contactA = await contactRespA.Content.ReadFromJsonAsync<ContactDto>();
        contactA.ShouldNotBeNull();

        var personResp = await tenantB.Client.PostAsJsonAsync("/api/v1/linkedin/lookup/person", new LookupLinkedInPersonRequest("hacker-handle"));
        var profile = await personResp.Content.ReadFromJsonAsync<LinkedInPersonProfileDto>();
        profile.ShouldNotBeNull();

        // Tenant B attempts to apply LinkedIn data to Tenant A's contact -> 404
        var crossApply = await tenantB.Client.PostAsJsonAsync("/api/v1/linkedin/apply/contact", new ApplyLinkedInToContactRequest(
            ContactId: contactA.Id,
            Profile: profile));
        crossApply.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
