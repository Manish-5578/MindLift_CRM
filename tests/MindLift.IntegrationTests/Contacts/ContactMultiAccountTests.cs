using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Contacts;

/// <summary>Phase 1 / #2: a contact can be tied to several accounts, only when the feature is on and only inside the caller's scope.</summary>
public sealed class ContactMultiAccountTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableAsync(Session s)
        => (await s.Client.PutAsJsonAsync("/api/v1/features/contact-multi-account", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static async Task<Guid> AccountAsync(Session s, string name)
    {
        var r = await s.Client.PostAsJsonAsync("/api/v1/accounts", new SaveAccountRequest(name, null, null, null, null, null, null, null));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<AccountDto>())!.Id;
    }

    private static async Task<Guid> ContactAsync(Session s, Guid? accountId = null)
    {
        var r = await s.Client.PostAsJsonAsync("/api/v1/contacts", new SaveContactRequest("Ava", "Stone", null, null, null, null, accountId, null, null, null));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<ContactDto>())!.Id;
    }

    [Fact]
    public async Task The_endpoints_do_not_exist_until_the_feature_is_switched_on()
    {
        var owner = await Workspaces.SignupAsync(factory);
        var contact = await ContactAsync(owner);

        (await owner.Client.GetAsync($"/api/v1/contacts/{contact}/accounts")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await EnableAsync(owner);
        (await owner.Client.GetAsync($"/api/v1/contacts/{contact}/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_contact_can_be_linked_to_extra_accounts_and_the_primary_stays_first()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var primary = await AccountAsync(owner, "Primary Co");
        var extra = await AccountAsync(owner, "Extra Co");
        var contact = await ContactAsync(owner, primary);

        var add = await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, "Advisor"));
        add.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = (await add.Content.ReadFromJsonAsync<List<ContactAccountDto>>())!;

        list.Count.ShouldBe(2);
        list[0].IsPrimary.ShouldBeTrue();
        list[0].AccountId.ShouldBe(primary);
        list[1].AccountId.ShouldBe(extra);
        list[1].Role.ShouldBe("Advisor");
    }

    [Fact]
    public async Task Linking_the_same_account_twice_or_the_primary_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var primary = await AccountAsync(owner, "P");
        var extra = await AccountAsync(owner, "E");
        var contact = await ContactAsync(owner, primary);

        (await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, null))).EnsureSuccessStatusCode();
        (await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(primary, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_overlong_role_is_refused()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var extra = await AccountAsync(owner, "E");
        var contact = await ContactAsync(owner);

        var r = await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, new string('x', 101)));

        r.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_link_can_be_removed_and_added_again()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var extra = await AccountAsync(owner, "E");
        var contact = await ContactAsync(owner);
        var list = (await (await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, null))).Content.ReadFromJsonAsync<List<ContactAccountDto>>())!;

        (await owner.Client.DeleteAsync($"/api/v1/contacts/{contact}/accounts/{list.Single().LinkId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.Client.GetFromJsonAsync<List<ContactAccountDto>>($"/api/v1/contacts/{contact}/accounts"))!.ShouldBeEmpty();
        (await owner.Client.PostAsJsonAsync($"/api/v1/contacts/{contact}/accounts", new AddContactAccountRequest(extra, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_company_cannot_see_or_link_anything()
    {
        var a = await Workspaces.SignupAsync(factory);
        var b = await Workspaces.SignupAsync(factory);
        await EnableAsync(a);
        await EnableAsync(b);
        var aAccount = await AccountAsync(a, "A Co");
        var aContact = await ContactAsync(a);
        var bContact = await ContactAsync(b);

        // B cannot open A's contact, and cannot attach A's account to its own contact.
        (await b.Client.GetAsync($"/api/v1/contacts/{aContact}/accounts")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await b.Client.PostAsJsonAsync($"/api/v1/contacts/{bContact}/accounts", new AddContactAccountRequest(aAccount, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await a.Client.GetFromJsonAsync<List<ContactAccountDto>>($"/api/v1/contacts/{aContact}/accounts"))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_signing_in_nothing_is_reachable()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var contact = await ContactAsync(owner);

        (await factory.CreateClient().GetAsync($"/api/v1/contacts/{contact}/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_staff_member_cannot_touch_a_contact_outside_their_own_records()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EnableAsync(owner);
        var staff = await Workspaces.InviteAndAcceptAsync(factory, owner, "Staff", "staff-multiacct");
        var extra = await AccountAsync(owner, "E");
        var ownersContact = await ContactAsync(owner);

        (await staff.Client.GetAsync($"/api/v1/contacts/{ownersContact}/accounts")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await staff.Client.PostAsJsonAsync($"/api/v1/contacts/{ownersContact}/accounts", new AddContactAccountRequest(extra, null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
