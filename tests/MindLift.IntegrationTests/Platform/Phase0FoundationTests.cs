using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Platform;
using MindLift.IntegrationTests.Support;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Platform;

/// <summary>Phase 0 foundations: idempotency (F3), credential vault (F4), webhook replay protection (F5) and timeline (F6), each with company isolation.</summary>
public sealed class Phase0FoundationTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private async Task<Guid> NewCompanyAsync()
    {
        var session = await Workspaces.SignupAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.IgnoreQueryFilters().Where(u => u.Email == session.Email).Select(u => u.TenantId).SingleAsync();
    }

    /// <summary>Runs the action inside a fresh scope that belongs to one company, the way a job or webhook does.</summary>
    private async Task<T> AsAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var use = tenant.Use(tenantId);
        return await action(scope.ServiceProvider);
    }

    // ---------- F3 idempotency ----------

    [Fact]
    public async Task The_same_work_can_only_be_started_once()
    {
        var company = await NewCompanyAsync();
        var key = Guid.NewGuid().ToString("N");

        var first = await AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("test-scope", key, TimeSpan.FromHours(1)));
        var second = await AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("test-scope", key, TimeSpan.FromHours(1)));

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    [Fact]
    public async Task Twenty_simultaneous_requests_for_the_same_key_let_exactly_one_through()
    {
        var company = await NewCompanyAsync();
        var key = Guid.NewGuid().ToString("N");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("race", key, TimeSpan.FromHours(1)))));

        results.Count(r => r).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_key_in_another_company_or_scope_is_a_different_piece_of_work()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        var key = Guid.NewGuid().ToString("N");

        (await AsAsync(a, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s1", key, TimeSpan.FromHours(1)))).ShouldBeTrue();
        (await AsAsync(b, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s1", key, TimeSpan.FromHours(1)))).ShouldBeTrue();
        (await AsAsync(a, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s2", key, TimeSpan.FromHours(1)))).ShouldBeTrue();
    }

    [Fact]
    public async Task An_expired_key_can_be_used_again_and_the_cleanup_removes_old_records()
    {
        var company = await NewCompanyAsync();
        var key = Guid.NewGuid().ToString("N");
        (await AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("expiry", key, TimeSpan.FromHours(1)))).ShouldBeTrue();

        // Age the record so it has expired.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<IdempotencyRecord>().IgnoreQueryFilters().Where(r => r.TenantId == company && r.Key == key)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAt, DateTime.UtcNow.AddMinutes(-5)));
        }

        (await AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("expiry", key, TimeSpan.FromHours(1)))).ShouldBeTrue();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var use = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(company);
            var old = Guid.NewGuid().ToString("N");
            db.Set<IdempotencyRecord>().Add(new IdempotencyRecord { TenantId = company, Scope = "old", Key = old, ExpiresAt = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IdempotencyCleanupJob>().RunAsync(CancellationToken.None);
            (await db.Set<IdempotencyRecord>().IgnoreQueryFilters().AnyAsync(r => r.Key == old)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Bad_scope_key_or_lifetime_is_refused_and_there_is_no_work_without_a_company()
    {
        var company = await NewCompanyAsync();

        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("", "k", TimeSpan.FromHours(1))));
        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s", new string('k', 201), TimeSpan.FromHours(1))));
        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s", "k", TimeSpan.Zero)));
        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s", "k", TimeSpan.FromDays(400))));

        using var scope = factory.Services.CreateScope();
        await Should.ThrowAsync<ForbiddenException>(() => scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().TryBeginAsync("s", "k", TimeSpan.FromHours(1)));
    }

    // ---------- F4 credential vault ----------

    [Fact]
    public async Task A_secret_is_stored_encrypted_and_only_the_vault_can_read_it_back()
    {
        var company = await NewCompanyAsync();
        const string secret = "sk_live_super_secret_value_123";

        var info = await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("stripe", "Main", secret, "{\"account\":\"acct_1\"}"));

        info.Provider.ShouldBe("stripe");
        info.SettingsJson.ShouldBe("{\"account\":\"acct_1\"}");
        (await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().GetSecretAsync("stripe", "Main"))).ShouldBe(secret);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Set<IntegrationCredential>().IgnoreQueryFilters().SingleAsync(c => c.Id == info.Id);
        stored.EncryptedSecret.ShouldNotBeNullOrEmpty();
        stored.EncryptedSecret.ShouldNotContain(secret);
        stored.EncryptedSecret.ShouldStartWith("v2:");
    }

    [Fact]
    public async Task One_company_can_never_read_another_companys_secret()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        await AsAsync(a, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("twilio", "Line", "token-of-company-a"));

        (await AsAsync(b, sp => sp.GetRequiredService<ICredentialVault>().GetSecretAsync("twilio", "Line"))).ShouldBeNull();
        (await AsAsync(b, sp => sp.GetRequiredService<ICredentialVault>().ListAsync())).ShouldBeEmpty();
        (await AsAsync(b, sp => sp.GetRequiredService<ICredentialVault>().RevokeAsync("twilio", "Line"))).ShouldBeFalse();
        (await AsAsync(a, sp => sp.GetRequiredService<ICredentialVault>().GetSecretAsync("twilio", "Line"))).ShouldBe("token-of-company-a");
    }

    [Fact]
    public async Task Storing_again_rotates_the_secret_and_revoking_erases_it()
    {
        var company = await NewCompanyAsync();
        var first = await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("whatsapp", "Main", "old-token"));
        first.RotatedAt.ShouldBeNull();

        var second = await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("whatsapp", "Main", "new-token"));
        second.Id.ShouldBe(first.Id);
        second.RotatedAt.ShouldNotBeNull();
        (await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().GetSecretAsync("whatsapp", "Main"))).ShouldBe("new-token");

        (await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().RevokeAsync("whatsapp", "Main"))).ShouldBeTrue();
        (await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().GetSecretAsync("whatsapp", "Main"))).ShouldBeNull();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Set<IntegrationCredential>().IgnoreQueryFilters().SingleAsync(c => c.Id == first.Id);
        row.EncryptedSecret.ShouldBeEmpty();
        row.IsActive.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "Main", "secret", null)]
    [InlineData("Bad Provider!", "Main", "secret", null)]
    [InlineData("stripe", "", "secret", null)]
    [InlineData("stripe", "Main", "", null)]
    [InlineData("stripe", "Main", "secret", "not json")]
    [InlineData("stripe", "Main", "secret", "[1,2]")]
    public async Task Invalid_credentials_are_refused(string provider, string label, string secret, string? settings)
    {
        var company = await NewCompanyAsync();

        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync(provider, label, secret, settings)));
    }

    [Fact]
    public async Task Rotating_a_secret_never_puts_it_or_its_ciphertext_in_the_audit_trail()
    {
        var company = await NewCompanyAsync();
        await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("audit-test", "Main", "first-secret-value"));
        await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("audit-test", "Main", "second-secret-value"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = await db.Set<IntegrationCredential>().IgnoreQueryFilters().Where(c => c.TenantId == company && c.Provider == "audit-test").Select(c => c.EncryptedSecret).SingleAsync();
        var trail = await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == company && a.EntityType == nameof(IntegrationCredential)).ToListAsync();

        trail.ShouldNotBeEmpty();
        var text = string.Join("\n", trail.Select(a => a.Changes));
        text.ShouldContain("EncryptedSecret");
        text.ShouldContain("[redacted]");
        text.ShouldNotContain(cipher);
        text.ShouldNotContain("secret-value");
    }

    [Fact]
    public async Task The_vault_listing_never_contains_a_secret()
    {
        var company = await NewCompanyAsync();
        await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().StoreAsync("stripe", "Main", "listing-secret-xyz"));

        var list = await AsAsync(company, sp => sp.GetRequiredService<ICredentialVault>().ListAsync("stripe"));

        list.ShouldHaveSingleItem();
        System.Text.Json.JsonSerializer.Serialize(list).ShouldNotContain("listing-secret-xyz");
    }

    // ---------- F5 webhook ingress ----------

    [Fact]
    public async Task A_webhook_is_accepted_once_then_treated_as_a_duplicate()
    {
        var company = await NewCompanyAsync();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        (await AsAsync(company, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("twilio", id, now))).ShouldBe(WebhookDecision.Accepted);
        (await AsAsync(company, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("twilio", id, now))).ShouldBe(WebhookDecision.Duplicate);
    }

    [Fact]
    public async Task A_webhook_that_is_too_old_or_from_the_future_is_refused_as_a_possible_replay()
    {
        var company = await NewCompanyAsync();

        (await AsAsync(company, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("stripe", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(-30)))).ShouldBe(WebhookDecision.Stale);
        (await AsAsync(company, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("stripe", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(30)))).ShouldBe(WebhookDecision.Stale);
    }

    [Fact]
    public async Task The_same_event_id_from_two_providers_or_two_companies_does_not_collide()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        (await AsAsync(a, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("twilio", id, now))).ShouldBe(WebhookDecision.Accepted);
        (await AsAsync(a, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("stripe", id, now))).ShouldBe(WebhookDecision.Accepted);
        (await AsAsync(b, sp => sp.GetRequiredService<IWebhookIngress>().AcceptAsync("twilio", id, now))).ShouldBe(WebhookDecision.Accepted);
    }

    [Fact]
    public void A_signature_must_match_the_payload_and_the_secret()
    {
        var payload = Encoding.UTF8.GetBytes("{\"event\":\"call.completed\"}");
        var hex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_1"), payload)).ToLowerInvariant();

        WebhookSignature.VerifyHmacSha256("whsec_1", payload, hex).ShouldBeTrue();
        WebhookSignature.VerifyHmacSha256("whsec_1", payload, "sha256=" + hex).ShouldBeTrue();
        WebhookSignature.VerifyHmacSha256("whsec_1", payload, hex.ToUpperInvariant()).ShouldBeTrue();
        WebhookSignature.VerifyHmacSha256("whsec_2", payload, hex).ShouldBeFalse();
        WebhookSignature.VerifyHmacSha256("whsec_1", Encoding.UTF8.GetBytes("{\"event\":\"tampered\"}"), hex).ShouldBeFalse();
        WebhookSignature.VerifyHmacSha256("whsec_1", payload, null).ShouldBeFalse();
        WebhookSignature.VerifyHmacSha256("whsec_1", payload, "not-hex").ShouldBeFalse();
        WebhookSignature.VerifyHmacSha256("whsec_1", payload, hex[..10]).ShouldBeFalse();
        WebhookSignature.VerifyHmacSha256("", payload, hex).ShouldBeFalse();
    }

    // ---------- F6 timeline ----------

    [Fact]
    public async Task Events_are_listed_for_their_record_newest_first()
    {
        var company = await NewCompanyAsync();
        var record = Guid.NewGuid();
        var start = DateTime.UtcNow.AddHours(-3);

        await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", record, "call.completed", "Call, 4 minutes", "calls", "{\"seconds\":240}", null, start)));
        await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", record, "email.opened", "Opened the proposal", "email-tracking", null, null, start.AddHours(1))));
        await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", Guid.NewGuid(), "call.completed", "Another record", "calls")));

        var list = await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().ListAsync("contacts", record));

        list.Select(e => e.Kind).ShouldBe(["email.opened", "call.completed"]);
        list[1].DetailJson.ShouldBe("{\"seconds\":240}");
    }

    [Fact]
    public async Task One_company_never_sees_another_companys_timeline()
    {
        var a = await NewCompanyAsync();
        var b = await NewCompanyAsync();
        var record = Guid.NewGuid();
        await AsAsync(a, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("deals", record, "payment.received", "Paid 500", "payments")));

        (await AsAsync(b, sp => sp.GetRequiredService<ITimelineEventBus>().ListAsync("deals", record))).ShouldBeEmpty();
        (await AsAsync(a, sp => sp.GetRequiredService<ITimelineEventBus>().ListAsync("deals", record))).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Contacts", "call.completed", "ok", "calls", null)]
    [InlineData("contacts", "Call Completed", "ok", "calls", null)]
    [InlineData("contacts", "call.completed", "", "calls", null)]
    [InlineData("contacts", "call.completed", "ok", "Bad Source", null)]
    [InlineData("contacts", "call.completed", "ok", "calls", "not json")]
    public async Task Malformed_events_are_refused(string entityType, string kind, string summary, string source, string? detail)
    {
        var company = await NewCompanyAsync();

        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput(entityType, Guid.NewGuid(), kind, summary, source, detail))));
    }

    [Fact]
    public async Task An_event_with_no_record_or_too_long_a_summary_is_refused_and_listing_is_capped()
    {
        var company = await NewCompanyAsync();

        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", Guid.Empty, "call.completed", "ok", "calls"))));
        await Should.ThrowAsync<ValidationException>(() => AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", Guid.NewGuid(), "call.completed", new string('x', 301), "calls"))));

        var record = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().PublishAsync(new TimelineEventInput("contacts", record, "note.added", "n", "notes")));
        }

        (await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().ListAsync("contacts", record, take: 2))).Count.ShouldBe(2);
        (await AsAsync(company, sp => sp.GetRequiredService<ITimelineEventBus>().ListAsync("contacts", record, take: 100000))).Count.ShouldBe(3);
    }
}
