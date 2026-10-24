using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Infrastructure.Messaging;
using MindLift.IntegrationTests.Support;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using Shouldly;

namespace MindLift.IntegrationTests.Communication;

/// <summary>Automated mail respects unsubscribes and a daily limit, carries an unsubscribe link, and a company can prove its sending domain.</summary>
public sealed class EmailProtectionTests(MindLiftApiFactory factory) : IClassFixture<MindLiftApiFactory>
{
    private sealed class FakeDns(Dictionary<string, string[]> records) : IDnsTxtLookup
    {
        public Task<IReadOnlyList<string>> TxtAsync(string name, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(records.TryGetValue(name, out var values) ? values : []);
    }

    private RecordingEmailSender Mail => factory.Services.GetRequiredService<RecordingEmailSender>();

    private static async Task EmailRuleAsync(Session owner)
    {
        var rule = new SaveAutomationRuleRequest("Welcome mail", "leads", AutomationTriggerType.RecordCreated, null, null,
            [new AutomationActionDefinition(AutomationActionType.SendEmail, Subject: "Welcome", EmailBody: "Thanks for your interest.")], true, 1);
        (await owner.Client.PostAsJsonAsync("/api/v1/automation/rules", rule)).EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> LeadAsync(Session owner, string email)
        => owner.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Mail", "Person", email, null, null, null, null, null, null, null, null, null));

    [Fact]
    public async Task Automated_mail_carries_an_unsubscribe_link_and_once_used_that_address_gets_no_more_mail()
    {
        var owner = await Workspaces.SignupAsync(factory);
        await EmailRuleAsync(owner);
        var address = $"reader-{Guid.NewGuid():N}@example.test";

        (await LeadAsync(owner, address)).EnsureSuccessStatusCode();
        var first = Mail.Sent.Last(m => m.To == address);
        var link = Regex.Match(first.Body, @"/unsubscribe\?t=(\S+)").Groups[1].Value;
        link.ShouldNotBeNullOrEmpty();

        // Opening the link only asks; pressing the button unsubscribes.
        var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/unsubscribe?t={link}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = await anonymous.PostAsync($"/unsubscribe?t={link}", null);
        done.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await done.Content.ReadAsStringAsync()).ShouldContain("unsubscribed");

        var before = Mail.Sent.Count(m => m.To == address);
        (await LeadAsync(owner, address)).EnsureSuccessStatusCode();
        Mail.Sent.Count(m => m.To == address).ShouldBe(before);

        var settings = (await owner.Client.GetFromJsonAsync<EmailSendingDto>("/api/v1/settings/email-sending"))!;
        settings.Suppressed.ShouldContain(s => s.Email == address && s.Reason == "unsubscribed");

        // A tampered link does nothing.
        (await anonymous.PostAsync("/unsubscribe?t=not-a-real-token", null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_company_cannot_send_more_automated_mail_than_its_daily_limit()
    {
        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Email:DailyLimitPerCompany", "2"));
        var owner = await Workspaces.SignupAsync(limited);
        await EmailRuleAsync(owner);
        var mail = limited.Services.GetRequiredService<RecordingEmailSender>();

        var addresses = Enumerable.Range(0, 4).Select(i => $"cap{i}-{Guid.NewGuid():N}@example.test").ToList();
        foreach (var a in addresses)
        {
            (await owner.Client.PostAsJsonAsync("/api/v1/leads", new SaveLeadRequest("Cap", "Person", a, null, null, null, null, null, null, null, null, null))).EnsureSuccessStatusCode();
        }

        addresses.Count(a => mail.Sent.Any(m => m.To == a)).ShouldBe(2);
        var settings = (await owner.Client.GetFromJsonAsync<EmailSendingDto>("/api/v1/settings/email-sending"))!;
        settings.DailyLimit.ShouldBe(2);
        settings.SentToday.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_company_adds_its_domain_and_the_check_finds_the_records_it_was_told_to_add()
    {
        var records = new Dictionary<string, string[]>();
        using var host = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Email:SpfInclude", "mail.example-provider.com");
            b.ConfigureTestServices(s => s.AddSingleton<IDnsTxtLookup>(new FakeDns(records)));
        });
        var owner = await Workspaces.SignupAsync(host);
        var domain = $"acme{Guid.NewGuid():N}"[..14] + ".com";

        var added = (await (await owner.Client.PostAsJsonAsync("/api/v1/settings/email-sending/domains", new AddSendingDomainRequest(domain))).Content.ReadFromJsonAsync<SendingDomainDto>())!;
        added.Verified.ShouldBeFalse();
        added.Records.Count.ShouldBe(3);

        var nothing = (await (await owner.Client.PostAsync($"/api/v1/settings/email-sending/domains/{added.Id}/check", null)).Content.ReadFromJsonAsync<SendingDomainDto>())!;
        nothing.Verified.ShouldBeFalse();
        nothing.LastCheckMessage!.ShouldContain("Not found yet");

        records[domain] = [added.Records[0].Value, "v=spf1 include:mail.example-provider.com ~all"];
        records[$"_dmarc.{domain}"] = ["v=DMARC1; p=none"];
        var ok = (await (await owner.Client.PostAsync($"/api/v1/settings/email-sending/domains/{added.Id}/check", null)).Content.ReadFromJsonAsync<SendingDomainDto>())!;
        ok.Verified.ShouldBeTrue();

        (await owner.Client.PostAsJsonAsync("/api/v1/settings/email-sending/domains", new AddSendingDomainRequest("http://bad"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.Client.PostAsJsonAsync("/api/v1/settings/email-sending/domains", new AddSendingDomainRequest(domain))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
