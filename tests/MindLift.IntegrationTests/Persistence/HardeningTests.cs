using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using Shouldly;

namespace MindLift.IntegrationTests.Persistence;

/// <summary>Production hardening: sessions end when they should, large bulk changes run in the background, old history is cleared.</summary>
public sealed class HardeningTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private HttpClient WithToken(string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task A_token_stops_working_as_soon_as_the_password_is_changed()
    {
        var session = await Workspaces.SignupAsync(factory);
        (await WithToken(session.AccessToken).GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await session.Client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(Workspaces.Password, "N3wPassword!x"));
        change.IsSuccessStatusCode.ShouldBeTrue(await change.Content.ReadAsStringAsync());

        var old = await WithToken(session.AccessToken).GetAsync("/api/v1/leads?page=1&pageSize=1");
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await old.Content.ReadAsStringAsync()).ShouldContain("session_revoked");

        // A fresh sign-in works.
        var again = await Workspaces.LoginAsync(factory, session.Email, "N3wPassword!x");
        (await again.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deactivated_person_is_shut_out_on_the_next_request_not_when_the_token_expires()
    {
        var session = await Workspaces.SignupAsync(factory);
        var me = (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var inTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me.TenantId, me.UserId);
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == me.UserId);
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_bulk_change_of_many_records_runs_in_the_background_and_reports_back()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var ids = new List<Guid>();
        for (var i = 0; i < MindLift.Infrastructure.Common.BulkQueue.InlineLimit + 5; i++)
        {
            var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest($"Bulk{i}", "Person", null, null, null, null, null, null, null, null, null, null)))
                .Content.ReadFromJsonAsync<LeadDto>();
            ids.Add(lead!.Id);
        }

        var response = await admin.Client.PostAsJsonAsync("/api/v1/leads/bulk", new BulkActionRequest(BulkActions.AddTags, ids, null, Tags: ["bulk-bg"]));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = (await response.Content.ReadFromJsonAsync<BulkActionResult>())!;
        result.Queued.ShouldBeTrue();
        result.JobId.ShouldNotBeNullOrEmpty();
        result.Requested.ShouldBe(ids.Count);

        // The test host runs background jobs straight away, so the change is already made and the person has been told.
        var tagged = await (await admin.Client.PostAsJsonAsync("/api/v1/leads/query", new ListQuery("Bulk", null, null, null, 1, 200)))
            .Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        tagged!.Total.ShouldBe(ids.Count);
        var notes = await admin.Client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/v1/notifications?page=1&pageSize=20");
        notes!.Items.ShouldContain(n => n.Type == NotificationTypes.BulkCompleted);
    }

    [Fact]
    public async Task A_small_bulk_change_still_answers_at_once()
    {
        var admin = await Workspaces.SignupAsync(factory);
        var lead = await (await admin.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Small", "Bulk", null, null, null, null, null, null, null, null, null, null)))
            .Content.ReadFromJsonAsync<LeadDto>();
        var result = await (await admin.Client.PostAsJsonAsync("/api/v1/leads/bulk", new BulkActionRequest(BulkActions.AddTags, [lead!.Id], null, Tags: ["now"])))
            .Content.ReadFromJsonAsync<BulkActionResult>();
        result!.Queued.ShouldBeFalse();
        result.Succeeded.ShouldBe(1);
    }

    [Fact]
    public async Task Record_history_older_than_the_retention_period_is_removed_and_newer_history_is_kept()
    {
        var session = await Workspaces.SignupAsync(factory);
        var me = (await session.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!;
        var old = Guid.CreateVersion7();
        var recent = Guid.CreateVersion7();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var inTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(me.TenantId, me.UserId);
            db.AuditLogs.Add(new AuditLog { Id = old, TenantId = me.TenantId, EntityType = "Lead", EntityId = Guid.NewGuid(), Action = "Update", OccurredAt = DateTime.UtcNow.AddDays(-(AuditRetentionJob.DefaultRetentionDays + 5)) });
            db.AuditLogs.Add(new AuditLog { Id = recent, TenantId = me.TenantId, EntityType = "Lead", EntityId = Guid.NewGuid(), Action = "Update", OccurredAt = DateTime.UtcNow.AddDays(-3) });
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<AuditRetentionJob>().RunAsync(CancellationToken.None)).ShouldBeGreaterThanOrEqualTo(1);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Id == old)).ShouldBeFalse();
            (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Id == recent)).ShouldBeTrue();
        }
    }
}
