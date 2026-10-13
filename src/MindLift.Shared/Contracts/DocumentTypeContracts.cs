namespace MindLift.Shared.Contracts;

/// <summary>Value types a field can hold. Each is normalised after it is read (dates, amounts, numbers).</summary>
public static class DocumentValueTypes
{
    public const string Text = "Text";
    public const string Id = "Id";
    public const string Number = "Number";
    public const string Amount = "Amount";
    public const string Date = "Date";

    public static readonly IReadOnlyList<string> All = [Text, Id, Number, Amount, Date];
}

/// <summary>A word or phrase that suggests a document is of this type. Weight counts towards the score.</summary>
public sealed record DocumentKeywordDto(string Text, int Weight);

/// <summary>
/// One value to take out of a document. It is found by a pattern (a regular expression; the first group is the value),
/// or by an anchor (a label such as "License No:"; the value is the text after it on the same line). Pattern wins when both are set.
/// </summary>
public sealed record DocumentFieldRuleDto(string Key, string Label, string ValueType, string? Anchor, string? Pattern, bool Required, bool IsMetric);

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
public sealed record DocumentTypeTestResultDto(bool Matched, int Score, string? MatchedKey, string TextSource, IReadOnlyList<DocumentTypeFieldResultDto> Fields);

/// <summary>One field as read from the sample. Value is null when nothing was found.</summary>
public sealed record DocumentTypeFieldResultDto(string Key, string Label, string? Value, double Confidence, bool Required, bool Found, string? Method);
