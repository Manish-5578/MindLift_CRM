using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindLift.Infrastructure.Documents.Ai;

/// <summary>
/// Talks to a local Ollama server (<c>/api/chat</c> with a JSON schema as the format, so the answer is always valid JSON of the shape asked for).
/// One call runs at a time: a CPU model cannot do two at once any faster, and the others wait their turn.
/// </summary>
public sealed class OllamaLlmClient(IHttpClientFactory httpFactory, IOptions<LlmOptions> options, ILogger<OllamaLlmClient> logger) : ILlmClient
{
    public const string HttpClientName = "ollama";

    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    private LlmOptions Options => options.Value;

    public bool IsConfigured => Options.IsEnabled;

    public string Provider => Options.Provider;

    public string Model => Options.Model;

    public async Task<LlmStatus> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new LlmStatus(false, false, "No AI model is set up on this server (Llm:Provider is None).");
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(8));
            var http = httpFactory.CreateClient(HttpClientName);
            var tags = await http.GetFromJsonAsync<JsonElement>(new Uri(new Uri(Options.BaseUrl), "api/tags"), budget.Token);
            var names = tags.TryGetProperty("models", out var models)
                ? models.EnumerateArray().Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null).Where(n => n is not null).Cast<string>().ToList()
                : [];
            var installed = names.Any(n => string.Equals(n, Options.Model, StringComparison.OrdinalIgnoreCase)
                || string.Equals(n, Options.Model + ":latest", StringComparison.OrdinalIgnoreCase));
            return installed
                ? new LlmStatus(true, true, null)
                : new LlmStatus(true, false, $"The model {Options.Model} is not installed. Install it with: ollama pull {Options.Model}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or UriFormatException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogInformation(ex, "Ollama is not reachable at {Url}", Options.BaseUrl);
            return new LlmStatus(false, false, $"Ollama is not running at {Options.BaseUrl}. Start it, then try again.");
        }
    }

    public async Task<string> CompleteJsonAsync(string system, string user, JsonElement schema, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new LlmException("No AI model is set up on this server.");
        }

        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, Options.TimeoutSeconds)));
            var request = new
            {
                model = Options.Model,
                messages = new object[] { new { role = "system", content = system }, new { role = "user", content = user } },
                format = schema,
                stream = false,
                keep_alive = Options.KeepAlive,
                options = new { temperature = 0, num_ctx = Options.NumCtx, num_predict = 1500 },
            };

            var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(new Uri(new Uri(Options.BaseUrl), "api/chat"), request, budget.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(budget.Token);
                logger.LogWarning("Ollama answered {Status}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
                throw new LlmException(response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? $"The model {Options.Model} is not installed. Install it with: ollama pull {Options.Model}"
                    : $"The AI model returned an error ({(int)response.StatusCode}).");
            }

            var answer = await response.Content.ReadFromJsonAsync<JsonElement>(budget.Token);
            var content = answer.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var text) ? text.GetString() : null;
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new LlmException("The AI model gave an empty answer.");
            }

            return content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlmException($"The AI model did not answer within {Options.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new LlmException($"Ollama is not running at {Options.BaseUrl}. Start it, then try again.", ex);
        }
        finally
        {
            OneAtATime.Release();
        }
    }
}
