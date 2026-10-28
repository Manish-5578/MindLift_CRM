using System.Text.Json.Serialization;

namespace MindLift.Shared.Contracts;

/// <summary>
/// A visual block in the email layout canvas.
/// </summary>
public sealed class EmailBlockDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Type of block: "header", "heading", "text", "image", "button", "columns", "divider", "spacer", "social", "footer"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nested child blocks for multi-column layouts (e.g. column 1 / column 2).
    /// </summary>
    [JsonPropertyName("children")]
    public List<EmailBlockDto>? Children { get; set; }
}

public sealed class EmailTemplateDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? PreheaderText { get; set; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public string LayoutJson { get; set; } = "[]";
    public List<EmailBlockDto> Blocks { get; set; } = [];
    public string RenderedHtml { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string[] MergeTags { get; set; } = [];
    public string? ThumbnailUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPreset { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class EmailTemplateSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public string[] MergeTags { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public bool IsPreset { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class SaveEmailTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? PreheaderText { get; set; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }

    /// <summary>
    /// Structured block list. If provided, compiles into LayoutJson, RenderedHtml, and PlainTextBody.
    /// </summary>
    public List<EmailBlockDto>? Blocks { get; set; }

    /// <summary>
    /// Raw LayoutJson string if Blocks is not directly passed.
    /// </summary>
    public string? LayoutJson { get; set; }

    /// <summary>
    /// Custom raw HTML content if visual blocks are bypassed.
    /// </summary>
    public string? CustomHtml { get; set; }

    /// <summary>
    /// Custom plain text fallback.
    /// </summary>
    public string? CustomPlainText { get; set; }

    public string? ThumbnailUrl { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CloneEmailTemplateRequest
{
    public string? NewName { get; set; }
}

public sealed class RenderTemplateRequest
{
    public Guid? TemplateId { get; set; }
    public string? Subject { get; set; }
    public List<EmailBlockDto>? Blocks { get; set; }
    public string? LayoutJson { get; set; }
    public string? HtmlContent { get; set; }
    public string? PlainTextContent { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}

public sealed class RenderWithEntityRequest
{
    /// <summary>
    /// Entity type: "lead", "contact", "deal"
    /// </summary>
    public string EntityType { get; set; } = "lead";
    public Guid EntityId { get; set; }
    public Dictionary<string, string>? CustomVariables { get; set; }
}

public sealed class RenderedEmailResult
{
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public string PlainTextBody { get; set; } = string.Empty;
    public string[] DetectedMergeTags { get; set; } = [];
    public string[] MissingVariables { get; set; } = [];
    public Dictionary<string, string> ResolvedVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EmailTemplatePresetDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string Description { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? PreheaderText { get; set; }
    public List<EmailBlockDto> Blocks { get; set; } = [];
    public string[] MergeTags { get; set; } = [];
}
