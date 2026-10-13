using System.Reflection;
using Hangfire.Common;
using Hangfire.States;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace MindLift.IntegrationTests.Support;

/// <summary>
/// Test-only job client: runs each enqueued job right away in its own scope instead of handing it to the Hangfire worker.
/// Under heavy parallel load the worker does not always pick jobs up within a test's patience, so the tests that check an email was sent use this.
/// Production always uses the queue. The Hangfire worker itself is still started by the test host.
/// </summary>
public sealed class InlineJobClient(IServiceScopeFactory scopes) : IBackgroundJobClient
{
    public string Create(Job job, IState state)
    {
        using var scope = scopes.CreateScope();
        var instance = job.Method.IsStatic ? null : scope.ServiceProvider.GetRequiredService(job.Method.DeclaringType!);
        var args = job.Args.Select(a => a is CancellationToken ? CancellationToken.None : a).ToArray();
        var result = job.Method.Invoke(instance, args);
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
        }

        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
