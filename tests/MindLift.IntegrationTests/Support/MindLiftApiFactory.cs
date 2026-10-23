using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MindLift.Application.Abstractions;

namespace MindLift.IntegrationTests.Support;

/// <summary>An email captured by the test host instead of being sent.</summary>
public sealed record SentEmail(string To, string Subject, string Body, IReadOnlyList<EmailAttachment>? Attachments = null);

/// <summary>Records emails so tests can read invitation and reset links.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<SentEmail> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string textBody, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(new SentEmail(to, subject, textBody));
        return Task.CompletedTask;
    }

    public Task SendAsync(string to, string subject, string textBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken = default)
    {
        Sent.Enqueue(new SentEmail(to, subject, textBody, attachments));
        return Task.CompletedTask;
    }

    /// <summary>The most recent token=... value sent to an address.</summary>
    public string LatestTokenFor(string to)
    {
        var message = Sent.Reverse().First(m => m.To.Equals(to, StringComparison.OrdinalIgnoreCase));
        var match = System.Text.RegularExpressions.Regex.Match(message.Body, @"token=([^\s&]+)");
        return Uri.UnescapeDataString(match.Groups[1].Value);
    }
}

/// <summary>
/// Hosts the real API against the shared PostgreSQL container, in Development mode with migrations applied on startup.
/// Emails are recorded instead of sent.
/// </summary>
public sealed class MindLiftApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The token the tests register their WhatsApp webhook with. It only exists in the test host.</summary>
    public const string WhatsAppVerifyToken = "test-only-verify-token";

    /// <summary>A fresh database for this host. Created on first use, so the factory can be constructed synchronously.</summary>
    private readonly Lazy<string> _connectionString = new(() => SharedPostgres.NewDatabaseConnectionStringAsync().GetAwaiter().GetResult());

    public RecordingEmailSender Emails => Services.GetRequiredService<RecordingEmailSender>();

    public ScriptedLlm Llm => Services.GetRequiredService<ScriptedLlm>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _connectionString.Value);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Hangfire:WorkerCount", "2");
        // Many tests sign in from the one test address, so the limits are lifted here. RateLimitTests sets a low limit of its own.
        builder.UseSetting("RateLimiting:PublicPermitsPerMinute", "100000");
        builder.UseSetting("RateLimiting:WebhookPermitsPerMinute", "100000");
        builder.UseSetting("Communication:WhatsAppVerifyToken", WhatsAppVerifyToken);
        builder.UseSetting("Storage:LocalPath", Path.Combine(Path.GetTempPath(), "mindlift-test-files"));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<RecordingEmailSender>();
            services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<RecordingEmailSender>());
            services.AddSingleton<Hangfire.IBackgroundJobClient, InlineJobClient>();

            // The language model is a script a test controls, never the real Ollama.
            services.AddSingleton<ScriptedLlm>();
            services.AddSingleton<MindLift.Infrastructure.Documents.Ai.ILlmClient>(sp => sp.GetRequiredService<ScriptedLlm>());
        });
    }
}
