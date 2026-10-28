using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Identity;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Deals;

/// <summary>
/// Phase 3 / #14: Deal Revenue Splits (deal-splits).
/// Service for calculating, validating, and managing revenue and overlay splits across deals.
/// </summary>
public sealed class DealSplitService(
    AppDbContext db,
    ITenantContext tenant) : IDealSplitService
{
    private static readonly HashSet<string> ValidSplitTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Revenue", "Overlay"
    };

    public async Task<DealSplitSummaryDto> GetDealSplitsAsync(Guid dealId, CancellationToken cancellationToken = default)
    {
        var deal = await db.Deals.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal", dealId);

        var splits = await db.DealSplits.AsNoTracking()
            .Where(s => s.DealId == dealId)
            .OrderBy(s => s.SplitType)
            .ThenByDescending(s => s.Percentage)
            .ToListAsync(cancellationToken);

        var userIds = splits.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var memberDtos = splits.Select(s => MapMember(s, users.GetValueOrDefault(s.UserId))).ToList();

        var revSplits = memberDtos.Where(s => string.Equals(s.SplitType, "Revenue", StringComparison.OrdinalIgnoreCase)).ToList();
        var totalRevPct = revSplits.Sum(s => s.Percentage);
        var totalRevAmt = revSplits.Sum(s => s.Amount);
        var isValid = revSplits.Count == 0 || Math.Abs(totalRevPct - 100.00m) < 0.01m;

        return new DealSplitSummaryDto(
            deal.Id,
            deal.Amount,
            deal.Currency,
            totalRevPct,
            totalRevAmt,
            isValid,
            memberDtos
        );
    }

    public async Task<DealSplitSummaryDto> SetDealSplitsAsync(Guid dealId, SetDealSplitsRequest request, CancellationToken cancellationToken = default)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal", dealId);

        var splits = request.Splits ?? [];

        // Validate duplicates and percentages
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in splits)
        {
            if (s.Percentage <= 0 || s.Percentage > 100)
                throw new ValidationException($"Percentage must be between 0.01 and 100. Got {s.Percentage}.");

            var normType = NormalizeSplitType(s.SplitType);
            var key = $"{s.UserId:N}_{normType}";
            if (!seen.Add(key))
                throw new ValidationException($"User is listed multiple times under split type '{normType}'.");
        }

        // Validate users exist in this tenant
        var userIds = splits.Select(s => s.UserId).Distinct().ToList();
        if (userIds.Count > 0)
        {
            var existingUserCount = await db.Users.CountAsync(u => userIds.Contains(u.Id), cancellationToken);
            if (existingUserCount != userIds.Count)
                throw new ValidationException("One or more split users do not exist in this workspace.");
        }

        // Validate revenue splits sum to 100%
        var revSplits = splits.Where(s => string.Equals(s.SplitType, "Revenue", StringComparison.OrdinalIgnoreCase)).ToList();
        if (revSplits.Count > 0)
        {
            var sum = revSplits.Sum(s => s.Percentage);
            if (Math.Abs(sum - 100.00m) >= 0.01m)
            {
                throw new ValidationException($"Revenue splits must total exactly 100%. Current total is {sum:F2}%.");
            }
        }

        // Replace existing splits
        var existing = await db.DealSplits.Where(s => s.DealId == dealId).ToListAsync(cancellationToken);
        db.DealSplits.RemoveRange(existing);

        foreach (var item in splits)
        {
            var amount = Math.Round(deal.Amount * (item.Percentage / 100m), 2);
            db.DealSplits.Add(new DealSplit
            {
                DealId = deal.Id,
                UserId = item.UserId,
                SplitType = NormalizeSplitType(item.SplitType),
                Percentage = item.Percentage,
                Amount = amount,
                Note = item.Note?.Trim()
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetDealSplitsAsync(dealId, cancellationToken);
    }

    public async Task<DealSplitMemberDto> AddOrUpdateSplitAsync(Guid dealId, SaveDealSplitMemberRequest request, CancellationToken cancellationToken = default)
    {
        var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal", dealId);

        if (request.Percentage <= 0 || request.Percentage > 100)
            throw new ValidationException($"Percentage must be between 0.01 and 100. Got {request.Percentage}.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException("User", request.UserId);

        var normType = NormalizeSplitType(request.SplitType);

        var existing = await db.DealSplits.FirstOrDefaultAsync(s =>
            s.DealId == dealId &&
            s.UserId == request.UserId &&
            s.SplitType == normType, cancellationToken);

        var amount = Math.Round(deal.Amount * (request.Percentage / 100m), 2);

        if (existing != null)
        {
            existing.Percentage = request.Percentage;
            existing.Amount = amount;
            existing.Note = request.Note?.Trim();
        }
        else
        {
            existing = new DealSplit
            {
                DealId = deal.Id,
                UserId = request.UserId,
                SplitType = normType,
                Percentage = request.Percentage,
                Amount = amount,
                Note = request.Note?.Trim()
            };
            db.DealSplits.Add(existing);
        }

        await db.SaveChangesAsync(cancellationToken);

        return MapMember(existing, user);
    }

    public async Task<DealSplitSummaryDto> RemoveSplitAsync(Guid dealId, Guid splitId, CancellationToken cancellationToken = default)
    {
        var row = await db.DealSplits.FirstOrDefaultAsync(s => s.Id == splitId && s.DealId == dealId, cancellationToken)
            ?? throw new NotFoundException("Deal split", splitId);

        db.DealSplits.Remove(row);
        await db.SaveChangesAsync(cancellationToken);

        return await GetDealSplitsAsync(dealId, cancellationToken);
    }

    public async Task<UserDealSplitsSummaryDto> GetUserSplitsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("User", userId);

        var rows = await db.DealSplits.AsNoTracking()
            .Include(s => s.Deal)
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.Deal.CreatedAt)
            .ToListAsync(cancellationToken);

        var userSplits = rows.Select(s => new UserDealSplitDto(
            s.DealId,
            s.Deal.Name,
            s.Deal.Status,
            s.Deal.Amount,
            s.Deal.Currency,
            s.Percentage,
            s.Amount,
            s.SplitType,
            s.Deal.ExpectedCloseDate,
            s.Deal.CreatedAt
        )).ToList();

        var totalSplitAmt = userSplits.Where(s => s.SplitType == "Revenue").Sum(s => s.Amount);

        return new UserDealSplitsSummaryDto(
            user.Id,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.Email ?? string.Empty,
            totalSplitAmt,
            userSplits.Count,
            userSplits
        );
    }

    public async Task RecalculateSplitsForDealAsync(Guid dealId, decimal newDealAmount, CancellationToken cancellationToken = default)
    {
        var splits = await db.DealSplits.Where(s => s.DealId == dealId).ToListAsync(cancellationToken);
        foreach (var s in splits)
        {
            s.Amount = Math.Round(newDealAmount * (s.Percentage / 100m), 2);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeSplitType(string? splitType)
    {
        if (string.IsNullOrWhiteSpace(splitType)) return "Revenue";
        var norm = splitType.Trim();
        if (string.Equals(norm, "Overlay", StringComparison.OrdinalIgnoreCase)) return "Overlay";
        return "Revenue";
    }

    private static DealSplitMemberDto MapMember(DealSplit s, AppUser? user)
    {
        var name = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown User";
        var email = user?.Email ?? string.Empty;
        return new DealSplitMemberDto(
            s.Id,
            s.DealId,
            s.UserId,
            name,
            email,
            s.SplitType,
            s.Percentage,
            s.Amount,
            s.Note
        );
    }
}
