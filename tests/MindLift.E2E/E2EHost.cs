using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Playwright;
using MindLift.Shared.Contracts;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace MindLift.E2E;

/// <summary>
/// Runs the real application for browser tests: a throwaway PostgreSQL 16 container, the API (which also serves the Blazor app)
/// started as a separate process on a free port, and one headless Microsoft Edge shared by every test.
/// The tests only run when MINDLIFT_E2E=1, because they need Docker and Edge and take a while.
/// </summary>
public sealed class E2EHost : IAsyncLifetime
{
    public const string EnableVariable = "MINDLIFT_E2E";

    public static bool Enabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";

    private PostgreSqlContainer? _postgres;
    private IContainer? _mailpit;
    private Process? _api;
    private IPlaywright? _playwright;
    private readonly string _logFile = Path.Combine(Path.GetTempPath(), $"mindlift-e2e-api-{Guid.NewGuid():N}.log");

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Address of the Mailpit inbox API.</summary>
    public string MailApi { get; private set; } = string.Empty;
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Where screenshots are saved.</summary>
    public static string ShotsDirectory { get; } = Path.Combine(RepoRoot(), "tests", "MindLift.E2E", "screenshots");

    public async Task InitializeAsync()
    {
        if (!Enabled)
        {
            return;
        }

        _postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();
        await _postgres.StartAsync();

        // A real mail server for tests (Mailpit): the app sends through SMTP and the tests read the inbox over its HTTP API.
        _mailpit = new ContainerBuilder()
            .WithImage("axllent/mailpit:latest")
            .WithPortBinding(1025, true)
            .WithPortBinding(8025, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
            .Build();
        await _mailpit.StartAsync();
        MailApi = $"http://127.0.0.1:{_mailpit.GetMappedPublicPort(8025)}";

        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var root = RepoRoot();

        var start = new ProcessStartInfo("dotnet", $"run --project \"{Path.Combine(root, "src", "MindLift.Api")}\" --no-build --no-launch-profile")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = BaseUrl;
        start.Environment["ConnectionStrings__Default"] = _postgres.GetConnectionString();
        start.Environment["Database__MigrateOnStartup"] = "true";
        start.Environment["Hangfire__WorkerCount"] = "2";
        start.Environment["RateLimiting__PublicPermitsPerMinute"] = "100000";
        start.Environment["App__BaseUrl"] = BaseUrl;
        start.Environment["Storage__LocalPath"] = Path.Combine(Path.GetTempPath(), "mindlift-e2e-files");
        start.Environment["Smtp__Host"] = "127.0.0.1";
        start.Environment["Smtp__Port"] = _mailpit.GetMappedPublicPort(1025).ToString();

        _api = Process.Start(start) ?? throw new InvalidOperationException("The API process did not start.");
        _ = PumpAsync(_api.StandardOutput);
        _ = PumpAsync(_api.StandardError);

        await WaitUntilHealthyAsync();

        // A fresh browser session downloads the whole Blazor app before the first screen appears, which takes more than the default 5 seconds.
        Assertions.SetDefaultExpectTimeout(45_000);
        _playwright = await Playwright.CreateAsync();
        // Edge is installed with Windows, so no browser has to be downloaded.
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_api is { HasExited: false })
        {
            _api.Kill(entireProcessTree: true);
            await _api.WaitForExitAsync();
        }

        try
        {
            foreach (var p in Process.GetProcessesByName("MindLift.Api"))
            {
                p.Kill();
            }
        }
        catch { }

        if (_mailpit is not null)
        {
            await _mailpit.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    /// <summary>A new page at a given screen size with its own cookies and storage, so tests never share a session.</summary>
    public async Task<IPage> NewPageAsync(int width = 1440, int height = 900, string locale = "en-US")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = locale,
            AcceptDownloads = true,
        });
        context.SetDefaultTimeout(45_000);
        return await context.NewPageAsync();
    }

    /// <summary>Creates a workspace through the API, with the general industry applied and setup finished, and returns how to sign in.</summary>
    public async Task<Workspace> NewWorkspaceAsync(bool finishSetup = true, string? company = null, string language = "en")
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.test";
        const string password = "Str0ngPass1";
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        http.DefaultRequestHeaders.Add("X-Client", "mobile");

        var signup = await http.PostAsJsonAsync("/api/v1/auth/signup",
            new SignupRequest(company ?? $"E2E Company {Guid.NewGuid():N}"[..20], "Eva", "Tester", email, password, "+971500000000", "AE", language, true));
        signup.EnsureSuccessStatusCode();
        var tokens = (await signup.Content.ReadFromJsonAsync<TokenResponse>())!;
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        if (finishSetup)
        {
            (await http.PostAsJsonAsync("/api/v1/onboarding/industry", new ApplyIndustryRequest("general"))).EnsureSuccessStatusCode();
            (await http.PostAsync("/api/v1/onboarding/complete", null)).EnsureSuccessStatusCode();
        }

        return new Workspace(email, password, tokens.AccessToken);
    }

    /// <summary>An HTTP client that is signed in as the workspace owner, for setting up data a test needs.</summary>
    public HttpClient ApiFor(Workspace workspace)
    {
        var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", workspace.AccessToken);
        return http;
    }

    /// <summary>Signs in through the real sign-in page.</summary>
    public static async Task LoginAsync(IPage page, Workspace workspace)
    {
        await page.GotoAsync("/login");
        await page.Locator("input[data-testid=login-email]").FillAsync(workspace.Email);
        await page.Locator("input[data-testid=login-password]").FillAsync(workspace.Password);
        await page.Locator("button[data-testid=primary-action]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/login"));
    }

    public string ApiLog => File.Exists(_logFile) ? File.ReadAllText(_logFile) : string.Empty;

    private async Task WaitUntilHealthyAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_api!.HasExited)
            {
                throw new InvalidOperationException("The API stopped while starting. Log:\n" + ApiLog);
            }

            try
            {
                if ((await http.GetAsync("/health")).StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not up yet.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("The API did not become healthy in 90 seconds. Log:\n" + ApiLog);
    }

    private async Task PumpAsync(StreamReader reader)
    {
        // Read the process output so its buffer never fills up and blocks it. Keep it in a file for debugging.
        await using var writer = new StreamWriter(new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        while (await reader.ReadLineAsync() is { } line)
        {
            await writer.WriteLineAsync(line);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MindLift.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find MindLift.slnx above " + AppContext.BaseDirectory);
    }
}

/// <summary>How to sign in to a workspace created for a test.</summary>
public sealed record Workspace(string Email, string Password, string AccessToken);

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EHost>
{
    public const string Name = "E2E";
}

/// <summary>A test that only runs when MINDLIFT_E2E=1.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (!E2EHost.Enabled)
        {
            Skip = $"Browser tests are off. Set {E2EHost.EnableVariable}=1 to run them (needs Docker and Microsoft Edge).";
        }
    }
}

