using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A kind of document a workspace wants read: its name, how to recognise it, and which values to take out (plan 19.3).
/// Keywords and fields are stored as JSON lists so the definition can change without a migration.
/// </summary>
[Audited]
public sealed class DocumentTypeDefinition : TenantEntity
{
    /// <summary>Lower-case, unique per workspace, used as the document's category.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>JSON list of { text, weight }. A document matches when the weights of the keywords it contains reach MinScore.</summary>
    public string KeywordsJson { get; set; } = "[]";

    public int MinScore { get; set; } = 3;

    /// <summary>JSON list of field rules: key, label, value type, anchor or pattern, required, metric.</summary>
    public string FieldsJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}
