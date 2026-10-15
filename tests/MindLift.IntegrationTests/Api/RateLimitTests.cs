using System.Net;
using System.Net.Http.Json;
using MindLift.IntegrationTests.Support;
using Shouldly;

namespace MindLift.IntegrationTests.Api;

/// <summary>Anonymous endpoints are limited per client address so sign-in and public forms cannot be hammered.</summary>
public sealed class RateLimitTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Sign_in_attempts_are_limited_per_address()
    {
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:PublicPermitsPerMinute", "3"));
        var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "Wrong-Passw0rd-1" });
            statuses.Add(response.StatusCode);
        }

        statuses.Take(3).ShouldNotContain(HttpStatusCode.TooManyRequests);
        statuses.Skip(3).ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
    }
}
