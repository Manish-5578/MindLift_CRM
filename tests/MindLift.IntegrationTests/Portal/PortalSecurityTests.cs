using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Portal;

/// <summary>What the customer portal must not allow: unusable email links, reusable tokens, giveaway sign-in errors, lingering sessions and unchecked links or file types.</summary>
public sealed class PortalSecurityTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private const string Password = "Customer!Pass1";

    private sealed record Invited(Session Admin, string Slug, Guid ContactId, string Email);

    private async Task<Invited> InviteAsync()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var me = await admin.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        string slug;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slug = await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == me!.TenantId).Select(t => t.Slug).SingleAsync();
        }

        var email = $"security-{Guid.NewGuid():N}@example.test";
        var created = await admin.Client.PostAsJsonAsync("/api/v1/contacts",
            new SaveContactRequest("Mona", "Synthetic", email, "+971500000001", null, null, null, "client", null, null));
        var contact = (await created.Content.ReadFromJsonAsync<ContactDto>())!;
        (await admin.Client.PostAsJsonAsync($"/api/v1/contacts/{contact.Id}/portal/invite", new InvitePortalUserRequest(contact.Id, email)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return new Invited(admin, slug, contact.Id, email);
    }

    private HttpClient Mobile()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client", "mobile");
        return client;
    }

    private async Task<PortalLoginResponse> AcceptAsync(Invited invited, HttpClient client)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/accept-invite",
            new AcceptPortalInviteRequest(factory.Emails.LatestTokenFor(invited.Email), Password));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortalLoginResponse>())!;
    }

    private string LatestBodyFor(string email)
        => factory.Emails.Sent.Reverse().First(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase)).Body;

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string claimedType, Guid? accountId = null)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
        form.Add(content, "file", fileName);
        if (accountId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "accountId");
        }

        return form;
    }

    [Fact]
    public async Task Invitation_and_magic_link_emails_contain_full_addresses()
    {
        var invited = await InviteAsync();

        Regex.IsMatch(LatestBodyFor(invited.Email), $@"https?://\S+/portal/{invited.Slug}/accept-invite\?token=").ShouldBeTrue();

        var client = Mobile();
        await AcceptAsync(invited, client);
        (await client.PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/magic-link", new PortalMagicLinkRequest(invited.Email)))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.Accepted);

        Regex.IsMatch(LatestBodyFor(invited.Email), $@"https?://\S+/portal/{invited.Slug}/magic\?token=").ShouldBeTrue();
    }

    [Fact]
    public async Task A_magic_link_signs_in_only_one_of_several_simultaneous_requests()
    {
        var invited = await InviteAsync();
        var client = Mobile();
        await AcceptAsync(invited, client);
        await client.PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/magic-link", new PortalMagicLinkRequest(invited.Email));
        var token = factory.Emails.LatestTokenFor(invited.Email);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/magic-link/verify", new PortalMagicLinkVerifyRequest(token))));

        attempts.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        attempts.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(5);
    }

    [Fact]
    public async Task An_invitation_can_be_accepted_only_once_even_with_simultaneous_requests()
    {
        var invited = await InviteAsync();
        var token = factory.Emails.LatestTokenFor(invited.Email);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/accept-invite", new AcceptPortalInviteRequest(token, Password))));

        attempts.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
    }

    [Fact]
    public async Task A_refresh_token_works_for_only_one_of_several_simultaneous_requests()
    {
        var invited = await InviteAsync();
        var first = await AcceptAsync(invited, Mobile());

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/refresh", new PortalRefreshRequest(first.RefreshToken))));

        attempts.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Sign_in_gives_the_same_answer_whatever_the_reason_it_fails()
    {
        var invited = await InviteAsync();
        var notYetActivated = await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/login", new PortalLoginRequest(invited.Email, Password));

        await AcceptAsync(invited, Mobile());
        var wrongPassword = await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/login", new PortalLoginRequest(invited.Email, "Not-The-Password-1"));
        var unknownAddress = await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/login", new PortalLoginRequest($"nobody-{Guid.NewGuid():N}@example.test", Password));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.PortalUsers.IgnoreQueryFilters().Where(u => u.NormalizedEmail == invited.Email.ToUpperInvariant())
                .ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, false));
        }

        var deactivated = await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/login", new PortalLoginRequest(invited.Email, Password));

        var answers = new List<(HttpStatusCode Status, string? Code, string Body)>();
        foreach (var response in new[] { notYetActivated, wrongPassword, unknownAddress, deactivated })
        {
            var body = await response.Content.ReadAsStringAsync();
            answers.Add((response.StatusCode, await CodeOf(body), body));
        }

        answers.ShouldAllBe(a => a.Status == HttpStatusCode.Unauthorized && a.Code == "invalid_credentials");
        answers.Select(a => a.Body).Distinct().Count().ShouldBe(1, "every failure should read the same");
    }

    private static Task<string?> CodeOf(string body)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        return Task.FromResult(doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    [Fact]
    public async Task Changing_the_password_signs_other_devices_out()
    {
        var invited = await InviteAsync();
        var client = Mobile();
        var session = await AcceptAsync(invited, client);
        var signedIn = Mobile();
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var change = await signedIn.PostAsJsonAsync("/api/v1/portal/me/password", new ChangePortalPasswordRequest(Password, "Another!Pass2"));
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent, await change.Content.ReadAsStringAsync());

        var refresh = await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/refresh", new PortalRefreshRequest(session.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Mobile().PostAsJsonAsync($"/api/v1/portal/{invited.Slug}/auth/login", new PortalLoginRequest(invited.Email, "Another!Pass2")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Repeated_wrong_current_passwords_lock_the_password_change()
    {
        var invited = await InviteAsync();
        var session = await AcceptAsync(invited, Mobile());
        var signedIn = Mobile();
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await signedIn.PostAsJsonAsync("/api/v1/portal/me/password", new ChangePortalPasswordRequest("Wrong-Current-1", "Another!Pass2")))
                .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        // Even the right current password is refused while the lock lasts.
        var locked = await signedIn.PostAsJsonAsync("/api/v1/portal/me/password", new ChangePortalPasswordRequest(Password, "Another!Pass2"));
        locked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await locked.Content.ReadAsStringAsync()).ShouldContain("Too many attempts");
    }

    [Fact]
    public async Task A_document_request_cannot_point_at_an_account_or_deal_that_is_not_the_companys()
    {
        var invited = await InviteAsync();

        var foreignAccount = await invited.Admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(invited.ContactId, Guid.NewGuid(), null, "Trade licence", null, null));
        var foreignDeal = await invited.Admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(invited.ContactId, null, Guid.NewGuid(), "Trade licence", null, null));
        var fine = await invited.Admin.Client.PostAsJsonAsync("/api/v1/document-requests",
            new SaveDocumentRequest(invited.ContactId, null, null, "Trade licence", null, null));

        foreignAccount.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreignDeal.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fine.StatusCode.ShouldBe(HttpStatusCode.OK, await fine.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_document_cannot_be_linked_to_a_record_that_is_not_the_companys()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

        var response = await admin.Client.PostAsync("/api/v1/documents/upload", FileForm(png, "scan.png", "image/png", accountId: Guid.NewGuid()));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("page.html", "<script>alert(1)</script>")]
    [InlineData("picture.svg", "<svg onload=\"alert(1)\"/>")]
    [InlineData("run.js", "alert(1)")]
    [InlineData("noextension", "plain text")]
    public async Task Documents_of_a_type_that_is_not_allowed_are_refused(string fileName, string content)
    {
        var admin = await Workspaces.SignupAsync(factory);

        var response = await admin.Client.PostAsync("/api/v1/documents/upload", FileForm(Encoding.UTF8.GetBytes(content), fileName, "text/html"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_document_is_stored_with_the_type_of_its_extension_not_the_one_the_sender_claimed()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

        var response = await admin.Client.PostAsync("/api/v1/documents/upload", FileForm(png, "scan.png", "text/html"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var stored = (await response.Content.ReadFromJsonAsync<BusinessDocumentDto>())!;
        stored.ContentType.ShouldBe("image/png");
    }
}