/// <summary>A theory that only runs when MINDLIFT_E2E=1.</summary>
public sealed class E2ETheoryAttribute : TheoryAttribute
{
    public E2ETheoryAttribute()
    {
        if (!E2EHost.Enabled)
        {
            Skip = $"Browser tests are off. Set {E2EHost.EnableVariable}=1 to run them (needs Docker and Microsoft Edge).";
        }
    }
}

/// <summary>One email found in the Mailpit inbox.</summary>
public sealed record InboxMessage(string Id, string Subject, string Text, IReadOnlyList<string> To);

public static class InboxExtensions
{
    /// <summary>Waits for a message to the address whose subject contains the text, and returns it with its plain-text body.</summary>
    public static async Task<InboxMessage> WaitForEmailAsync(this E2EHost host, string to, string subjectContains, int seconds = 20)
    {
        using var http = new HttpClient { BaseAddress = new Uri(host.MailApi) };
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var query = Uri.EscapeDataString($"to:{to}");
            using var list = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/v1/search?query={query}"));
            foreach (var message in list.RootElement.GetProperty("messages").EnumerateArray())
            {
                var subject = message.GetProperty("Subject").GetString() ?? string.Empty;
                if (!subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = message.GetProperty("ID").GetString()!;
                using var full = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"/api/v1/message/{id}"));
                var recipients = full.RootElement.GetProperty("To").EnumerateArray().Select(t => t.GetProperty("Address").GetString()!).ToList();
                return new InboxMessage(id, subject, full.RootElement.GetProperty("Text").GetString() ?? string.Empty, recipients);
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No email to {to} with a subject containing '{subjectContains}' arrived in {seconds} seconds.");
    }

    /// <summary>The first web link in an email body.</summary>
    public static string FirstLink(this InboxMessage message)
        => System.Text.RegularExpressions.Regex.Match(message.Text, @"https?://\S+").Value;
}
