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
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content)) return fallback;

            using var document = JsonDocument.Parse(content);
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

            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }

            if (document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(title.GetString()))
            {
                return title.GetString()!;
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
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content)) return result;

            using var document = JsonDocument.Parse(content);
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
