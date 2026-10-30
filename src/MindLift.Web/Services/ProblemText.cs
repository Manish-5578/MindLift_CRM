using System.Net.Http.Json;
using System.Text.Json;

namespace MindLift.Web.Services;

/// <summary>Reads the human message from an RFC 7807 error response, or falls back to a safe default.</summary>
public static class ProblemText
{
    public static async Task<string> FromAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var messages = new List<string>();
                foreach (var prop in errors.EnumerateObject())
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        {
                            messages.Add(item.GetString()!);
                        }
                    }
                }
                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }

            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            // Not a JSON problem response.
        }

        return fallback;
    }

    /// <summary>The field errors of a validation problem, keyed by field name.</summary>
    public static async Task<Dictionary<string, string>> FieldErrorsAsync(HttpResponseMessage response)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    var first = field.Value.EnumerateArray().FirstOrDefault();
                    result[field.Name] = first.ValueKind == JsonValueKind.String ? first.GetString()! : "Please check this field.";
                }
            }
        }
        catch (JsonException)
        {
            // Not a validation problem.
        }

        return result;
    }
}
