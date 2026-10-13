using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Activities;

/// <summary>
/// Everything that happened to a lead, contact, account or deal, newest first: activities, field changes, stage moves, conversion and,
/// for accounts, the contacts and deals added to them (plan 7.8, timeline fan-out).
/// </summary>
public sealed class TimelineService(AppDbContext db, IPermissionService permissions, IDataScopeService scope)
{
    private const int MaxEntries = 200;

    private static readonly HashSet<string> HiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "NormalizedEmail", "NormalizedPhone", "StageChangedAt", "LastActivityAt", "NextActivityAt", "StageId", "CustomFields", "ProbabilityOverridden",
        "AssignedAt", "ConvertedAt", "ConvertedById", "ConvertedContactId", "ConvertedAccountId", "ConvertedDealId", "ClosedAt", "IsSample",
    };

    public async Task<IReadOnlyList<TimelineEntryDto>> GetAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var kind = entityType.ToLowerInvariant();
        var (module, auditType, ownerId, exists) = kind switch
        {
            "leads" => (AppModule.Leads, "Lead", await db.Leads.AsNoTracking().Where(x => x.Id == entityId).Select(x => (Guid?)x.OwnerId).FirstOrDefaultAsync(cancellationToken), true),
            "contacts" => (AppModule.Contacts, "Contact", await db.Contacts.AsNoTracking().Where(x => x.Id == entityId).Select(x => (Guid?)x.OwnerId).FirstOrDefaultAsync(cancellationToken), true),
            "accounts" => (AppModule.Accounts, "Account", await db.Accounts.AsNoTracking().Where(x => x.Id == entityId).Select(x => (Guid?)x.OwnerId).FirstOrDefaultAsync(cancellationToken), true),
            "deals" => (AppModule.Deals, "Deal", await db.Deals.AsNoTracking().Where(x => x.Id == entityId).Select(x => (Guid?)x.OwnerId).FirstOrDefaultAsync(cancellationToken), true),
            _ => throw new ValidationException(new Dictionary<string, string[]> { ["entityType"] = ["Use leads, contacts, accounts or deals."] }),
        };

        // A record the person may not see looks like a record that does not exist.
        var visible = exists && ownerId is not null && await permissions.HasAsync(module, PermissionAction.View, cancellationToken);
        if (visible)
        {
            var allowed = await scope.GetScopeUserIdsAsync(module, cancellationToken);
            visible = allowed is null || allowed.Contains(ownerId!.Value);
        }

        if (!visible)
        {
            throw new NotFoundException(entityType, entityId);
        }

        var entries = new List<TimelineEntryDto>();
        var userIds = new HashSet<Guid>();

        // Activities linked to the record. Linking a deal or contact also links its company, so a company's page gathers theirs too.
        var activities = await db.Activities.AsNoTracking()
            .Where(a => kind == "leads" ? a.LeadId == entityId : kind == "contacts" ? a.ContactId == entityId : kind == "accounts" ? a.AccountId == entityId : a.DealId == entityId)
            .OrderByDescending(a => a.CompletedAt ?? a.DueAt ?? a.CreatedAt).Take(MaxEntries).ToListAsync(cancellationToken);
        foreach (var a in activities)
        {
            var when = a.CompletedAt ?? a.DueAt ?? a.CreatedAt;
            var verb = a.Status == "Completed" ? (a.Type == ActivityTypes.Note ? "Note" : a.Type + " done") : a.Type + " planned";
            entries.Add(new TimelineEntryDto("activity", when, $"{verb}: {a.Subject}", Trim(a.Description), null, null, a.Type));
        }

        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == auditType && a.EntityId == entityId && a.Action != "Delete")
            .OrderByDescending(a => a.OccurredAt).Take(MaxEntries).ToListAsync(cancellationToken);
        foreach (var audit in audits)
        {
            if (audit.UserId is { } u) userIds.Add(u);
            if (audit.Action == "Create")
            {
                entries.Add(new TimelineEntryDto("created", audit.OccurredAt, $"{Singular(auditType)} created", null, audit.UserId?.ToString(), null, null));
                continue;
            }

            foreach (var line in Describe(audit.Changes))
            {
                entries.Add(new TimelineEntryDto("changed", audit.OccurredAt, line.Title, line.Detail, audit.UserId?.ToString(), null, null));
            }
        }

        if (kind == "deals")
        {
            var history = await db.DealStageHistory.AsNoTracking().Where(h => h.DealId == entityId && h.FromStageId != null).OrderByDescending(h => h.ChangedAt).Take(MaxEntries).ToListAsync(cancellationToken);
            var stageIds = history.SelectMany(h => new[] { h.FromStageId, (Guid?)h.ToStageId }).Where(s => s != null).Select(s => s!.Value).Distinct().ToList();
            var stages = await db.PipelineStages.AsNoTracking().IgnoreQueryFilters().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
            foreach (var h in history)
            {
                if (h.ChangedById is { } u) userIds.Add(u);
                entries.Add(new TimelineEntryDto("stage", h.ChangedAt, $"Moved from {stages.GetValueOrDefault(h.FromStageId!.Value)} to {stages.GetValueOrDefault(h.ToStageId)}", null, h.ChangedById?.ToString(), null, null));
            }
        }

        if (kind == "contacts")
        {
            var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.ConvertedContactId == entityId, cancellationToken);
            if (lead is not null)
            {
                entries.Add(new TimelineEntryDto("converted", lead.ConvertedAt ?? lead.CreatedAt, $"Converted from lead {lead.FirstName} {lead.LastName}".Trim(), lead.Source is null ? null : $"Source: {lead.Source}", null, "/leads/" + lead.Id, null));
                entries.Add(new TimelineEntryDto("created", lead.CreatedAt, "Lead created", $"{lead.FirstName} {lead.LastName}".Trim(), null, null, null));
            }
        }

        if (kind == "accounts")
        {
            var contacts = await db.Contacts.AsNoTracking().Where(c => c.AccountId == entityId).OrderByDescending(c => c.CreatedAt).Take(50).ToListAsync(cancellationToken);
            entries.AddRange(contacts.Select(c => new TimelineEntryDto("contact", c.CreatedAt, $"Contact added: {c.FirstName} {c.LastName}".Trim(), c.JobTitle, null, "/contacts/" + c.Id, null)));
            var deals = await db.Deals.AsNoTracking().Where(d => d.AccountId == entityId).OrderByDescending(d => d.CreatedAt).Take(50).ToListAsync(cancellationToken);
            entries.AddRange(deals.Select(d => new TimelineEntryDto("deal", d.CreatedAt, $"Deal created: {d.Name}", $"{d.Amount:N0} {d.Currency}", null, "/deals/" + d.Id, null)));
        }

        var names = await RecordHelpers.OwnerNamesAsync(db, userIds, cancellationToken);
        return entries
            .Select(e => e.ActorName is { } id && Guid.TryParse(id, out var guid) ? e with { ActorName = names.GetValueOrDefault(guid) } : e)
            .OrderByDescending(e => e.WhenUtc).Take(MaxEntries).ToList();
    }

    private static string Singular(string auditType) => auditType;

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Length > 200 ? text[..200] + "…" : text;

    private static IEnumerable<(string Title, string? Detail)> Describe(string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(changesJson))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(changesJson);
        foreach (var change in document.RootElement.EnumerateArray())
        {
            string? Read(params string[] names) => names.Select(n => change.TryGetProperty(n, out var v) ? v : default).FirstOrDefault(v => v.ValueKind != JsonValueKind.Undefined) is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } value ? value.ToString() : null;

            var field = Read("field", "Field");
            if (field is null || HiddenFields.Contains(field) || field is "Version" or "UpdatedAt" or "UpdatedById" or "CreatedAt" or "CreatedById" or "IsDeleted" or "DeletedAt")
            {
                continue;
            }

            var label = Regex.Replace(field.EndsWith("Id", StringComparison.Ordinal) && field.Length > 2 ? field[..^2] : field, "(?<=[a-z])(?=[A-Z])", " ");
            var oldValue = Read("old", "Old");
            var newValue = Read("new", "New");
            yield return field.EndsWith("Id", StringComparison.Ordinal)
                ? ($"{label} changed", null)
                : ($"{label} changed", $"{oldValue ?? "empty"} → {newValue ?? "empty"}");
        }
    }
}
