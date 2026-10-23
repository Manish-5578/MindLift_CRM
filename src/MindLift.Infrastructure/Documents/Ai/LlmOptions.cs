namespace MindLift.Infrastructure.Documents.Ai;

/// <summary>
/// The language model that reads document fields. It runs on the customer's own machine or network (Ollama); no document text leaves it.
/// Set in the "Llm" section of the configuration (environment variables <c>Llm__Provider</c>, <c>Llm__BaseUrl</c>, ...).
/// </summary>
public sealed class LlmOptions
{
    /// <summary>"Ollama" or "None". "None" turns AI reading off for every workspace.</summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>Where Ollama listens. From inside a container use http://host.docker.internal:11434.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>The model name as Ollama lists it (<c>ollama list</c>). An instruction-tuned model of 7B or more reads fields reliably.</summary>
    public string Model { get; set; } = "qwen2.5:7b-instruct";

    /// <summary>The longest one model call may take, in seconds. A 7B model on a CPU needs about a minute per page of text.</summary>
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>The most characters of document text sent in one call. Longer documents are cut after this many characters.</summary>
    public int MaxInputChars { get; set; } = 9000;

    /// <summary>The model's context window in tokens. Must hold the instructions, the document text and the answer.</summary>
    public int NumCtx { get; set; } = 6144;

    /// <summary>How long Ollama keeps the model in memory after a call, so the next document does not wait for it to load.</summary>
    public string KeepAlive { get; set; } = "30m";

    public bool IsEnabled => string.Equals(Provider, "Ollama", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Model);
}
