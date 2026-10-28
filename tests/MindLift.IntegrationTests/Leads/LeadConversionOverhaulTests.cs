using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Leads;

/// <summary>
/// Phase 1 / #1: Lead Conversion Overhaul (lead-conversion).
/// Verifies convert options dialogue data (matching accounts, contacts, pipelines, base currency),
/// atomic lead conversion with ContactAccountLink creation, re-parenting of child messages & bookings,
/// timeline events (lead_converted, contact_created, deal_created), and cross-tenant isolation.
/// </summary>
public sealed class LeadConversionOverhaulTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static async Task EnableFeatureAsync(Session s, string feature)
        => (await s.Client.PutAsJsonAsync($"/api/v1/features/{feature}", new SetFeatureRequest(true))).EnsureSuccessStatusCode();

    private static SaveLeadRequest NewLead(string first, string last, string email, string? phone = null, string? company = null)
        => new(first, last, email, phone, company, "new", "website", null, null, null, null, null);

    private static SaveContactRequest NewContact(string first, string last, string email, string? phone = null, string? title = null, Guid? accountId = null)
        => new(first, last, email, phone, title, null, accountId, null, null, null);

    private static SaveAccountRequest NewAccount(string name, string? phone = null, string? website = null)
        => new(name, website, phone, "Technology", "A test account", null, null, null);

    [Fact]
    public async Task Feature_flag_off_blocks_convert_options_endpoint_while_standard_convert_works()
    {
        var s = await Workspaces.SignupAsync(factory);

        var lead = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Jane", "Doe", "jane.doe@example.test", "+1112223333", "Acme Corp"))).Content.ReadFromJsonAsync<LeadDto>();

        // GET /convert-options blocked by RequiresFeature (answers 404 to hide disabled feature)
        var optionsRes = await s.Client.GetAsync($"/api/v1/leads/{lead!.Id}/convert-options");
        optionsRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Standard convert still succeeds with flag OFF
        var convertRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead.Id}/convert", new ConvertLeadRequest(
            "create", null, "Acme Corp",
            "create", null,
            false, null, null, null, null, null, null));
        convertRes.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Feature_flag_on_returns_recommended_options()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "lead-conversion");
        await s.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Sales", null, true, true, 1));

        // Seed an existing account and contact
        var account = await (await s.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Apex Global", "+1234567890", "https://apex.test"))).Content.ReadFromJsonAsync<AccountDto>();
        var contact = await (await s.Client.PostAsJsonAsync("/api/v1/contacts", NewContact("Alice", "Apex", "alice@apex.test", "+1987654321"))).Content.ReadFromJsonAsync<ContactDto>();

        // Create lead with matching company and contact info
        var lead = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Alice", "Apex", "alice@apex.test", "+1987654321", "Apex Global"))).Content.ReadFromJsonAsync<LeadDto>();

        var optionsRes = await s.Client.GetAsync($"/api/v1/leads/{lead!.Id}/convert-options");
        optionsRes.StatusCode.ShouldBe(HttpStatusCode.OK);

        var options = await optionsRes.Content.ReadFromJsonAsync<LeadConvertOptionsDto>();
        options.ShouldNotBeNull();
        options.LeadId.ShouldBe(lead.Id);
        options.LeadName.ShouldBe("Alice Apex");
        options.CompanyName.ShouldBe("Apex Global");
        options.Email.ShouldBe("alice@apex.test");
        options.Phone.ShouldBe("+1987654321");

        // Verify matching account is recommended
        options.MatchingAccounts.ShouldContain(a => a.Id == account!.Id && a.Title == "Apex Global");

        // Verify matching contact is recommended
        options.MatchingContacts.ShouldContain(c => c.Id == contact!.Id && c.Title == "Alice Apex");

        // Verify pipelines and base currency
        options.AvailablePipelines.ShouldNotBeEmpty();
        options.AvailablePipelines.First().Stages.ShouldNotBeEmpty();
        options.BaseCurrency.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Lead_conversion_links_contact_account_and_reparents_messages_and_bookings()
    {
        var s = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(s, "lead-conversion");
        await s.Client.PostAsJsonAsync("/api/v1/settings/pipelines", new SavePipelineRequest("Standard", null, true, true, 1));

        var lead = await (await s.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Daniel", "Ocean", "daniel.ocean@bellagio.test", "+17025550100", "Bellagio Vaults"))).Content.ReadFromJsonAsync<LeadDto>();

        // Seed a message and a booking for this lead
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
            using var use = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId);

            db.Messages.Add(new Message
            {
                Channel = CommunicationChannel.Email,
                Direction = MessageDirection.Inbound,
                Status = MessageStatus.Delivered,
                From = "daniel.ocean@bellagio.test",
                To = "sales@bellagio.test",
                Subject = "Inquiry regarding vaults",
                Body = "Can we schedule a consultation?",
                LeadId = lead!.Id
            });

            db.Bookings.Add(new Booking
            {
                LeadId = lead.Id,
                UserId = me.UserId,
                Name = "Daniel Ocean",
                Email = "daniel.ocean@bellagio.test",
                StartAt = DateTime.UtcNow.AddDays(2),
                EndAt = DateTime.UtcNow.AddDays(2).AddHours(1),
                Status = "Confirmed"
            });

            await db.SaveChangesAsync();
        }

        // Convert the lead into an Account, Contact, and Deal
        var convertRes = await s.Client.PostAsJsonAsync($"/api/v1/leads/{lead!.Id}/convert", new ConvertLeadRequest(
            "create", null, "Bellagio Vaults",
            "create", null,
            true, "High Roller Vault", null, null, 150000m, null, DateTime.UtcNow.AddDays(60)));
        convertRes.StatusCode.ShouldBe(HttpStatusCode.OK, await convertRes.Content.ReadAsStringAsync());

        var result = await convertRes.Content.ReadFromJsonAsync<ConvertLeadResult>();
        result.ShouldNotBeNull();
        result.ContactId.ShouldNotBe(Guid.Empty);
        result.AccountId.ShouldNotBeNull();
        result.DealId.ShouldNotBeNull();

        // Verify DB side-effects in single atomic transaction
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var me = await s.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
            using var use = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me!.TenantId);

            // ContactAccountLink exists
            var link = await db.ContactAccountLinks
                .FirstOrDefaultAsync(l => l.ContactId == result.ContactId && l.AccountId == result.AccountId.Value);
            link.ShouldNotBeNull();

            // Message is re-parented to contact, account, and deal
            var msg = await db.Messages.FirstOrDefaultAsync(m => m.LeadId == lead.Id);
            msg.ShouldNotBeNull();
            msg.ContactId.ShouldBe(result.ContactId);
            msg.AccountId.ShouldBe(result.AccountId);
            msg.DealId.ShouldBe(result.DealId);

            // Booking is re-parented to contact
            var booking = await db.Bookings.FirstOrDefaultAsync(b => b.LeadId == lead.Id);
            booking.ShouldNotBeNull();
            booking.ContactId.ShouldBe(result.ContactId);

            // Timeline events published
            var timelineBus = scope.ServiceProvider.GetRequiredService<ITimelineEventBus>();
            var leadEvents = await timelineBus.ListAsync("lead", lead.Id);
            leadEvents.ShouldContain(e => e.Kind == "lead_converted");

            var contactEvents = await timelineBus.ListAsync("contact", result.ContactId);
            contactEvents.ShouldContain(e => e.Kind == "contact_created");

            var dealEvents = await timelineBus.ListAsync("deal", result.DealId.Value);
            dealEvents.ShouldContain(e => e.Kind == "deal_created");
        }
    }

    [Fact]
    public async Task Cross_tenant_lead_conversion_isolation_is_enforced()
    {
        var tenantA = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantA, "lead-conversion");

        var tenantB = await Workspaces.SignupAsync(factory);
        await EnableFeatureAsync(tenantB, "lead-conversion");

        var leadA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Tenant", "Alpha", "alpha@tenantA.test", company: "Alpha Org"))).Content.ReadFromJsonAsync<LeadDto>();
        var accountA = await (await tenantA.Client.PostAsJsonAsync("/api/v1/accounts", NewAccount("Alpha Org Corp"))).Content.ReadFromJsonAsync<AccountDto>();

        var leadB = await (await tenantB.Client.PostAsJsonAsync("/api/v1/leads", NewLead("Tenant", "Beta", "beta@tenantB.test", company: "Beta Org"))).Content.ReadFromJsonAsync<LeadDto>();

        // Tenant B cannot view Tenant A's convert options (404)
        var crossOptionsRes = await tenantB.Client.GetAsync($"/api/v1/leads/{leadA!.Id}/convert-options");
        crossOptionsRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B cannot convert Tenant A's lead (404)
        var crossConvertRes = await tenantB.Client.PostAsJsonAsync($"/api/v1/leads/{leadA.Id}/convert", new ConvertLeadRequest(
            "create", null, "Alpha Org",
            "create", null,
            false, null, null, null, null, null, null));
        crossConvertRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Tenant B cannot link Tenant A's account during conversion
        var crossLinkRes = await tenantB.Client.PostAsJsonAsync($"/api/v1/leads/{leadB!.Id}/convert", new ConvertLeadRequest(
            "existing", accountA!.Id, null,
            "create", null,
            false, null, null, null, null, null, null));
        crossLinkRes.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
