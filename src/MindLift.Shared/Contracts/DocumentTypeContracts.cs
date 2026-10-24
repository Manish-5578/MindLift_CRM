namespace MindLift.Shared.Contracts;

/// <summary>Value types a field can hold. Each is normalised after it is read (dates, amounts, numbers).</summary>
public static class DocumentValueTypes
{
    public const string Text = "Text";
    public const string Id = "Id";
    public const string Number = "Number";
    public const string Amount = "Amount";
    public const string Date = "Date";
    public const string Email = "Email";
    public const string Phone = "Phone";

    public static readonly IReadOnlyList<string> All = [Text, Id, Number, Amount, Date, Email, Phone];

    /// <summary>True when a value of a document field's kind can be put in a record field of the target's kind: the same kind, a number into an amount, anything into text.</summary>
    public static bool Compatible(string documentType, string targetType)
    {
        if (documentType == targetType)
        {
            return true;
        }

        return documentType switch
        {
            Number or Amount => targetType is Number or Amount or Text,
            Date => targetType is Text,
            Email or Phone => targetType is Text or Id,
            _ => targetType is Text or Id,
        };
    }
}

/// <summary>How a field is read. Auto uses the AI reader when it is on and falls back to the anchor or pattern; Rules never uses AI; Ai never uses rules.</summary>
public static class DocumentFieldModes
{
    public const string Auto = "Auto";
    public const string Rules = "Rules";
    public const string Ai = "Ai";

    public static readonly IReadOnlyList<string> All = [Auto, Rules, Ai];
}

/// <summary>A word or phrase that suggests a document is of this type. Weight counts towards the score.</summary>
public sealed record DocumentKeywordDto(string Text, int Weight);

/// <summary>
/// One value to take out of a document. The AI reader finds it from <c>Description</c> (what to look for, in words); the rules reader finds it by a pattern
/// (a regular expression; the first group is the value) or by an anchor (a label such as "License No:"; the value is the text after it on the same line).
/// Pattern wins when both are set. <c>SaveTo</c> names the record field an approved value is written to, as "contacts.first_name"
/// (entity "contacts", "accounts" or "deals", then a standard or custom field key); empty keeps the value on the document only.
/// </summary>
public sealed record DocumentFieldRuleDto(
    string Key,
    string Label,
    string ValueType,
    string? Anchor,
    string? Pattern,
    bool Required,
    bool IsMetric,
    string? Description = null,
    string? Example = null,
    IReadOnlyList<string>? AllowedValues = null,
    string Mode = DocumentFieldModes.Auto,
    string? SaveTo = null);

public sealed record DocumentTypeDto(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsActive,
    int MinScore,
    IReadOnlyList<DocumentKeywordDto> Keywords,
    IReadOnlyList<DocumentFieldRuleDto> Fields);

public sealed record SaveDocumentTypeRequest(
    string Key,
    string Name,
    string? Description,
    bool IsActive,
    int MinScore,
    IReadOnlyList<DocumentKeywordDto> Keywords,
    IReadOnlyList<DocumentFieldRuleDto> Fields);

/// <summary>The result of running a type on a sample file, without saving anything.</summary>
public sealed record DocumentTypeTestResultDto(
    bool Matched, int Score, string? MatchedKey, string TextSource, IReadOnlyList<DocumentTypeFieldResultDto> Fields, string? ExtractionMethod = null, string? Note = null);

/// <summary>
/// One field as read from the sample. Value is null when nothing was found. <c>Method</c> is "ai", "pattern" or "anchor".
/// <c>Verified</c> says the value was found in the document's own text; <c>Snippet</c> is the text around it.
/// </summary>
public sealed record DocumentTypeFieldResultDto(
    string Key, string Label, string? Value, double Confidence, bool Required, bool Found, string? Method,
    bool? Verified = null, string? Snippet = null, int? Page = null);

/// <summary>A record field a document field can be saved to: entity "contacts", "accounts" or "deals", the field key, its label and kind of value.</summary>
public sealed record RecordTargetDto(string Entity, string Key, string Label, string ValueType, bool IsCustom);

/// <summary>Whether AI reading is on for this workspace, and whether the model behind it can be reached.</summary>
public sealed record DocumentAiStatusDto(bool Enabled, bool Configured, bool Reachable, bool ModelInstalled, string Provider, string Model, string? Message, bool Allowed = true);

public sealed record SaveDocumentAiRequest(bool Enabled);

public sealed record DocumentBuiltInRulesDto(bool Enabled);

/// <summary>An active document type as a choice on upload and review: its key and name.</summary>
public sealed record DocumentTypeChoiceDto(string Key, string Name);

/// <summary>A field the AI proposes after reading a sample document.</summary>
public sealed record SuggestedFieldDto(string Key, string Label, string ValueType, string? Description, string? Example);

public sealed record SuggestFieldsResultDto(IReadOnlyList<SuggestedFieldDto> Fields, string? Note);
