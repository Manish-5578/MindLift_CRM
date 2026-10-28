using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Communication;

public sealed class MeetingLinkTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveContactRequest NewContact(string first, string last, string email, string phone)
        => new(first, last, email, phone, "Chief Strategist", null, null, null, null, null);

    [Fact]
    public async Task Feature_flag_gates_meeting_links_endpoints()
    {
        var admin = await Workspaces.SignupAsync(factory);

        // Feature is OFF by default
        var respOff = await admin.Client.GetAsync("/api/v1/meeting-links");
        respOff.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Turn feature ON
        var enableResp = await admin.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true));
        enableResp.EnsureSuccessStatusCode();

        // Feature endpoint now succeeds
        var respOn = await admin.Client.GetAsync("/api/v1/meeting-links");
        respOn.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_and_read_meeting_links_with_conversion_metrics()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create meeting link
        var createResp = await admin.Client.PostAsJsonAsync("/api/v1/meeting-links", new CreateMeetingLinkRequest(
            Title: "30-Min Product Walkthrough",
            Slug: $"walkthrough-{Guid.NewGuid():N}"[..20],
            Description: "Guided walkthrough of MindLift features",
            DurationMinutes: 30,
            LocationType: "Video",
            LocationValue: "https://meet.google.com/test-room"));
        createResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var link = await createResp.Content.ReadFromJsonAsync<MeetingLinkDto>();

        link.ShouldNotBeNull();
        link.Title.ShouldBe("30-Min Product Walkthrough");
        link.DurationMinutes.ShouldBe(30);
        link.ClickCount.ShouldBe(0);
        link.BookingCount.ShouldBe(0);
        link.ConversionRate.ShouldBe(0m);
        link.ShareableUrl.ShouldStartWith("/book/m/");

        // 2. Track clicks
        var anonClient = factory.CreateClient();
        var trackResp = await anonClient.PostAsync($"/api/v1/meeting-links/public/{link.Slug}/track-click", null);
        trackResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. Verify click count incremented
        var readResp = await admin.Client.GetAsync($"/api/v1/meeting-links/{link.Id}");
        readResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await readResp.Content.ReadFromJsonAsync<MeetingLinkDto>();
        updated.ShouldNotBeNull();
        updated.ClickCount.ShouldBe(1);
        updated.BookingCount.ShouldBe(0);
        updated.ConversionRate.ShouldBe(0m);
    }

    [Fact]
    public async Task Public_meeting_link_resolution_and_booking_with_contact_autolink()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        // 1. Create Contact
        var contactEmail = $"marcus_{Guid.NewGuid():N}@rome.gov";
        var contactResp = await admin.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Marcus", "Aurelius", contactEmail, "+15558881234"));
        contactResp.EnsureSuccessStatusCode();
        var contact = await contactResp.Content.ReadFromJsonAsync<ContactDto>();
        contact.ShouldNotBeNull();

        // 2. Create Meeting Link tied to Contact
        var linkSlug = $"consult-{Guid.NewGuid():N}"[..18];
        var linkResp = await admin.Client.PostAsJsonAsync("/api/v1/meeting-links", new CreateMeetingLinkRequest(
            Title: "Executive Consultation",
            Slug: linkSlug,
            ContactId: contact.Id,
            CustomerName: "Marcus Aurelius",
            CustomerEmail: contactEmail,
            DurationMinutes: 45));
        linkResp.StatusCode.ShouldBe(HttpStatusCode.Created);
        var link = await linkResp.Content.ReadFromJsonAsync<MeetingLinkDto>();
        link.ShouldNotBeNull();

        // 3. Anonymous public resolves meeting link
        var anonClient = factory.CreateClient();
        var pubResp = await anonClient.GetAsync($"/api/v1/meeting-links/public/{linkSlug}");
        pubResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pubLink = await pubResp.Content.ReadFromJsonAsync<PublicMeetingLinkDto>();

        pubLink.ShouldNotBeNull();
        pubLink.Title.ShouldBe("Executive Consultation");
        pubLink.DurationMinutes.ShouldBe(45);
        pubLink.CustomerEmail.ShouldBe(contactEmail.ToLowerInvariant());
        pubLink.IsAvailable.ShouldBeTrue();

        // Track click
        await anonClient.PostAsync($"/api/v1/meeting-links/public/{linkSlug}/track-click", null);

        // 4. Anonymous public books the meeting
        var bookTime = DateTime.UtcNow.Date.AddDays(2).AddHours(14); // 2pm UTC
        var bookResp = await anonClient.PostAsJsonAsync($"/api/v1/meeting-links/public/{linkSlug}/book", new BookThroughMeetingLinkRequest(
            StartAtUtc: bookTime,
            CustomerName: "Marcus Aurelius",
            CustomerEmail: contactEmail,
            CustomerPhone: "+15558881234",
            Notes: "Discuss enterprise license roll-out"));
        bookResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var confirmation = await bookResp.Content.ReadFromJsonAsync<BookingConfirmationDto>();

        confirmation.ShouldNotBeNull();
        confirmation.PageName.ShouldBe("Executive Consultation");
        confirmation.StartUtc.ShouldBe(bookTime);
        confirmation.EndUtc.ShouldBe(bookTime.AddMinutes(45));

        // 5. Rep reads updated link metrics (1 click, 1 booking = 100% conversion)
        var repLinkResp = await admin.Client.GetAsync($"/api/v1/meeting-links/{link.Id}");
        var repLink = await repLinkResp.Content.ReadFromJsonAsync<MeetingLinkDto>();
        repLink.ShouldNotBeNull();
        repLink.ClickCount.ShouldBe(1);
        repLink.BookingCount.ShouldBe(1);
        repLink.ConversionRate.ShouldBe(100m);
    }

    [Fact]
    public async Task Single_use_meeting_link_enforcement()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var slug = $"single-{Guid.NewGuid():N}"[..18];
        var linkResp = await admin.Client.PostAsJsonAsync("/api/v1/meeting-links", new CreateMeetingLinkRequest(
            Title: "VIP Deal Review",
            Slug: slug,
            IsSingleUse: true,
            DurationMinutes: 30));
        linkResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var anonClient = factory.CreateClient();

        // First booking succeeds
        var firstBook = await anonClient.PostAsJsonAsync($"/api/v1/meeting-links/public/{slug}/book", new BookThroughMeetingLinkRequest(
            StartAtUtc: DateTime.UtcNow.Date.AddDays(1).AddHours(10),
            CustomerName: "VIP Buyer",
            CustomerEmail: "buyer@vip.org"));
        firstBook.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Second booking attempt on single-use link fails
        var secondBook = await anonClient.PostAsJsonAsync($"/api/v1/meeting-links/public/{slug}/book", new BookThroughMeetingLinkRequest(
            StartAtUtc: DateTime.UtcNow.Date.AddDays(1).AddHours(11),
            CustomerName: "Second Person",
            CustomerEmail: "second@vip.org"));
        secondBook.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Expired_meeting_link_rejection()
    {
        var admin = await Workspaces.SignupAsync(factory);
        (await admin.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var slug = $"expired-{Guid.NewGuid():N}"[..18];
        var linkResp = await admin.Client.PostAsJsonAsync("/api/v1/meeting-links", new CreateMeetingLinkRequest(
            Title: "Limited Window Strategy",
            Slug: slug,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(-10),
            DurationMinutes: 15));
        linkResp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var anonClient = factory.CreateClient();

        // Booking fails because link has expired
        var bookResp = await anonClient.PostAsJsonAsync($"/api/v1/meeting-links/public/{slug}/book", new BookThroughMeetingLinkRequest(
            StartAtUtc: DateTime.UtcNow.Date.AddDays(1).AddHours(12),
            CustomerName: "Late Customer",
            CustomerEmail: "late@customer.com"));
        bookResp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_meeting_links_access()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        var tenantB = await Workspaces.SignupAsync(factory);

        (await tenantA.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();
        (await tenantB.Client.PutAsJsonAsync("/api/v1/features/meeting-links", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

        var slugA = $"link-a-{Guid.NewGuid():N}"[..18];
        var linkResp = await tenantA.Client.PostAsJsonAsync("/api/v1/meeting-links", new CreateMeetingLinkRequest(
            Title: "Tenant A Strategy",
            Slug: slugA));
        linkResp.EnsureSuccessStatusCode();
        var linkA = await linkResp.Content.ReadFromJsonAsync<MeetingLinkDto>();
        linkA.ShouldNotBeNull();

        // Tenant B queries meeting links -> should not contain Tenant A's link
        var listB = await tenantB.Client.GetFromJsonAsync<List<MeetingLinkDto>>("/api/v1/meeting-links");
        listB.ShouldNotBeNull();
        listB.ShouldNotContain(l => l.Id == linkA.Id);

        // Tenant B attempts to get Tenant A's link by ID
        var crossGet = await tenantB.Client.GetAsync($"/api/v1/meeting-links/{linkA.Id}");
        crossGet.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to update Tenant A's link
        var crossUpdate = await tenantB.Client.PutAsJsonAsync($"/api/v1/meeting-links/{linkA.Id}", new UpdateMeetingLinkRequest("Compromised"));
        crossUpdate.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B attempts to delete Tenant A's link
        var crossDelete = await tenantB.Client.DeleteAsync($"/api/v1/meeting-links/{linkA.Id}");
        crossDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
