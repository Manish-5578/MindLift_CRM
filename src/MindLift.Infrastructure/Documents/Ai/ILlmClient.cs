using System.Text.Json;

namespace MindLift.Infrastructure.Documents.Ai;

/// <summary>Whether the model can be used right now.</summary>
public sealed record LlmStatus(bool Reachable, bool ModelInstalled, string? Message);

/// <summary>The model could not give an answer. The message is written for a person: what is wrong and what to do.</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A language model asked to answer with JSON that follows a schema.</summary>
public interface ILlmClient
{
    /// <summary>False when no model is set up (Provider is None). Nothing is sent anywhere then.</summary>
    bool IsConfigured { get; }

    string Provider { get; }

    string Model { get; }

    Task<LlmStatus> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Asks the model and returns its answer, a JSON text that follows <paramref name="schema"/>. Throws <see cref="LlmException"/> when it cannot answer.</summary>
    Task<string> CompleteJsonAsync(string system, string user, JsonElement schema, CancellationToken cancellationToken);
}
