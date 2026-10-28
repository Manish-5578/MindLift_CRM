using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Platform;

public sealed class OfflineSyncService(
    AppDbContext db,
    ITenantContext tenantContext) : IOfflineSyncService
{
    public Task<PwaManifestDto> GetPwaManifestAsync(CancellationToken ct = default)
    {
        var manifest = new PwaManifestDto(
            Name: "MindLift CRM",
            ShortName: "MindLift",
            StartUrl: "/",
            Display: "standalone",
            ThemeColor: "#111827",
            BackgroundColor: "#0F172A",
            Icons:
            [
                new PwaIconDto("/icons/icon-192.png", "192x192", "image/png", "any maskable"),
                new PwaIconDto("/icons/icon-512.png", "512x512", "image/png", "any maskable")
            ]);

        return Task.FromResult(manifest);
    }

    public async Task<OfflineBootstrapDto> GetBootstrapAsync(CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var currency = settings?.BaseCurrency ?? "USD";

        var rawPipelines = await db.Pipelines.AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);

        var rawStages = await db.PipelineStages.AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        var pipelines = rawPipelines.Select(p => new PipelineDto(
            Id: p.Id,
            Name: p.Name,
            NameAr: p.NameAr,
            IsDefault: p.IsDefault,
            IsActive: p.IsActive,
            SortOrder: p.SortOrder,
            Stages: rawStages.Where(s => s.PipelineId == p.Id).Select(s => new PipelineStageDto(
                Id: s.Id,
                Name: s.Name,
                NameAr: s.NameAr,
                SortOrder: s.SortOrder,
                Probability: s.Probability,
                StageType: s.StageType,
                Color: s.Color,
                RottingDays: s.RottingDays)).ToList())).ToList();

        var records = new List<OfflineRecordDto>();

        // Leads
        var leads = await db.Leads.AsNoTracking().Take(100).ToListAsync(ct);
        foreach (var l in leads)
        {
            records.Add(new OfflineRecordDto(
                Id: l.Id,
                EntityType: "lead",
                Title: $"{l.FirstName} {l.LastName}".Trim(),
                Subtitle: l.CompanyName ?? l.Email,
                Amount: null,
                Status: l.Status,
                Version: l.Version,
                UpdatedAtUtc: l.CreatedAt,
                Data: null));
        }

        // Deals
        var deals = await db.Deals.AsNoTracking().Take(100).ToListAsync(ct);
        foreach (var d in deals)
        {
            records.Add(new OfflineRecordDto(
                Id: d.Id,
                EntityType: "deal",
                Title: d.Name,
                Subtitle: d.Status,
                Amount: d.Amount,
                Status: d.Status,
                Version: d.Version,
                UpdatedAtUtc: d.StageChangedAt == default ? d.CreatedAt : d.StageChangedAt,
                Data: null));
        }

        // Contacts
        var contacts = await db.Contacts.AsNoTracking().Take(100).ToListAsync(ct);
        foreach (var c in contacts)
        {
            records.Add(new OfflineRecordDto(
                Id: c.Id,
                EntityType: "contact",
                Title: $"{c.FirstName} {c.LastName}".Trim(),
                Subtitle: c.Email ?? c.Phone,
                Amount: null,
                Status: null,
                Version: c.Version,
                UpdatedAtUtc: c.CreatedAt,
                Data: null));
        }

        return new OfflineBootstrapDto(
            SnapshotUtc: DateTime.UtcNow,
            BaseCurrency: currency,
            Pipelines: pipelines,
            Records: records);
    }

    public async Task<OfflineDeltaDto> GetDeltaAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var upserts = new List<OfflineRecordDto>();

        var leads = await db.Leads.AsNoTracking().Where(l => l.CreatedAt >= sinceUtc).ToListAsync(ct);
        foreach (var l in leads)
        {
            upserts.Add(new OfflineRecordDto(
                Id: l.Id,
                EntityType: "lead",
                Title: $"{l.FirstName} {l.LastName}".Trim(),
                Subtitle: l.CompanyName ?? l.Email,
                Amount: null,
                Status: l.Status,
                Version: l.Version,
                UpdatedAtUtc: l.CreatedAt,
                Data: null));
        }

        var deals = await db.Deals.AsNoTracking().Where(d => d.CreatedAt >= sinceUtc || d.StageChangedAt >= sinceUtc).ToListAsync(ct);
        foreach (var d in deals)
        {
            upserts.Add(new OfflineRecordDto(
                Id: d.Id,
                EntityType: "deal",
                Title: d.Name,
                Subtitle: d.Status,
                Amount: d.Amount,
                Status: d.Status,
                Version: d.Version,
                UpdatedAtUtc: d.StageChangedAt == default ? d.CreatedAt : d.StageChangedAt,
                Data: null));
        }

        return new OfflineDeltaDto(
            ServerTimestampUtc: DateTime.UtcNow,
            Upserts: upserts,
            DeletedIds: []);
    }

    public async Task<OfflineSyncBatchResultDto> ProcessBatchSyncAsync(OfflineSyncBatchRequest request, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var userId = tenantContext.UserId ?? Guid.Empty;
        var idMap = new Dictionary<string, Guid>();
        var results = new List<OfflineMutationResultDto>();
        int applied = 0, conflict = 0, error = 0;

        foreach (var m in request.Mutations)
        {
            var op = m.Operation.Trim().ToLowerInvariant();
            var entity = m.EntityType.Trim().ToLowerInvariant();

            try
            {
                if (op == "create")
                {
                    if (entity == "lead")
                    {
                        var root = m.Payload.RootElement;
                        var firstName = root.TryGetProperty("firstName", out var fn) ? fn.GetString() : "Offline";
                        var lastName = root.TryGetProperty("lastName", out var ln) ? ln.GetString() : "Lead";
                        var email = root.TryGetProperty("email", out var em) ? em.GetString() : null;
                        var company = root.TryGetProperty("company", out var cp) ? cp.GetString() : null;

                        var lead = new Lead
                        {
                            FirstName = firstName ?? "Offline",
                            LastName = lastName ?? "Lead",
                            Email = email,
                            CompanyName = company,
                            Status = "new",
                            OwnerId = userId
                        };

                        db.Leads.Add(lead);
                        await db.SaveChangesAsync(ct);

                        if (!string.IsNullOrWhiteSpace(m.ClientTempId))
                            idMap[m.ClientTempId] = lead.Id;

                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "applied", lead.Id, null, lead.Version));
                        applied++;
                    }
                    else if (entity == "deal")
                    {
                        var root = m.Payload.RootElement;
                        var name = root.TryGetProperty("name", out var nm) ? nm.GetString() : "Offline Deal";
                        var amount = root.TryGetProperty("amount", out var am) ? am.GetDecimal() : 0m;

                        var pipeline = await db.Pipelines.FirstOrDefaultAsync(p => p.IsDefault, ct)
                                       ?? await db.Pipelines.FirstOrDefaultAsync(ct);

                        var openStage = pipeline != null
                            ? await db.PipelineStages.FirstOrDefaultAsync(s => s.PipelineId == pipeline.Id && s.StageType == "Open", ct)
                            : null;

                        if (pipeline == null || openStage == null)
                        {
                            results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, "No open pipeline stage found", null));
                            error++;
                            continue;
                        }

                        var deal = new Deal
                        {
                            Name = name ?? "Offline Deal",
                            Amount = amount,
                            PipelineId = pipeline.Id,
                            StageId = openStage.Id,
                            Status = "Open",
                            OwnerId = userId,
                            StageChangedAt = DateTime.UtcNow
                        };

                        db.Deals.Add(deal);
                        await db.SaveChangesAsync(ct);

                        if (!string.IsNullOrWhiteSpace(m.ClientTempId))
                            idMap[m.ClientTempId] = deal.Id;

                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "applied", deal.Id, null, deal.Version));
                        applied++;
                    }
                    else if (entity == "activity")
                    {
                        var root = m.Payload.RootElement;
                        var subject = root.TryGetProperty("subject", out var sb) ? sb.GetString() : "Offline Note";

                        var act = new Activity
                        {
                            Subject = subject ?? "Offline Note",
                            Type = "Note",
                            Status = "Completed",
                            OwnerId = userId
                        };

                        db.Activities.Add(act);
                        await db.SaveChangesAsync(ct);

                        if (!string.IsNullOrWhiteSpace(m.ClientTempId))
                            idMap[m.ClientTempId] = act.Id;

                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "applied", act.Id, null, act.Version));
                        applied++;
                    }
                    else
                    {
                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, $"Unsupported entity type '{m.EntityType}'", null));
                        error++;
                    }
                }
                else if (op == "update")
                {
                    if (entity == "deal")
                    {
                        if (!m.EntityId.HasValue)
                        {
                            results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, "EntityId required for update", null));
                            error++;
                            continue;
                        }

                        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == m.EntityId.Value, ct);
                        if (deal == null)
                        {
                            results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, "Deal not found", null));
                            error++;
                            continue;
                        }

                        // Version conflict check
                        if (m.BaseVersion.HasValue && deal.Version != m.BaseVersion.Value)
                        {
                            results.Add(new OfflineMutationResultDto(m.ClientMutationId, "conflict", deal.Id, $"Conflict: server version is {deal.Version}, base was {m.BaseVersion}", deal.Version));
                            conflict++;
                            continue;
                        }

                        var root = m.Payload.RootElement;
                        if (root.TryGetProperty("amount", out var am))
                            deal.Amount = am.GetDecimal();
                        if (root.TryGetProperty("name", out var nm) && !string.IsNullOrWhiteSpace(nm.GetString()))
                            deal.Name = nm.GetString()!;

                        deal.Version++;
                        await db.SaveChangesAsync(ct);

                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "applied", deal.Id, null, deal.Version));
                        applied++;
                    }
                    else
                    {
                        results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, $"Update unsupported for entity '{m.EntityType}'", null));
                        error++;
                    }
                }
                else
                {
                    results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, $"Unsupported operation '{m.Operation}'", null));
                    error++;
                }
            }
            catch (Exception ex)
            {
                results.Add(new OfflineMutationResultDto(m.ClientMutationId, "error", null, ex.Message, null));
                error++;
            }
        }

        // Record audit batch log
        var log = new OfflineSyncBatchLog
        {
            UserId = userId,
            BatchId = request.BatchId,
            ClientMutationCount = request.Mutations.Count,
            AppliedCount = applied,
            ConflictCount = conflict,
            ErrorCount = error,
            CompletedAtUtc = DateTime.UtcNow
        };

        db.OfflineSyncBatches.Add(log);
        await db.SaveChangesAsync(ct);

        return new OfflineSyncBatchResultDto(
            BatchId: request.BatchId,
            ProcessedCount: request.Mutations.Count,
            AppliedCount: applied,
            ConflictCount: conflict,
            ErrorCount: error,
            IdMap: idMap,
            Results: results);
    }
}
