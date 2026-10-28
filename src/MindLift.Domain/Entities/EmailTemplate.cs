using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// Phase 4 / #26: Visual email template builder (email-template-builder).
/// Visual block-based template layout with compiled responsive HTML, plain-text fallback,
/// and automated merge tag extraction/resolution.
/// </summary>
[Audited]
public sealed class EmailTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? PreheaderText { get; set; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }

    /// <summary>
    /// Structured JSON representation of the visual layout block tree (EmailBlockDto array).
    /// </summary>
    public string LayoutJson { get; set; } = "[]";

    /// <summary>
    /// Compiled responsive email-safe HTML (table structure with inlined CSS styles).
    /// </summary>
    public string RenderedHtml { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text alternative for email clients without HTML rendering support.
    /// </summary>
    public string PlainTextBody { get; set; } = string.Empty;

    /// <summary>
    /// Detected merge tags extracted from Subject, Preheader, and content (e.g. ["firstName", "company", "unsubscribeUrl"]).
    /// </summary>
    public string[] MergeTags { get; set; } = [];

    public string? ThumbnailUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPreset { get; set; }
}
