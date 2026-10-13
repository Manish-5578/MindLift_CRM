using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;
using MindLift.Shared.Filtering;
using MindLift.Shared.Imports;

namespace MindLift.Infrastructure.Deals;

/// <summary>Something that has to happen when a deal is won, for example creating the onboarding checklist.</summary>
public interface IDealWonHandler
{
    Task OnDealWonAsync(Deal deal, CancellationToken cancellationToken);
}

/// <summary>Deals: list, board, create, edit, move between stages, history, import and export (plan 7.7).</summary>
public sealed class DealService(
    AppDbContext db,
    ITenantContext tenant,
    IClock clock,
    FieldSettingsService fields,
    IDataScopeService scope,
    IEnumerable<IDealWonHandler> wonHandlers,
    INotificationService notifications,
    IRecordSavePipeline savePipeline)
{
    private const int BoardCardsPerStage = 50;
    private const int ClosedDealDays = 30;
    private const int MaxExportRows = 10000;

    // ---------- reading ----------

    public async Task<PagedResult<DealListItemDto>> QueryAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var source = await ScopedAsync(cancellationToken);

        if (query.Filter is not null)
        {
            var registry = await fields.RegistryAsync("deals", cancellationToken);
            source = source.Where(FilterCompiler.Compile<Deal>(query.Filter, registry, await RecordHelpers.FilterContextAsync(db, tenant, clock, cancellationToken)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            source = source.Where(d => d.Name.ToLower().Contains(term)
                || (d.AccountId != null && db.Accounts.Any(a => a.Id == d.AccountId && a.Name.ToLower().Contains(term))));
        }

        var total = await source.CountAsync(cancellationToken);
        source = Sort(source, query.SortField, query.SortDir);
        var rows = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        var lookups = await LookupsAsync(rows, cancellationToken);
        var items = rows.Select(d => ToListItem(d, lookups)).ToList();
        return new PagedResult<DealListItemDto>(items, total, page, pageSize);
    }

    public async Task<DealDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => await MapAsync(await LoadScopedAsync(id, cancellationToken), cancellationToken);

    /// <summary>The kanban board of one pipeline (plan 7.7). Open stages show every open deal; Won and Lost show the last 30 days.</summary>
    public async Task<DealBoardDto> BoardAsync(Guid? pipelineId, FilterGroup? filter, CancellationToken cancellationToken = default)
    {
        var pipeline = pipelineId ?? await db.Pipelines.AsNoTracking().Where(p => p.IsDefault).Select(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (pipeline == Guid.Empty || !await db.Pipelines.AnyAsync(p => p.Id == pipeline, cancellationToken))
        {
            throw new NotFoundException("Pipeline", pipelineId);
        }

        var stages = await db.PipelineStages.AsNoTracking().Where(s => s.PipelineId == pipeline).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        var rates = await RatesAsync(settings.BaseCurrency, cancellationToken);
        var since = clock.UtcNow.AddDays(-ClosedDealDays);

        var source = (await ScopedAsync(cancellationToken)).Where(d => d.PipelineId == pipeline && (d.Status == "Open" || (d.ClosedAt != null && d.ClosedAt >= since)));
        if (filter is not null)
        {
            var registry = await fields.RegistryAsync("deals", cancellationToken);
            source = source.Where(FilterCompiler.Compile<Deal>(filter, registry, await RecordHelpers.FilterContextAsync(db, tenant, clock, cancellationToken)));
        }

        var deals = await source.OrderByDescending(d => d.Amount).Take(5000).ToListAsync(cancellationToken);
        var lookups = await LookupsAsync(deals, cancellationToken);
        var now = clock.UtcNow;

        var columns = stages.Select(stage =>
        {
            var inStage = deals.Where(d => d.StageId == stage.Id).ToList();
            var total = inStage.Sum(d => InBase(d.Amount, d.Currency, rates));
            var weighted = inStage.Sum(d => InBase(d.Amount, d.Currency, rates) * d.Probability / 100m);
            var cards = inStage.Take(BoardCardsPerStage).Select(d => new DealCardDto(
                d.Id, d.Name, d.Amount, d.Currency, InBase(d.Amount, d.Currency, rates),
                d.AccountId is { } a ? lookups.Accounts.GetValueOrDefault(a) : null,
                d.PrimaryContactId is { } c ? lookups.Contacts.GetValueOrDefault(c) : null,
                d.OwnerId, lookups.Owners.GetValueOrDefault(d.OwnerId), d.ExpectedCloseDate, IsRotting(d, stage, now), (int)(now - d.StageChangedAt).TotalDays, d.Version)).ToList();
            return new DealBoardStageDto(stage.Id, stage.Name, stage.StageType, stage.Color, stage.Probability, inStage.Count, total, weighted, cards);
        }).ToList();

        return new DealBoardDto(pipeline, settings.BaseCurrency, columns);
    }

    public async Task<IReadOnlyList<DealStageHistoryDto>> HistoryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await LoadScopedAsync(id, cancellationToken);
        var rows = await db.DealStageHistory.AsNoTracking().Where(h => h.DealId == id).OrderByDescending(h => h.ChangedAt).ToListAsync(cancellationToken);
        var stageIds = rows.SelectMany(r => new[] { r.FromStageId, r.ToStageId }).Where(s => s != null).Select(s => s!.Value).Distinct().ToList();
        var stages = await db.PipelineStages.AsNoTracking().IgnoreQueryFilters().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var users = await RecordHelpers.OwnerNamesAsync(db, rows.Where(r => r.ChangedById != null).Select(r => r.ChangedById!.Value).Distinct(), cancellationToken);

        return rows.Select(r => new DealStageHistoryDto(
            r.FromStageId is { } f ? stages.GetValueOrDefault(f) : null,
            stages.GetValueOrDefault(r.ToStageId) ?? "—",
            r.ChangedAt,
            r.ChangedById is { } u ? users.GetValueOrDefault(u) : null,
            r.SecondsInFromStage is { } s ? Math.Round(s / 86400d, 1) : null)).ToList();
    }

    // ---------- writing ----------

    public async Task<DealDto> CreateAsync(SaveDealRequest request, CancellationToken cancellationToken = default)
    {
        var deal = new Deal { OwnerId = tenant.UserId ?? throw new ForbiddenException("Sign in to create records.", "no_user"), StageChangedAt = clock.UtcNow };
        await ApplyAsync(deal, request, isNew: true, cancellationToken);

        deal = await savePipeline.ExecuteCreateAsync("deals", deal, async () =>
        {
            db.Deals.Add(deal);
            db.DealStageHistory.Add(new DealStageHistory { DealId = deal.Id, FromStageId = null, ToStageId = deal.StageId, ChangedAt = clock.UtcNow, ChangedById = tenant.UserId });
            await db.SaveChangesAsync(cancellationToken);
            return deal;
        }, cancellationToken);

        await NotifyAssignedAsync(deal, cancellationToken);
        return await MapAsync(deal, cancellationToken);
    }

    public async Task<DealDto> UpdateAsync(Guid id, SaveDealRequest request, CancellationToken cancellationToken = default)
    {
        var deal = await LoadScopedAsync(id, cancellationToken);
        var previousOwner = deal.OwnerId;
        var previousStatus = deal.Status;
        var previousSnapshot = new Deal
        {
            Id = deal.Id,
            Name = deal.Name,
            PipelineId = deal.PipelineId,
            StageId = deal.StageId,
            Status = deal.Status,
            Amount = deal.Amount,
            Currency = deal.Currency,
            Probability = deal.Probability,
            ExpectedCloseDate = deal.ExpectedCloseDate,
            OwnerId = deal.OwnerId,
            AccountId = deal.AccountId,
            CustomFields = deal.CustomFields
        };

        await ApplyAsync(deal, request, isNew: false, cancellationToken);
        deal = await savePipeline.ExecuteUpdateAsync("deals", deal, previousSnapshot, async () =>
        {
            await SaveAsync(cancellationToken);
            return deal;
        }, cancellationToken);

        if (deal.OwnerId != previousOwner)
        {
            await NotifyAssignedAsync(deal, cancellationToken);
        }

        await NotifyClosedAsync(deal, previousStatus, cancellationToken);
        return await MapAsync(deal, cancellationToken);
    }

    private Task NotifyAssignedAsync(Deal deal, CancellationToken cancellationToken)
        => notifications.NotifyAsync(new NotificationRequest([deal.OwnerId], NotificationTypes.DealAssigned, $"Deal assigned to you: {deal.Name}", $"{deal.Amount:N0} {deal.Currency}", "deals", deal.Id, $"/deals/{deal.Id}"), cancellationToken);

    /// <summary>A deal that was just won or lost tells its owner and the leaders of the owner's teams (plan 7.7).</summary>
    private async Task NotifyClosedAsync(Deal deal, string previousStatus, CancellationToken cancellationToken)
    {
        if (deal.Status == previousStatus || deal.Status is not ("Won" or "Lost"))
        {
            return;
        }

        var leaders = await db.Teams.AsNoTracking()
            .Where(t => t.LeaderId != null && db.Users.Any(u => u.Id == deal.OwnerId && u.TeamId == t.Id))
            .Select(t => t.LeaderId!.Value).ToListAsync(cancellationToken);
        var won = deal.Status == "Won";
        await notifications.NotifyAsync(new NotificationRequest(
            leaders.Append(deal.OwnerId).ToList(), won ? NotificationTypes.DealWon : NotificationTypes.DealLost,
            won ? $"Deal won: {deal.Name}" : $"Deal lost: {deal.Name}",
            $"{deal.Amount:N0} {deal.Currency}", "deals", deal.Id, $"/deals/{deal.Id}"), cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deal = await LoadScopedAsync(id, cancellationToken);
        var links = await db.DealContacts.Where(c => c.DealId == id).ToListAsync(cancellationToken);
        db.DealContacts.RemoveRange(links);
        db.Deals.Remove(deal);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Moves a deal to another stage of its pipeline. Won and Lost have their own rules (plan 7.7).</summary>
    public async Task<DealDto> ChangeStageAsync(Guid id, ChangeDealStageRequest request, CancellationToken cancellationToken = default)
    {
        var deal = await LoadScopedAsync(id, cancellationToken);
        if (request.Version is { } version && version != deal.Version)
        {
            throw new ConflictException("version_conflict", "Someone else changed this deal. The board has been reloaded.");
        }

        var stage = await db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StageId, cancellationToken)
            ?? throw RecordHelpers.Invalid("stageId", "Choose a stage.");
        if (stage.PipelineId != deal.PipelineId)
        {
            throw new BusinessRuleException("stage_not_in_pipeline", "That stage belongs to a different pipeline.");
        }

        var previousSnapshot = new Deal
        {
            Id = deal.Id,
            Name = deal.Name,
            PipelineId = deal.PipelineId,
            StageId = deal.StageId,
            Status = deal.Status,
            Amount = deal.Amount,
            Currency = deal.Currency,
            Probability = deal.Probability,
            ExpectedCloseDate = deal.ExpectedCloseDate,
            OwnerId = deal.OwnerId,
            AccountId = deal.AccountId,
            CustomFields = deal.CustomFields
        };

        var previousStatus = deal.Status;
        await MoveToStageAsync(deal, stage, request.LostReason, request.LostNotes, request.ClosedAt, cancellationToken);
        deal = await savePipeline.ExecuteUpdateAsync("deals", deal, previousSnapshot, async () =>
        {
            await SaveAsync(cancellationToken);
            return deal;
        }, cancellationToken);

        await NotifyClosedAsync(deal, previousStatus, cancellationToken);
        return await MapAsync(deal, cancellationToken);
    }

    public async Task<ImportResultDto> ImportCsvAsync(string csv, CancellationToken cancellationToken = default)
    {
        var aliases = ImportFieldSets.Deals.ToDictionary(f => f.Field, f => f.Aliases);
        return await RecordHelpers.ImportAsync(csv, aliases, "name", "Name",
            async (cell, ct) =>
            {
                var name = cell("name")!.ToLower();
                return await db.Deals.AnyAsync(d => d.Name.ToLower() == name && d.Status == "Open", ct);
            },
            async (cell, ct) =>
            {
                Guid? accountId = null;
                if (cell("account") is { Length: > 0 } accountName)
                {
                    var lowered = accountName.ToLower();
                    accountId = await db.Accounts.Where(a => a.Name.ToLower() == lowered).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
                }

                Guid? stageId = null;
                if (cell("stage") is { Length: > 0 } stageName)
                {
                    var lowered = stageName.ToLower();
                    stageId = await db.PipelineStages.Where(s => s.Name.ToLower() == lowered && s.StageType == "Open").Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
                }

                var amount = decimal.TryParse(cell("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
                DateTime? closes = DateTime.TryParse(cell("expected_close_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
                await CreateAsync(new SaveDealRequest(cell("name")!, null, stageId, amount, cell("currency")?.ToUpperInvariant(), null, closes, accountId, null, null, null, null), ct);
            },
            cancellationToken);
    }

    public async Task<string> ExportCsvAsync(ListQuery query, CancellationToken cancellationToken = default)
    {
        const int pageSize = 200;
        var items = new List<DealListItemDto>();
        for (var pageNumber = 1; items.Count < MaxExportRows; pageNumber++)
        {
            var page = await QueryAsync(query with { Page = pageNumber, PageSize = pageSize }, cancellationToken);
            items.AddRange(page.Items);
            if (page.Items.Count < pageSize || items.Count >= page.Total) break;
        }

        var builder = new StringBuilder("id,name,amount,currency,amount_in_base,stage,status,probability,account,contact,owner,expected_close_date,created_at\n");
        foreach (var d in items.Take(MaxExportRows))
        {
            var parts = new[]
            {
                d.Id.ToString(), d.Name, d.Amount.ToString(CultureInfo.InvariantCulture), d.Currency, d.AmountInBase.ToString(CultureInfo.InvariantCulture),
                d.StageName, d.Status, d.Probability.ToString(CultureInfo.InvariantCulture), d.AccountName ?? "", d.PrimaryContactName ?? "", d.OwnerName ?? "",
                d.ExpectedCloseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", d.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            };
            builder.AppendLine(string.Join(",", parts.Select(CsvText.Escape)));
        }

        return builder.ToString();
    }

    // ---------- rules ----------

    private async Task ApplyAsync(Deal deal, SaveDealRequest request, bool isNew, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw RecordHelpers.Invalid("name", "Name is required.");
        }

        if (request.Amount < 0 || request.Amount > 9_999_999_999_999m)
        {
            throw RecordHelpers.Invalid("amount", "Enter an amount of zero or more.");
        }

        if (request.Probability is < 0 or > 100)
        {
            throw RecordHelpers.Invalid("probability", "Probability is a number from 0 to 100.");
        }

        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        var currency = (request.Currency ?? (isNew ? settings.BaseCurrency : deal.Currency)).Trim().ToUpperInvariant();
        if (currency != settings.BaseCurrency && !await db.ExchangeRates.AnyAsync(r => r.Currency == currency, cancellationToken))
        {
            throw RecordHelpers.Invalid("currency", $"No exchange rate for {currency} yet. Add one in Settings, Currencies.");
        }

        var pipelineId = request.PipelineId ?? (isNew ? await db.Pipelines.Where(p => p.IsDefault).Select(p => p.Id).FirstOrDefaultAsync(cancellationToken) : deal.PipelineId);
        if (pipelineId == Guid.Empty || !await db.Pipelines.AnyAsync(p => p.Id == pipelineId && p.IsActive, cancellationToken))
        {
            throw RecordHelpers.Invalid("pipelineId", "Choose a pipeline.");
        }

        var stages = await db.PipelineStages.AsNoTracking().Where(s => s.PipelineId == pipelineId).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var pipelineChanged = !isNew && pipelineId != deal.PipelineId;

        if (request.AccountId is { } accountId && !await db.Accounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            throw RecordHelpers.Invalid("accountId", "Choose an existing account.");
        }

        var customErrors = CustomFieldRules.ValidateValues(await fields.CustomRulesAsync("deals", cancellationToken),
            request.CustomFields?.ToDictionary(kv => kv.Key, kv => (JsonElement?)kv.Value) ?? []);
        if (customErrors.Count > 0)
        {
            throw new ValidationException(customErrors);
        }

        var contacts = await ResolveContactsAsync(request.Contacts, cancellationToken);

        deal.Name = request.Name.Trim();
        deal.Amount = request.Amount;
        deal.Currency = currency;
        deal.ExpectedCloseDate = request.ExpectedCloseDate;
        deal.Description = request.Description;
        deal.PrimaryContactId = contacts.FirstOrDefault(c => c.IsPrimary)?.ContactId;
        deal.AccountId = request.AccountId
            ?? (deal.PrimaryContactId is { } primary ? await db.Contacts.Where(c => c.Id == primary).Select(c => c.AccountId).FirstOrDefaultAsync(cancellationToken) : null);
        if (request.OwnerId is { } owner)
        {
            deal.OwnerId = owner;
        }

        if (request.CustomFields is not null)
        {
            deal.CustomFields = RecordHelpers.CustomDoc(request.CustomFields);
        }

        if (isNew)
        {
            var stage = request.StageId is { } wanted
                ? stages.FirstOrDefault(s => s.Id == wanted) ?? throw RecordHelpers.Invalid("stageId", "Choose a stage of this pipeline.")
                : stages.FirstOrDefault(s => s.StageType == "Open") ?? throw new BusinessRuleException("no_open_stage", "This pipeline has no open stage.");
            if (stage.StageType != "Open")
            {
                throw new BusinessRuleException("stage_not_open", "A new deal must start in an open stage.");
            }

            deal.PipelineId = pipelineId;
            deal.StageId = stage.Id;
            deal.Status = "Open";
            deal.Probability = request.Probability ?? stage.Probability;
            deal.ProbabilityOverridden = request.Probability is not null && request.Probability != stage.Probability;
        }
        else if (pipelineChanged)
        {
            var stage = request.StageId is { } wanted
                ? stages.FirstOrDefault(s => s.Id == wanted) ?? throw RecordHelpers.Invalid("stageId", "Choose a stage of the new pipeline.")
                : stages.FirstOrDefault(s => s.StageType == "Open") ?? throw new BusinessRuleException("no_open_stage", "The new pipeline has no open stage.");
            deal.PipelineId = pipelineId;
            deal.ProbabilityOverridden = false;
            await MoveToStageAsync(deal, stage, null, null, null, cancellationToken, force: true);
        }
        else
        {
            if (request.StageId is { } wanted && wanted != deal.StageId)
            {
                var stage = stages.FirstOrDefault(s => s.Id == wanted) ?? throw RecordHelpers.Invalid("stageId", "Choose a stage of this pipeline.");
                await MoveToStageAsync(deal, stage, null, null, null, cancellationToken);
            }

            if (request.Probability is { } probability && deal.Status == "Open")
            {
                var stageProbability = stages.First(s => s.Id == deal.StageId).Probability;
                deal.Probability = probability;
                deal.ProbabilityOverridden = probability != stageProbability;
            }
        }

        if (request.Contacts is not null || isNew)
        {
            await ReplaceContactsAsync(deal, contacts, cancellationToken);
        }
    }

    private async Task<List<DealContactInput>> ResolveContactsAsync(IReadOnlyList<DealContactInput>? input, CancellationToken cancellationToken)
    {
        var list = (input ?? []).GroupBy(c => c.ContactId).Select(g => g.First()).ToList();
        if (list.Count > 25)
        {
            throw RecordHelpers.Invalid("contacts", "A deal can have at most 25 contacts.");
        }

        var ids = list.Select(c => c.ContactId).ToList();
        if (await db.Contacts.CountAsync(c => ids.Contains(c.Id), cancellationToken) != ids.Count)
        {
            throw RecordHelpers.Invalid("contacts", "One of the contacts no longer exists.");
        }

        if (list.Count(c => c.IsPrimary) > 1)
        {
            throw RecordHelpers.Invalid("contacts", "Only one contact can be the primary contact.");
        }

        if (list.Count > 0 && !list.Any(c => c.IsPrimary))
        {
            list[0] = list[0] with { IsPrimary = true };
        }

        return list;
    }

    private async Task ReplaceContactsAsync(Deal deal, List<DealContactInput> contacts, CancellationToken cancellationToken)
    {
        var existing = await db.DealContacts.Where(c => c.DealId == deal.Id).ToListAsync(cancellationToken);
        db.DealContacts.RemoveRange(existing.Where(e => contacts.All(c => c.ContactId != e.ContactId)));
        foreach (var input in contacts)
        {
            var current = existing.FirstOrDefault(e => e.ContactId == input.ContactId);
            if (current is null)
            {
                db.DealContacts.Add(new DealContact { DealId = deal.Id, ContactId = input.ContactId, Role = RecordHelpers.Clean(input.Role), IsPrimary = input.IsPrimary });
            }
            else
            {
                current.Role = RecordHelpers.Clean(input.Role);
                current.IsPrimary = input.IsPrimary;
            }
        }
    }

    /// <summary>Applies the Open, Won or Lost rules of a stage move, writes the history row and runs the deal-won handlers.</summary>
    private async Task MoveToStageAsync(Deal deal, PipelineStage stage, string? lostReason, string? lostNotes, DateTime? closedAt, CancellationToken cancellationToken, bool force = false)
    {
        if (stage.Id == deal.StageId && !force)
        {
            return;
        }

        var now = clock.UtcNow;
        var wasWon = deal.Status == "Won";
        if (stage.StageType == "Lost")
        {
            if (string.IsNullOrWhiteSpace(lostReason))
            {
                throw new BusinessRuleException("lost_reason_required", "Choose why this deal was lost.");
            }

            if (!await db.PicklistValues.AnyAsync(p => p.PicklistKey == "lost_reason" && p.Value == lostReason && p.IsActive, cancellationToken))
            {
                throw RecordHelpers.Invalid("lostReason", "Choose a valid reason.");
            }

            deal.Status = "Lost";
            deal.ClosedAt = now;
            deal.Probability = 0;
            deal.LostReason = lostReason;
            deal.LostNotes = RecordHelpers.Clean(lostNotes);
        }
        else if (stage.StageType == "Won")
        {
            deal.Status = "Won";
            deal.ClosedAt = closedAt is { } at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : now;
            deal.Probability = 100;
            deal.LostReason = null;
            deal.LostNotes = null;
        }
        else
        {
            deal.Status = "Open";
            deal.ClosedAt = null;
            deal.LostReason = null;
            deal.LostNotes = null;
            if (!deal.ProbabilityOverridden)
            {
                deal.Probability = stage.Probability;
            }
        }

        db.DealStageHistory.Add(new DealStageHistory
        {
            DealId = deal.Id,
            FromStageId = deal.StageId,
            ToStageId = stage.Id,
            ChangedAt = now,
            ChangedById = tenant.UserId,
            SecondsInFromStage = (long)Math.Max(0, (now - deal.StageChangedAt).TotalSeconds),
        });
        deal.StageId = stage.Id;
        deal.StageChangedAt = now;

        if (deal.Status == "Won" && !wasWon)
        {
            await PromoteContactAsync(deal, cancellationToken);
            foreach (var handler in wonHandlers)
            {
                await handler.OnDealWonAsync(deal, cancellationToken);
            }
        }
    }

    /// <summary>The first won deal turns a prospect into a client (plan 7.4).</summary>
    private async Task PromoteContactAsync(Deal deal, CancellationToken cancellationToken)
    {
        if (deal.PrimaryContactId is not { } contactId || !await db.PicklistValues.AnyAsync(p => p.PicklistKey == "contact_type" && p.Value == "client" && p.IsActive, cancellationToken))
        {
            return;
        }

        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is { Type: "prospect" or null })
        {
            contact.Type = "client";
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("version_conflict", "Someone else changed this deal. Reload and try again.");
        }
    }

    // ---------- helpers ----------

    private static bool IsRotting(Deal deal, PipelineStage? stage, DateTime now)
        => deal.Status == "Open" && stage?.RottingDays is { } days && (deal.LastActivityAt ?? deal.StageChangedAt) < now.AddDays(-days);

    private static decimal InBase(decimal amount, string currency, IReadOnlyDictionary<string, decimal> rates)
        => Math.Round(amount * rates.GetValueOrDefault(currency, 1m), 2);

    /// <summary>The newest rate on or before today for each currency; the base currency is always 1.</summary>
    private async Task<Dictionary<string, decimal>> RatesAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var today = clock.UtcNow;
        var rows = await db.ExchangeRates.AsNoTracking().Where(r => r.EffectiveDate <= today).OrderBy(r => r.EffectiveDate).ToListAsync(cancellationToken);
        var rates = rows.GroupBy(r => r.Currency).ToDictionary(g => g.Key, g => g.Last().RateToBase);
        rates[baseCurrency] = 1m;
        return rates;
    }

    private sealed record Lookups(
        Dictionary<Guid, string> Accounts,
        Dictionary<Guid, string> Contacts,
        Dictionary<Guid, PipelineStage> Stages,
        Dictionary<Guid, string> Owners,
        IReadOnlyDictionary<string, decimal> Rates,
        DateTime Now);

    private async Task<Lookups> LookupsAsync(IReadOnlyList<Deal> deals, CancellationToken cancellationToken)
    {
        var accountIds = deals.Where(d => d.AccountId != null).Select(d => d.AccountId!.Value).Distinct().ToList();
        var contactIds = deals.Where(d => d.PrimaryContactId != null).Select(d => d.PrimaryContactId!.Value).Distinct().ToList();
        var stageIds = deals.Select(d => d.StageId).Distinct().ToList();
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        return new Lookups(
            await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken),
            await db.Contacts.AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.FirstName + " " + c.LastName).Trim(), cancellationToken),
            await db.PipelineStages.AsNoTracking().Where(s => stageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken),
            await RecordHelpers.OwnerNamesAsync(db, deals.Select(d => d.OwnerId).Distinct(), cancellationToken),
            await RatesAsync(settings.BaseCurrency, cancellationToken),
            clock.UtcNow);
    }

    private static DealListItemDto ToListItem(Deal d, Lookups l)
    {
        var stage = l.Stages.GetValueOrDefault(d.StageId);
        return new DealListItemDto(d.Id, d.Name, d.Amount, d.Currency, InBase(d.Amount, d.Currency, l.Rates), d.PipelineId, d.StageId,
            stage?.Name ?? "—", stage?.StageType ?? "Open", d.Status, d.Probability, d.AccountId,
            d.AccountId is { } a ? l.Accounts.GetValueOrDefault(a) : null,
            d.PrimaryContactId is { } c ? l.Contacts.GetValueOrDefault(c) : null,
            d.OwnerId, l.Owners.GetValueOrDefault(d.OwnerId), d.ExpectedCloseDate, IsRotting(d, stage, l.Now), d.CreatedAt);
    }

    private async Task<DealDto> MapAsync(Deal d, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(cancellationToken);
        var rates = await RatesAsync(settings.BaseCurrency, cancellationToken);
        var stage = await db.PipelineStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == d.StageId, cancellationToken);
        var accountName = d.AccountId is { } a ? await db.Accounts.AsNoTracking().Where(x => x.Id == a).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) : null;
        var links = await db.DealContacts.AsNoTracking().Where(c => c.DealId == d.Id).ToListAsync(cancellationToken);
        var ids = links.Select(c => c.ContactId).ToList();
        var names = await db.Contacts.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.FirstName + " " + c.LastName).Trim(), cancellationToken);
        var contacts = links.OrderByDescending(c => c.IsPrimary).ThenBy(c => names.GetValueOrDefault(c.ContactId))
            .Select(c => new DealContactDto(c.ContactId, names.GetValueOrDefault(c.ContactId) ?? "—", c.Role, c.IsPrimary)).ToList();

        return new DealDto(d.Id, d.Name, d.PipelineId, d.StageId, d.Status, d.Amount, d.Currency, InBase(d.Amount, d.Currency, rates), d.Probability, d.ProbabilityOverridden,
            d.ExpectedCloseDate, d.ClosedAt, d.LostReason, d.LostNotes, d.Description, d.AccountId, accountName, contacts, d.OwnerId, d.CreatedAt, d.StageChangedAt,
            IsRotting(d, stage, clock.UtcNow), d.Version, RecordHelpers.Custom(d.CustomFields));
    }

    private async Task<IQueryable<Deal>> ScopedAsync(CancellationToken cancellationToken)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Deals, cancellationToken);
        var query = db.Deals.AsQueryable();
        return ids is null ? query : query.Where(d => ids.Contains(d.OwnerId));
    }

    private async Task<Deal> LoadScopedAsync(Guid id, CancellationToken cancellationToken)
        => await (await ScopedAsync(cancellationToken)).FirstOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new NotFoundException("Deal", id);

    private static IQueryable<Deal> Sort(IQueryable<Deal> query, string? field, string? dir)
    {
        var descending = !string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        Expression<Func<Deal, object>> key = field switch
        {
            "name" => d => d.Name,
            "amount" => d => d.Amount,
            "expected_close_date" => d => d.ExpectedCloseDate!,
            "probability" => d => d.Probability,
            _ => d => d.CreatedAt,
        };
        return descending ? query.OrderByDescending(key).ThenBy(d => d.Id) : query.OrderBy(key).ThenBy(d => d.Id);
    }
}
