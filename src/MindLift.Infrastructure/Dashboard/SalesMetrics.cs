using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;

namespace MindLift.Infrastructure.Dashboard;

public sealed class SalesMetrics(AppDbContext db, IClock clock) : ISalesMetrics
{
    public async Task<decimal> WonAmountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Won" && d.ClosedAt >= range.StartUtc && d.ClosedAt < range.EndUtc);
        if (userIds is not null)
        {
            q = q.Where(d => userIds.Contains(d.OwnerId));
        }
        if (pipelineId is not null)
        {
            q = q.Where(d => d.PipelineId == pipelineId);
        }

        var deals = await q.Select(d => new { d.Amount, d.Currency }).ToListAsync(ct);
        if (deals.Count == 0) return 0m;

        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(ct);
        var rates = await RatesAsync(settings.BaseCurrency, ct);

        return deals.Sum(d => InBase(d.Amount, d.Currency, rates));
    }

    public async Task<int> WonCountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Won" && d.ClosedAt >= range.StartUtc && d.ClosedAt < range.EndUtc);
        if (userIds is not null) q = q.Where(d => userIds.Contains(d.OwnerId));
        if (pipelineId is not null) q = q.Where(d => d.PipelineId == pipelineId);
        return await q.CountAsync(ct);
    }

    public async Task<int> LostCountAsync(IReadOnlyList<Guid>? userIds, DateRange range, Guid? pipelineId, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Lost" && d.ClosedAt >= range.StartUtc && d.ClosedAt < range.EndUtc);
        if (userIds is not null) q = q.Where(d => userIds.Contains(d.OwnerId));
        if (pipelineId is not null) q = q.Where(d => d.PipelineId == pipelineId);
        return await q.CountAsync(ct);
    }

    public async Task<(decimal Total, decimal Weighted, int Count)> OpenPipelineAsync(IReadOnlyList<Guid>? userIds, Guid? pipelineId, CancellationToken ct)
    {
        var q = db.Deals.AsNoTracking().Where(d => d.Status == "Open");
        if (userIds is not null) q = q.Where(d => userIds.Contains(d.OwnerId));
        if (pipelineId is not null) q = q.Where(d => d.PipelineId == pipelineId);

        var deals = await q.Select(d => new { d.Amount, d.Currency, d.Probability }).ToListAsync(ct);
        if (deals.Count == 0) return (0m, 0m, 0);

        var settings = await db.TenantSettings.AsNoTracking().FirstAsync(ct);
        var rates = await RatesAsync(settings.BaseCurrency, ct);

        var total = deals.Sum(d => InBase(d.Amount, d.Currency, rates));
        var weighted = deals.Sum(d => InBase(d.Amount, d.Currency, rates) * d.Probability / 100m);

        return (total, weighted, deals.Count);
    }

    public async Task<int> NewLeadsAsync(IReadOnlyList<Guid>? userIds, DateRange range, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking().Where(l => l.CreatedAt >= range.StartUtc && l.CreatedAt < range.EndUtc);
        if (userIds is not null) q = q.Where(l => userIds.Contains(l.OwnerId));
        return await q.CountAsync(ct);
    }

    public async Task<int> ConvertedLeadsCohortAsync(IReadOnlyList<Guid>? userIds, DateRange createdRange, CancellationToken ct)
    {
        var q = db.Leads.AsNoTracking().Where(l => l.CreatedAt >= createdRange.StartUtc && l.CreatedAt < createdRange.EndUtc && l.ConvertedAt != null);
        if (userIds is not null) q = q.Where(l => userIds.Contains(l.OwnerId));
        return await q.CountAsync(ct);
    }

    public async Task<int> ActivitiesCompletedAsync(IReadOnlyList<Guid>? userIds, DateRange range, string? type, CancellationToken ct)
    {
        var q = db.Activities.AsNoTracking().Where(a => a.Status == "Completed" && a.CompletedAt != null && a.CompletedAt >= range.StartUtc && a.CompletedAt < range.EndUtc);
        if (userIds is not null) q = q.Where(a => userIds.Contains(a.OwnerId));
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(a => a.Type == type);
        return await q.CountAsync(ct);
    }

    public async Task<int> OverdueTasksAsync(IReadOnlyList<Guid>? userIds, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Activities.AsNoTracking().Where(a => a.Type == "Task" && a.Status == "Open" && a.DueAt != null && a.DueAt < now);
        if (userIds is not null) q = q.Where(a => userIds.Contains(a.OwnerId));
        return await q.CountAsync(ct);
    }

    private static decimal InBase(decimal amount, string currency, IReadOnlyDictionary<string, decimal> rates)
        => Math.Round(amount * rates.GetValueOrDefault(currency, 1m), 2);

    private async Task<Dictionary<string, decimal>> RatesAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        var today = clock.UtcNow;
        var rows = await db.ExchangeRates.AsNoTracking().Where(r => r.EffectiveDate <= today).OrderBy(r => r.EffectiveDate).ToListAsync(cancellationToken);
        var rates = rows.GroupBy(r => r.Currency).ToDictionary(g => g.Key, g => g.Last().RateToBase);
        rates[baseCurrency] = 1m;
        return rates;
    }
}
