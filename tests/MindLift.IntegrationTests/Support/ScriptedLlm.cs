using System.Collections.Concurrent;
using System.Text.Json;
using MindLift.Infrastructure.Documents.Ai;

namespace MindLift.IntegrationTests.Support;

/// <summary>
/// The language model of the test host: answers from a script a test sets, and records what it was asked. No network, no model files.
/// Off by default for every workspace, so tests that do not switch AI reading on never reach it.
/// </summary>
public sealed class ScriptedLlm : ILlmClient
{
    public ConcurrentQueue<(string System, string User, JsonElement Schema)> Calls { get; } = new();

    /// <summary>Gives the answer for a call. The default answers an empty object.</summary>
    public Func<string, string, JsonElement, string> Answer { get; set; } = (_, _, _) => "{}";

    /// <summary>When set, every call fails with this message, as when Ollama is not running.</summary>
    public string? FailWith { get; set; }

    public bool Configured { get; set; } = true;

    public bool IsConfigured => Configured;

    public string Provider => "Scripted";

    public string Model => "scripted-1";

    public Task<LlmStatus> CheckAsync(CancellationToken cancellationToken)
        => Task.FromResult(FailWith is null ? new LlmStatus(true, true, null) : new LlmStatus(false, false, FailWith));

    public Task<string> CompleteJsonAsync(string system, string user, JsonElement schema, CancellationToken cancellationToken)
    {
        Calls.Enqueue((system, user, schema));
        if (FailWith is not null)
        {
            throw new LlmException(FailWith);
        }

        return Task.FromResult(Answer(system, user, schema));
    }

    /// <summary>Forgets the script and the recorded calls, so the next test starts clean.</summary>
    public void Reset()
    {
        Calls.Clear();
        Answer = (_, _, _) => "{}";
        FailWith = null;
        Configured = true;
    }
}
