using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Documents.Text;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>Document types a workspace defines in Settings: list, create, edit, delete, and a test run on a sample file.</summary>
public sealed class DocumentTypeService(AppDbContext db, DocumentPageReader reader)
{
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{1,49}$", RegexOptions.Compiled);
    private const int MaxFields = 40;
    private const int MaxKeywords = 30;

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
        var fields = CustomDocumentExtractor.Extract(CustomDocumentExtractor.Fields(definition.FieldsJson), text);
        var source = pages.Count == 0 ? "None" : pages.Select(p => p.Source).Distinct().Count() == 1 ? pages[0].Source : "Mixed";
        return new DocumentTypeTestResultDto(score >= definition.MinScore, score, definition.Key, source, fields);
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

            for (var i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                var problem = FieldProblem(f);
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
    private static string? FieldProblem(DocumentFieldRuleDto f)
    {
        if (string.IsNullOrWhiteSpace(f.Key) || !KeyPattern.IsMatch(f.Key.Trim())) return "A field key uses lowercase letters, numbers or underscores, starting with a letter.";
        if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Trim().Length > 100) return "Each field needs a label of up to 100 characters.";
        if (!DocumentValueTypes.All.Contains(f.ValueType)) return "Choose a value type: text, id, number, amount or date.";
        if (string.IsNullOrWhiteSpace(f.Anchor) && string.IsNullOrWhiteSpace(f.Pattern)) return "Give each field an anchor (the label written before it) or a pattern.";
        if (f.Anchor is { Length: > 100 }) return "An anchor is at most 100 characters.";
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
