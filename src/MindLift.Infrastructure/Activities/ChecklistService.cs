using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Deals;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Activities;

/// <summary>Onboarding checklist templates (plan 7.4) and the handler that turns them into tasks when a deal is won.</summary>
public sealed class ChecklistService(AppDbContext db, IClock clock) : IDealWonHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ChecklistTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
        => (await db.ChecklistTemplates.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken)).Select(Map).ToList();

    public async Task<ChecklistTemplateDto> SaveAsync(Guid? id, SaveChecklistTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw RecordHelpers.Invalid("name", "Name is required.");
        }

        if (request.Name.Trim().Length > 150)
        {
            throw RecordHelpers.Invalid("name", "Use at most 150 characters.");
        }

        if (request.Items.Count is 0 or > 30)
        {
            throw RecordHelpers.Invalid("items", "Add between 1 and 30 tasks.");
        }

        if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.Subject) || i.Subject.Trim().Length > 250))
        {
            throw RecordHelpers.Invalid("items", "Every task needs a subject of up to 250 characters.");
        }

        if (request.Items.Any(i => i.OffsetDays is < 0 or > 365))
        {
            throw RecordHelpers.Invalid("items", "Days after winning must be between 0 and 365.");
        }

        if (request.PipelineId is { } pipelineId && !await db.Pipelines.AnyAsync(p => p.Id == pipelineId, cancellationToken))
        {
            throw RecordHelpers.Invalid("pipelineId", "Choose an existing pipeline.");
        }

        var name = request.Name.Trim();
        if (await db.ChecklistTemplates.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower(), cancellationToken))
        {
            throw new ConflictException("template_exists", $"A checklist named {name} already exists.");
        }

        var template = id is { } existing
            ? await db.ChecklistTemplates.FirstOrDefaultAsync(t => t.Id == existing, cancellationToken) ?? throw new NotFoundException("Checklist", existing)
            : new ChecklistTemplate();
        template.Name = name;
        template.PipelineId = request.PipelineId;
        template.IsActive = request.IsActive;
        template.Items = JsonSerializer.Serialize(request.Items.Select(i => new ChecklistItemDto(i.Subject.Trim(), i.OffsetDays, string.IsNullOrWhiteSpace(i.Description) ? null : i.Description.Trim())), Json);
        if (id is null)
        {
            db.ChecklistTemplates.Add(template);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(template);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.ChecklistTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Checklist", id);
        db.ChecklistTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates the tasks of every matching active template, once per deal and template.</summary>
    public async Task OnDealWonAsync(Deal deal, CancellationToken cancellationToken)
    {
        var templates = await db.ChecklistTemplates.AsNoTracking()
            .Where(t => t.IsActive && t.Trigger == "DealWon" && (t.PipelineId == null || t.PipelineId == deal.PipelineId))
            .ToListAsync(cancellationToken);
        var today = DateTime.SpecifyKind(clock.UtcNow.Date, DateTimeKind.Utc);

        foreach (var template in templates)
        {
            if (await db.Activities.AnyAsync(a => a.DealId == deal.Id && a.ChecklistTemplateId == template.Id, cancellationToken))
            {
                continue;
            }

            foreach (var item in Parse(template.Items))
            {
                db.Activities.Add(new Activity
                {
                    Type = ActivityTypes.Task,
                    Subject = item.Subject,
                    Description = item.Description,
                    Status = "Open",
                    DueAt = today.AddDays(item.OffsetDays),
                    OwnerId = deal.OwnerId,
                    DealId = deal.Id,
                    AccountId = deal.AccountId,
                    ContactId = deal.PrimaryContactId,
                    ChecklistTemplateId = template.Id,
                });
            }
        }
    }

    private static List<ChecklistItemDto> Parse(string json) => JsonSerializer.Deserialize<List<ChecklistItemDto>>(json, Json) ?? [];

    private static ChecklistTemplateDto Map(ChecklistTemplate t) => new(t.Id, t.Name, t.Trigger, t.PipelineId, t.IsActive, Parse(t.Items));
}
