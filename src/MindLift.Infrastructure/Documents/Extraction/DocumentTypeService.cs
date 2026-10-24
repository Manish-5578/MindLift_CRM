using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Documents.Ai;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>Document types a workspace defines in Settings: list, create, edit, delete, and a test run on a sample file.</summary>
public sealed class DocumentTypeService(
    AppDbContext db, DocumentPageReader reader, DocumentFieldReader fieldReader, AiFieldExtractor ai, ILlmClient llm, DocumentRecordApplier applier,
    IPlatformEntitlements entitlements)
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,49}$", RegexOptions.Compiled);
    private const int MaxFields = 40;
    private const int MaxKeywords = 30;
    private const int MaxAllowedValues = 30;

    public async Task<IReadOnlyList<DocumentTypeDto>> ListAsync(CancellationToken cancellationToken = default)
        => (await db.DocumentTypeDefinitions.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<DocumentTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Map(await LoadAsync(id, cancellationToken));

    public async Task<DocumentTypeDto> SaveAsync(Guid? id, SaveDocumentTypeRequest request, CancellationToken cancellationToken = default)
    {
        var definition = id is { } existing ? await LoadAsync(existing, cancellationToken) : new DocumentTypeDefinition();
        await ValidateAsync(id, request, cancellationToken);

        definition.Key = request.Key.Trim().ToLowerInvariant();
        definition.Name = request.Name.Trim();
        definition.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        definition.IsActive = request.IsActive;
        definition.MinScore = request.MinScore;
        definition.KeywordsJson = CustomDocumentExtractor.Write(request.Keywords.Select(k => k with { Text = k.Text.Trim() }).ToList());
        definition.FieldsJson = CustomDocumentExtractor.Write(request.Fields.Select(f => f with
        {
            Key = f.Key.Trim(),
            Label = f.Label.Trim(),
            Anchor = string.IsNullOrWhiteSpace(f.Anchor) ? null : f.Anchor.Trim(),
            Pattern = string.IsNullOrWhiteSpace(f.Pattern) ? null : f.Pattern.Trim(),
            Description = string.IsNullOrWhiteSpace(f.Description) ? null : f.Description.Trim(),
            Example = string.IsNullOrWhiteSpace(f.Example) ? null : f.Example.Trim(),
            AllowedValues = f.AllowedValues is { Count: > 0 } allowed
                ? allowed.Select(a => a?.Trim() ?? string.Empty).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : null,
            Mode = string.IsNullOrWhiteSpace(f.Mode) ? DocumentFieldModes.Auto : f.Mode.Trim(),
            SaveTo = string.IsNullOrWhiteSpace(f.SaveTo) ? null : f.SaveTo.Trim().ToLowerInvariant(),
        }).ToList());

        if (id is null)
        {
            db.DocumentTypeDefinitions.Add(definition);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(definition);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await LoadAsync(id, cancellationToken);
        db.DocumentTypeDefinitions.Remove(definition);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Runs the type on a sample file and reports what it would read. Nothing is saved.</summary>
    public async Task<DocumentTypeTestResultDto> TestAsync(Guid id, string samplePath, CancellationToken cancellationToken)
    {
        var definition = await LoadAsync(id, cancellationToken);
        var pages = await reader.ReadAsync(samplePath, cancellationToken);
        var text = string.Join("\n", pages.Select(p => p.Text));
        var keywords = CustomDocumentExtractor.Keywords(definition.KeywordsJson);
        var score = CustomDocumentExtractor.Score(keywords, text);
        var aiOn = await db.Set<TenantSettings>().AsNoTracking().Select(t => t.AiReadingEnabled).FirstOrDefaultAsync(cancellationToken)
            && await entitlements.IsAiAllowedAsync(cancellationToken);
        var reading = await fieldReader.ReadAsync(definition.Name, definition.Description, CustomDocumentExtractor.Fields(definition.FieldsJson), pages, aiOn, cancellationToken);
        var source = pages.Count == 0 ? "None" : pages.Select(p => p.Source).Distinct().Count() == 1 ? pages[0].Source : "Mixed";
        var note = reading.Note ?? (aiOn ? null : "AI reading is switched off, so only anchors and patterns were used.");
        return new DocumentTypeTestResultDto(score >= definition.MinScore, score, definition.Key, source, reading.Fields, reading.Method, note);
    }

    /// <summary>The active document types, for choosing one when a document is uploaded or reviewed.</summary>
    public async Task<IReadOnlyList<DocumentTypeChoiceDto>> ChoicesAsync(CancellationToken cancellationToken = default)
        => await db.DocumentTypeDefinitions.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new DocumentTypeChoiceDto(d.Key, d.Name)).ToListAsync(cancellationToken);

    /// <summary>The record fields a document field can be saved to, for the "Save to" choice.</summary>
    public Task<IReadOnlyList<RecordTargetDto>> TargetsAsync(CancellationToken cancellationToken = default) => applier.TargetsAsync(cancellationToken);

    /// <summary>Whether AI reading is on for this workspace and whether the model can be reached right now.</summary>
    public async Task<DocumentAiStatusDto> AiStatusAsync(CancellationToken cancellationToken = default)
    {
        var allowed = await entitlements.IsAiAllowedAsync(cancellationToken);
        var enabled = allowed && await db.Set<TenantSettings>().AsNoTracking().Select(t => t.AiReadingEnabled).FirstOrDefaultAsync(cancellationToken);
        if (!allowed)
        {
            return new DocumentAiStatusDto(false, llm.IsConfigured, false, false, llm.Provider, llm.Model, "AI reading is not included for your company. Contact support to add it.", false);
        }

        if (!llm.IsConfigured)
        {
            return new DocumentAiStatusDto(enabled, false, false, false, llm.Provider, llm.Model, "No AI model is set up on this server.");
        }

        var status = await llm.CheckAsync(cancellationToken);
        return new DocumentAiStatusDto(enabled, true, status.Reachable, status.ModelInstalled, llm.Provider, llm.Model, status.Message);
    }

    /// <summary>Whether the fixed built-in reading rules run before the company's own document types.</summary>
    public async Task<DocumentBuiltInRulesDto> BuiltInRulesAsync(CancellationToken cancellationToken = default)
        => new(await db.Set<TenantSettings>().AsNoTracking().Select(t => t.BuiltInDocumentRulesEnabled).FirstOrDefaultAsync(cancellationToken));

    public async Task<DocumentBuiltInRulesDto> SetBuiltInRulesAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var settings = await db.Set<TenantSettings>().FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("settings_missing", "The company settings could not be found.");
        settings.BuiltInDocumentRulesEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return new DocumentBuiltInRulesDto(enabled);
    }

    /// <summary>Switches AI reading on or off for the workspace. It can only be switched on when a model is set up on the server.</summary>
    public async Task<DocumentAiStatusDto> SetAiAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var settings = await db.Set<TenantSettings>().FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("settings_missing", "The company settings could not be found.");
        if (enabled && !await entitlements.IsAiAllowedAsync(cancellationToken))
        {
            throw new BusinessRuleException("ai_not_in_plan", "AI reading is not included for your company. Contact support to add it.");
        }

        if (enabled && !llm.IsConfigured)
        {
            throw new ValidationException("enabled", "No AI model is set up on this server, so AI reading cannot be switched on.");
        }

        settings.AiReadingEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return await AiStatusAsync(cancellationToken);
    }

    /// <summary>Reads a sample document and proposes fields for a document type. Needs AI reading to be on.</summary>
    public async Task<SuggestFieldsResultDto> SuggestFieldsAsync(string samplePath, string? typeName, CancellationToken cancellationToken)
    {
        var enabled = await db.Set<TenantSettings>().AsNoTracking().Select(t => t.AiReadingEnabled).FirstOrDefaultAsync(cancellationToken)
            && await entitlements.IsAiAllowedAsync(cancellationToken);
        if (!enabled || !llm.IsConfigured)
        {
            throw new BusinessRuleException("ai_off", "Switch on AI reading first. Suggesting fields needs it.");
        }

        var pages = await reader.ReadAsync(samplePath, cancellationToken);
        if (pages.Count == 0 || pages.All(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            throw new BusinessRuleException("no_text", "No text could be read from this file.");
        }

        try
        {
            return await ai.SuggestFieldsAsync(typeName, pages, cancellationToken);
        }
        catch (LlmException ex)
        {
            throw new BusinessRuleException("ai_unavailable", ex.Message);
        }
    }

    /// <summary>Adds the ready-made document types that this workspace does not have yet (matched by key) and returns the ones added.</summary>
    public async Task<IReadOnlyList<DocumentTypeDto>> AddStartersAsync(CancellationToken cancellationToken = default)
    {
        var existing = (await db.DocumentTypeDefinitions.AsNoTracking().Select(d => d.Key).ToListAsync(cancellationToken)).ToHashSet();
        var added = new List<DocumentTypeDto>();
        foreach (var starter in StarterDocumentTypes.All.Where(s => !existing.Contains(s.Key)))
        {
            added.Add(await SaveAsync(null, starter, cancellationToken));
        }

        return added;
    }

    private async Task ValidateAsync(Guid? id, SaveDocumentTypeRequest r, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        void Fail(string key, string message) => errors[key] = [message];

        var key = (r.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (!KeyPattern.IsMatch(key)) Fail("key", "Use 2 to 50 lowercase letters, numbers or underscores, starting with a letter.");
        else if (await db.DocumentTypeDefinitions.AnyAsync(d => d.Key == key && d.Id != id, cancellationToken)) Fail("key", "Another document type already uses this key.");

        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 150) Fail("name", "Enter a name of up to 150 characters.");
        if (r.MinScore is < 1 or > 1000) Fail("minScore", "The minimum score is 1 to 1000.");

        var keywords = r.Keywords ?? [];
        if (keywords.Count > MaxKeywords || keywords.Any(k => string.IsNullOrWhiteSpace(k.Text) || k.Text.Trim().Length > 100 || k.Weight is < 1 or > 100))
        {
            Fail("keywords", $"Use up to {MaxKeywords} keywords, each with text and a weight from 1 to 100.");
        }

        var fields = r.Fields ?? [];
        if (fields.Count == 0 || fields.Count > MaxFields)
        {
            Fail("fields", $"Add between 1 and {MaxFields} fields.");
        }
        else
        {
            if (fields.Select(f => f.Key?.Trim()).Distinct().Count() != fields.Count)
            {
                Fail("fields", "Each field needs its own key.");
            }

            var targets = await applier.TargetsAsync(cancellationToken);
            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                var problem = FieldProblem(f, targets);
                if (problem is not null)
                {
                    Fail($"fields[{i}]", problem);
                    break;
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>The first thing wrong with a field rule, or null when it is usable.</summary>
    private static string? FieldProblem(DocumentFieldRuleDto f, IReadOnlyList<RecordTargetDto> targets)
    {
        if (string.IsNullOrWhiteSpace(f.Key) || !KeyPattern.IsMatch(f.Key.Trim())) return "A field key uses lowercase letters, numbers or underscores, starting with a letter.";
        if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Trim().Length > 100) return "Each field needs a label of up to 100 characters.";
        if (!DocumentValueTypes.All.Contains(f.ValueType)) return "Choose a value type: text, id, number, amount, date, email or phone.";
        var mode = string.IsNullOrWhiteSpace(f.Mode) ? DocumentFieldModes.Auto : f.Mode.Trim();
        if (!DocumentFieldModes.All.Contains(mode)) return "Choose how the field is read: automatic, patterns only or AI only.";

        // A field read by patterns only has nothing to go on without an anchor or a pattern. Otherwise the AI can read it from its name and description.
        if (mode == DocumentFieldModes.Rules && string.IsNullOrWhiteSpace(f.Anchor) && string.IsNullOrWhiteSpace(f.Pattern)) return "A field read by patterns only needs an anchor (the label written before it) or a pattern.";
        if (f.Anchor is { Length: > 100 }) return "An anchor is at most 100 characters.";
        if (f.Description is { Length: > 300 }) return "A description is at most 300 characters.";
        if (f.Example is { Length: > 150 }) return "An example is at most 150 characters.";
        if (f.AllowedValues is { } allowed && (allowed.Count > MaxAllowedValues || allowed.Any(a => a is { Length: > 100 }))) return $"A list of allowed values has at most {MaxAllowedValues} values of up to 100 characters.";
        if (!string.IsNullOrWhiteSpace(f.SaveTo))
        {
            var split = DocumentRecordApplier.Split(f.SaveTo);
            var target = split is { } s ? targets.FirstOrDefault(t => t.Entity == s.Entity && string.Equals(t.Key, s.Key, StringComparison.OrdinalIgnoreCase)) : null;
            if (target is null) return $"\"{f.SaveTo}\" is not a field a document value can be saved to.";
            if (!DocumentRecordApplier.Compatible(f.ValueType, target.ValueType)) return $"{f.Label} is a {f.ValueType.ToLowerInvariant()} value and cannot be saved to {target.Label} on {target.Entity}, which holds {target.ValueType.ToLowerInvariant()}.";
        }

        if (!string.IsNullOrWhiteSpace(f.Pattern))
        {
            if (f.Pattern.Length > 300) return "A pattern is at most 300 characters.";
            try
            {
                _ = new Regex(f.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                return "The pattern is not a valid regular expression.";
            }
        }

        return null;
    }

    private async Task<DocumentTypeDefinition> LoadAsync(Guid id, CancellationToken cancellationToken)
        => await db.DocumentTypeDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException("Document type", id);

    private static DocumentTypeDto Map(DocumentTypeDefinition d) => new(
        d.Id, d.Key, d.Name, d.Description, d.IsActive, d.MinScore,
        CustomDocumentExtractor.Keywords(d.KeywordsJson), CustomDocumentExtractor.Fields(d.FieldsJson));
}
