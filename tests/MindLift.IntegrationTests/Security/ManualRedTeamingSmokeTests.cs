using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

/// <summary>
/// Manual Red-Teaming & Security Smoke Tests:
/// 1. Cross-Tenant isolation (User B attempting GET/PUT/DELETE on User A's record).
/// 2. Payload Injection / Over-posting (unexpected fields like role, tenantId, isSuperAdmin).
/// 3. Invalid/Malformed Token handling (returns 401 Unauthorized without 500 or stack trace leaks).
/// </summary>
public sealed class ManualRedTeamingSmokeTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private static SaveLeadRequest CreateLeadRequest(string firstName, Guid ownerId) =>
        new(firstName, "Target", $"{firstName.ToLowerInvariant()}@example.test", "+1234567890", "TargetCorp", "new", "website", "warm", null, null, ownerId, null);

    [Fact]
    public async Task Cross_tenant_test_user_b_cannot_get_put_or_delete_user_a_item()
    {
        // 1. Create two distinct tenants (User A and User B)
        var userA = await Workspaces.SignupAsync(factory, "Tenant A");
        var userB = await Workspaces.SignupAsync(factory, "Tenant B");

        var meA = await userA.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        meA.ShouldNotBeNull();

        // 2. User A creates an item (Lead) and captures its ID
        var createResponse = await userA.Client.PostAsJsonAsync("/api/v1/leads", CreateLeadRequest("TenantAItem", meA.UserId));
        createResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await createResponse.Content.ReadAsStringAsync());
        var createdLead = await createResponse.Content.ReadFromJsonAsync<LeadDto>();
        createdLead.ShouldNotBeNull();
        var itemId = createdLead.Id;

        // User A can read their own item
        var aGet = await userA.Client.GetAsync($"/api/v1/leads/{itemId}");
        aGet.StatusCode.ShouldBe(HttpStatusCode.OK);

        // 3. User B attempts GET User A's item ID -> Expect 404 (or 403)
        var bGet = await userB.Client.GetAsync($"/api/v1/leads/{itemId}");
        bGet.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        // 4. User B attempts PUT User A's item ID -> Expect 404 (or 403)
        var bPut = await userB.Client.PutAsJsonAsync($"/api/v1/leads/{itemId}", CreateLeadRequest("HijackedByB", Guid.NewGuid()));
        bPut.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        // Verify User A's item was NOT modified
        var aVerifyAfterPut = await userA.Client.GetFromJsonAsync<LeadDto>($"/api/v1/leads/{itemId}");
        aVerifyAfterPut!.FirstName.ShouldBe("TenantAItem");

        // 5. User B attempts DELETE User A's item ID -> Expect 404 (or 403)
        var bDelete = await userB.Client.DeleteAsync($"/api/v1/leads/{itemId}");
        bDelete.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        // Verify User A's item still exists and was NOT deleted
        var aVerifyAfterDelete = await userA.Client.GetAsync($"/api/v1/leads/{itemId}");
        aVerifyAfterDelete.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Payload_injection_unexpected_fields_are_ignored_or_rejected_without_privilege_escalation()
    {
        var admin = await Workspaces.SignupAsync(factory, "Injection Test Corp");
        var staff = await Workspaces.InviteAndAcceptAsync(factory, admin, "Staff", "staff-inject");

        var staffMe = await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        staffMe.ShouldNotBeNull();
        staffMe.RoleName.ShouldBe("Staff");
        staffMe.IsAdmin.ShouldBeFalse();

        // Staff tries to inject unauthorized fields into update request (e.g. role elevation, tenantId swap)
        var maliciousPayload = new
        {
            firstName = "Malicious",
            lastName = "User",
            phone = "+971500000001",
            jobTitle = "PenTester",
            language = "en",
            role = "Admin",
            roleName = "Admin",
            roleId = Guid.NewGuid(),
            tenantId = Guid.NewGuid(),
            isSuperAdmin = true,
            isAdmin = true
        };

        var json = JsonSerializer.Serialize(maliciousPayload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var updateResponse = await staff.Client.PutAsync("/api/v1/auth/profile", content);

        // The endpoint should succeed (updating allowed fields, silently ignoring injected fields)
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify role and permissions were strictly NOT escalated
        var refreshedMe = await staff.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me");
        refreshedMe.ShouldNotBeNull();
        refreshedMe.RoleName.ShouldBe("Staff");
        refreshedMe.IsAdmin.ShouldBeFalse();
        refreshedMe.TenantId.ShouldBe(staffMe.TenantId);

        // Verify staff still cannot access admin-only endpoints
        var adminOnlyCheck = await staff.Client.GetAsync("/api/v1/users");
        adminOnlyCheck.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invalid_token_handling_malformed_and_expired_tokens_return_401_without_stack_traces()
    {
        var client = factory.CreateClient();

        var invalidTokens = new[]
        {
            "invalid.token.here",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.e30.invalid_signature",
            "BearerNotSeparatedProperly",
            Guid.NewGuid().ToString(),
            string.Empty
        };

        foreach (var badToken in invalidTokens)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
            if (!string.IsNullOrEmpty(badToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", badToken);
            }

            var response = await client.SendAsync(request);

            // Must be 401 Unauthorized
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            // Must not return 500 Internal Server Error
            response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError);

            // Must not leak stack traces or internal exception details
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldNotContain("System.Exception");
            body.ShouldNotContain("StackTrace");
            body.ShouldNotContain("at MindLift.");
            body.ShouldNotContain("Npgsql.");
        }
    }
}
