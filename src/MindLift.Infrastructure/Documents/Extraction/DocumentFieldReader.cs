using Microsoft.Extensions.Logging;
using MindLift.Infrastructure.Documents.Ai;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>What was read for a document type's fields: every field (found or not), how, and anything a person should know.</summary>
public sealed record FieldReading(IReadOnlyList<DocumentTypeFieldResultDto> Fields, string Method, string? Note);

/// <summary>
/// Reads the fields of a document type from a document's text, by AI, by patterns, or by both, as each field's mode says.
/// AI that is switched off, not set up, unreachable or too slow never fails the document: the patterns answer instead and the note says why.
/// </summary>
public sealed class DocumentFieldReader(AiFieldExtractor ai, ILlmClient llm, ILogger<DocumentFieldReader> logger)
{
    public const string MethodAi = "ai";
    public const string MethodRules = "rules";
    public const string MethodMixed = "mixed";

    public async Task<FieldReading> ReadAsync(
        string typeName, string? typeDescription, IReadOnlyList<DocumentFieldRuleDto> rules, IReadOnlyList<PageText> pages, bool aiAllowed, CancellationToken cancellationToken)
    {
        var text = string.Join("\n", pages.Select(p => p.Text));
        var ruleResults = CustomDocumentExtractor.Extract(rules.Where(r => r.Mode != DocumentFieldModes.Ai).ToList(), text).ToDictionary(r => r.Key);

        var aiResults = new Dictionary<string, AiFieldValue>();
        string? note = null;
        var aiRan = false;
        var wantsAi = rules.Where(r => r.Mode != DocumentFieldModes.Rules).ToList();
        if (wantsAi.Count > 0)
        {
            if (!aiAllowed)
            {
                note = wantsAi.Any(r => r.Mode == DocumentFieldModes.Ai)
                    ? "Some fields are read only by AI, and AI reading is switched off for this workspace."
                    : null;
            }
            else if (!llm.IsConfigured)
            {
                note = "AI reading is on, but no AI model is set up on this server. The patterns were used.";
            }
            else
            {
                try
                {
                    var read = await ai.ExtractAsync(typeName, typeDescription, wantsAi, pages, cancellationToken);
                    aiResults = read.Fields.ToDictionary(f => f.Key);
                    aiRan = true;
                    note = read.Truncated ? "Only the start of a long document was read by the AI." : null;
                }
                catch (LlmException ex)
                {
                    logger.LogWarning(ex, "The AI could not read fields of type {Type}; patterns are used instead.", typeName);
                    note = ex.Message + " The patterns were used.";
                }
            }
        }

        var fields = new List<DocumentTypeFieldResultDto>();
        foreach (var rule in rules)
        {
            ruleResults.TryGetValue(rule.Key, out var byRule);
            aiResults.TryGetValue(rule.Key, out var byAi);
            fields.Add(Choose(rule, byRule, byAi));
        }

        var found = fields.Where(f => f.Found).Select(f => f.Method == "ai" ? MethodAi : MethodRules).Distinct().ToList();
        var method = found.Count > 1 ? MethodMixed : found.Count == 1 ? found[0] : aiRan ? MethodAi : MethodRules;
        return new FieldReading(fields, method, note);
    }

    /// <summary>Picks the answer for one field. In Auto mode a value the AI found in the text wins; else a pattern that matched; else the AI value that could not be verified.</summary>
    private static DocumentTypeFieldResultDto Choose(DocumentFieldRuleDto rule, DocumentTypeFieldResultDto? byRule, AiFieldValue? byAi)
    {
        DocumentTypeFieldResultDto FromAi(AiFieldValue v) => new(
            rule.Key, rule.Label, v.Value, Math.Round(v.Confidence * 100, 1), rule.Required, true, "ai", v.Verified, v.Snippet, v.Page);

        DocumentTypeFieldResultDto FromRule(DocumentTypeFieldResultDto v) => v with { Verified = true };

        var missing = new DocumentTypeFieldResultDto(rule.Key, rule.Label, null, 0, rule.Required, false, null);

        switch (rule.Mode)
        {
            case DocumentFieldModes.Rules:
                return byRule is { Found: true } ? FromRule(byRule) : missing;
            case DocumentFieldModes.Ai:
                return byAi is { Found: true } ? FromAi(byAi) : missing;
            default:
                if (byAi is { Found: true, Verified: true })
                {
                    return FromAi(byAi);
                }

                if (byRule is { Found: true })
                {
                    return FromRule(byRule);
                }

                return byAi is { Found: true } ? FromAi(byAi) : missing;
        }
    }

    /// <summary>For a document with no document type: the useful facts the AI finds, or nothing when AI is off or fails.</summary>
    public async Task<(IReadOnlyList<AiDetail> Details, string? Note)> ReadDetailsAsync(IReadOnlyList<PageText> pages, bool aiAllowed, CancellationToken cancellationToken)
    {
        if (!aiAllowed || !llm.IsConfigured)
        {
            return ([], null);
        }

        try
        {
            return (await ai.KeyDetailsAsync(pages, cancellationToken), null);
        }
        catch (LlmException ex)
        {
            logger.LogWarning(ex, "The AI could not list key details.");
            return ([], ex.Message);
        }
    }
}
