using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;
using MindLift.Infrastructure.Tenancy;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using Shouldly;

namespace MindLift.IntegrationTests.Cpq;

/// <summary>Quote and invoice numbers never repeat, and uploads accept only the kinds of file a company really exchanges.</summary>
public sealed class NumberingAndUploadTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    [Fact]
    public async Task Numbers_taken_at_the_same_moment_are_all_different_and_continue_after_existing_records()
    {
        var company = await Workspaces.SignupAsync(factory);
        var tenantId = (await company.Client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me"))!.TenantId;

        var taken = await Task.WhenAll(Enumerable.Range(0, 25).Select(async _ =>
        {
            using var scope = factory.Services.CreateScope();
            using var inTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenantId);
            return await scope.ServiceProvider.GetRequiredService<NumberSequences>().NextAsync(NumberSequences.Invoice, () => Task.FromResult(10001L));
        }));

        taken.Distinct().Count().ShouldBe(25);
        taken.Min().ShouldBe(10001);
        taken.Max().ShouldBe(10025);

        // Another kind of number, and another company, count on their own.
        using var scope2 = factory.Services.CreateScope();
        using var ctx = scope2.ServiceProvider.GetRequiredService<ITenantContext>().Use(tenantId);
        (await scope2.ServiceProvider.GetRequiredService<NumberSequences>().NextAsync(NumberSequences.Quote, () => Task.FromResult(10001L))).ShouldBe(10001);
    }

    private static MultipartFormDataContent Form(string fileName, string contentType)
    {
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Theory]
    [InlineData("page.html", "text/html")]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("script.js", "application/javascript")]
    [InlineData("noextension", "application/octet-stream")]
    public async Task A_file_that_could_run_in_a_browser_is_refused(string name, string type)
    {
        var company = await Workspaces.SignupAsync(factory);
        var response = await company.Client.PostAsync("/api/v1/documents/upload", Form(name, type));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }
}
