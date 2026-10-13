using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Hosting;

namespace MindLift.Api.Hosting;

/// <summary>
/// Runs the Hangfire worker for this application, using the storage it was given rather than Hangfire's process-wide one (see Program.cs).
/// It starts with the host and stops when the host stops, finishing the jobs already in progress.
/// </summary>
public sealed class HangfireServerHost(JobStorage storage, BackgroundJobServerOptions options) : IHostedService, IDisposable
{
    private BackgroundJobServer? _server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server = new BackgroundJobServer(options, storage);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.SendStop();
        return Task.CompletedTask;
    }

    public void Dispose() => _server?.Dispose();
}
