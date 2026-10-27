using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Files;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Tenancy;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using Shouldly;
using Xunit;

namespace MindLift.IntegrationTests.Security;

/// <summary>
/// The Five Production-Readiness Verification Tests:
/// 1. Concurrency & Race Condition Test (Invitation Acceptance & Atomic Sequences).
/// 2. Large Payload & Resource Exhaustion (Unbounded Pagination Clamping & ReDoS Immunity).
/// 3. File Upload & Path Safety Audit (Path Traversal Stripping & Content Signature Validation).
/// 4. Background Job & Multi-Tenant Context Isolation (TenantContext.Use Disposability & Scoping).
/// 5. Security Headers & Cookie Flags Audit.
/// </summary>
public sealed class ProductionVerificationTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    // =========================================================================
    // 1. Concurrency & Race Condition Test (Double-Spend / State Collision)
    // =========================================================================
    [Fact]
    public async Task Concurrency_ten_simultaneous_invitation_claims_allow_exactly_one_winner()
    {
        var admin = await Workspaces.SignupAsync(factory, "RaceCondition Corp");
        var roles = await admin.Client.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var staffRoleId = roles!.Single(r => r.Name == "Staff").Id;

        var inviteEmail = $"race-{Guid.NewGuid():N}@example.test";
        var inviteResponse = await admin.Client.PostAsJsonAsync("/api/v1/invitations",
            new CreateInvitationRequest(inviteEmail, "Race", "Winner", staffRoleId, null));
        inviteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var token = factory.Emails.LatestTokenFor(inviteEmail);
        token.ShouldNotBeNullOrEmpty();

        // Prepare 10 identical acceptance requests firing at the exact same millisecond
        var client = Workspaces.MobileClient(factory);
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.PostAsJsonAsync("/api/v1/auth/accept-invite",
                new AcceptInvitationRequest(token, Workspaces.Password)))
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Exactly ONE request must succeed (200 OK); the remaining 9 must fail
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        successCount.ShouldBe(1, $"Expected exactly 1 claim to succeed, but {successCount} succeeded.");

        var nonSuccessCount = responses.Count(r => !r.IsSuccessStatusCode);
        nonSuccessCount.ShouldBe(9);

        // Every failure must be a client rejection (4xx: BadRequest, Conflict, Unauthorized, or TooManyRequests)
        foreach (var failure in responses.Where(r => !r.IsSuccessStatusCode))
        {
            failure.StatusCode.ShouldBeOneOf(
                HttpStatusCode.BadRequest,
                HttpStatusCode.Conflict,
                HttpStatusCode.Unauthorized,
                HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task Concurrency_ten_simultaneous_sequence_increments_never_collide()
    {
        var tenantId = Guid.NewGuid();

        // Fire 10 parallel atomic sequence allocations across separate requests/scopes
        var tasks = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                using var tScope = tenant.Use(tenantId);
                var sequences = new NumberSequences(db, tenant);
                return await sequences.NextAsync("quote", () => Task.FromResult(10001L));
            })
            .ToList();

        var results = await Task.WhenAll(tasks);

        // All 10 returned numbers must be completely distinct (no duplicates)
        results.Distinct().Count().ShouldBe(10);
        results.Min().ShouldBeGreaterThanOrEqualTo(10001L);
    }

    // =========================================================================
    // 2. Large Payload & Resource Exhaustion (DoS at App Layer)
    // =========================================================================
    [Fact]
    public async Task Resource_exhaustion_unbounded_pagination_is_strictly_clamped_to_max_pagesize()
    {
        var owner = await Workspaces.SignupAsync(factory, "Pagination Clamp Corp");

        // Attempt 1: PageSize = 1,000,000 (attempting memory exhaustion)
        var hugePageQuery = new ListQuery(null, null, null, null, Page: 1, PageSize: 1_000_000);
        var hugeResponse = await owner.Client.PostAsJsonAsync("/api/v1/leads/query", hugePageQuery);
        hugeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hugeResult = await hugeResponse.Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        hugeResult.ShouldNotBeNull();
        hugeResult.PageSize.ShouldBeLessThanOrEqualTo(200);

        // Attempt 2: Negative PageSize = -50
        var negativePageQuery = new ListQuery(null, null, null, null, Page: 1, PageSize: -50);
        var negResponse = await owner.Client.PostAsJsonAsync("/api/v1/leads/query", negativePageQuery);
        negResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var negResult = await negResponse.Content.ReadFromJsonAsync<PagedResult<LeadListItemDto>>();
        negResult.ShouldNotBeNull();
        negResult.PageSize.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Redos_nested_repetitive_strings_complete_promptly()
    {
        // Test degenerate nested input against email/phone normalization logic
        var degenerateInput = new string('a', 5000) + "@" + new string('b', 5000) + ".com";
        var start = DateTime.UtcNow;

        var normalized = MindLift.Infrastructure.Communication.OptOutService.NormalizeTarget(
            degenerateInput, MindLift.Shared.Contracts.CommunicationChannel.Email);

        var duration = DateTime.UtcNow - start;
        duration.TotalMilliseconds.ShouldBeLessThan(500);
        normalized.ShouldNotBeNull();
    }

    // =========================================================================
    // 3. File Upload & Path Safety Audit
    // =========================================================================
    [Theory]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("..\\..\\Windows\\System32\\calc.exe", "calc.exe")]
    [InlineData("/var/log/secrets.txt", "secrets.txt")]
    [InlineData("C:\\Users\\Administrator\\ntuser.dat", "ntuser.dat")]
    public void File_upload_safefilename_strips_all_directory_traversal_paths(string inputPath, string expectedCleanName)
    {
        var cleaned = FileService.SafeFileName(inputPath);
        cleaned.ShouldBe(expectedCleanName);
        cleaned.ShouldNotContain("/");
        cleaned.ShouldNotContain("\\");
        cleaned.ShouldNotContain("..");
    }

    [Fact]
    public async Task Extension_spoofing_executable_renamed_to_png_is_rejected_by_magic_bytes()
    {
        var owner = await Workspaces.SignupAsync(factory, "Malware Guard Corp");

        // An executable DOS MZ header (0x4D, 0x5A) disguised as .png
        var fakePngBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        using var content = new MultipartFormDataContent();
        using var byteContent = new ByteArrayContent(fakePngBytes);
        content.Add(byteContent, "file", "trojan.png");

        var response = await owner.Client.PostAsync("/api/v1/files", content);

        // Must reject bad file signatures
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("does not match its type");
    }

    // =========================================================================
    // 4. Background Job & Multi-Tenant Context Leaks
    // =========================================================================
    [Fact]
    public void Background_jobs_tenantcontext_use_restores_clean_state_without_leaks()
    {
        var tenantContext = new TenantContext();
        tenantContext.TenantId.ShouldBeNull();
        tenantContext.UserId.ShouldBeNull();

        var tenantA = Guid.NewGuid();
        var userA = Guid.NewGuid();

        var tenantB = Guid.NewGuid();
        var userB = Guid.NewGuid();

        // Scope 1: Tenant A
        using (tenantContext.Use(tenantA, userA))
        {
            tenantContext.TenantId.ShouldBe(tenantA);
            tenantContext.UserId.ShouldBe(userA);

            // Nested Scope 2: Tenant B
            using (tenantContext.Use(tenantB, userB))
            {
                tenantContext.TenantId.ShouldBe(tenantB);
                tenantContext.UserId.ShouldBe(userB);
            }

            // Restored to Tenant A
            tenantContext.TenantId.ShouldBe(tenantA);
            tenantContext.UserId.ShouldBe(userA);
        }

        // Scope cleanly disposed -> Restored to root
        tenantContext.TenantId.ShouldBeNull();
        tenantContext.UserId.ShouldBeNull();
    }

    // =========================================================================
    // 5. Automated Pipeline: Security Headers & Cookie Flags Audit
    // =========================================================================
    [Fact]
    public async Task Pipeline_baseline_security_headers_are_present_on_all_responses()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").First().ShouldBe("nosniff");

        response.Headers.Contains("X-Frame-Options").ShouldBeTrue();
        response.Headers.GetValues("X-Frame-Options").First().ShouldBe("DENY");

        response.Headers.Contains("Referrer-Policy").ShouldBeTrue();
        response.Headers.GetValues("Referrer-Policy").First().ShouldBe("strict-origin-when-cross-origin");

        // Ensure Server banner is not leaking details
        response.Headers.Server.ToString().ShouldNotContain("Kestrel");
    }

    [Fact]
    public async Task Pipeline_auth_cookies_are_httponly_and_samesite_strict()
    {
        var client = factory.CreateClient();
        var email = $"cookietest-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync("/api/v1/auth/signup",
            new SignupRequest("Cookie Corp", "Cookie", "User", email, Workspaces.Password, "+971500000000", "AE", "en", true));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verify Set-Cookie header for refresh token
        var setCookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refreshCookie = setCookies.FirstOrDefault(c => c.StartsWith("ml_refresh="));
        refreshCookie.ShouldNotBeNull();

        // Must be HttpOnly and SameSite=Strict
        refreshCookie.ShouldContain("httponly", Case.Insensitive);
        refreshCookie.ShouldContain("samesite=strict", Case.Insensitive);
    }
}
