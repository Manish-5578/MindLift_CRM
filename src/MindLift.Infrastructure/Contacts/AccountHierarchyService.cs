using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;

namespace MindLift.Infrastructure.Contacts;

public sealed class AccountHierarchyService(
    AppDbContext db,
    ITenantContext tenantContext) : IAccountHierarchyService
{
    public async Task<AccountHierarchyTreeDto> GetHierarchyAsync(Guid accountId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var focusAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (focusAccount == null)
            throw new NotFoundException("account_not_found", "Account was not found.");

        // Traverse upwards to root account
        var current = focusAccount;
        var visitedAncestors = new HashSet<Guid> { current.Id };
        while (current.ParentAccountId.HasValue)
        {
            var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Id == current.ParentAccountId.Value, ct);
            if (parent == null || !visitedAncestors.Add(parent.Id))
                break;
            current = parent;
        }

        var rootAccount = current;

        // Fetch all tenant accounts to construct the tree
        var allAccounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var childrenLookup = allAccounts.Where(a => a.ParentAccountId != null).ToLookup(a => a.ParentAccountId!.Value);

        // BFS to collect all node IDs in the hierarchy rooted at rootAccount
        var subtreeIds = new HashSet<Guid> { rootAccount.Id };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootAccount.Id);
        while (queue.Count > 0)
        {
            var currId = queue.Dequeue();
            foreach (var child in childrenLookup[currId])
            {
                if (subtreeIds.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        // Aggregate related metrics
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.AccountId != null && subtreeIds.Contains(c.AccountId.Value))
            .Select(c => new { AccountId = c.AccountId!.Value })
            .ToListAsync(ct);
        var contactCounts = contacts.GroupBy(c => c.AccountId).ToDictionary(g => g.Key, g => g.Count());

        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.AccountId != null && subtreeIds.Contains(d.AccountId.Value))
            .Select(d => new { AccountId = d.AccountId!.Value, d.Status, d.Amount })
            .ToListAsync(ct);

        var tickets = await db.Tickets.AsNoTracking()
            .Where(t => t.AccountId != null && subtreeIds.Contains(t.AccountId.Value) && t.Status != "closed")
            .Select(t => new { AccountId = t.AccountId!.Value })
            .ToListAsync(ct);
        var openTicketCounts = tickets.GroupBy(t => t.AccountId).ToDictionary(g => g.Key, g => g.Count());

        var userIds = allAccounts.Where(a => subtreeIds.Contains(a.Id)).Select(a => a.OwnerId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var accountsMap = allAccounts.Where(a => subtreeIds.Contains(a.Id)).ToDictionary(a => a.Id);

        AccountHierarchyNodeDto BuildNode(Guid accId, int level)
        {
            var acc = accountsMap[accId];
            var ownerName = users.GetValueOrDefault(acc.OwnerId, "Unassigned");
            var directChildren = childrenLookup[accId].OrderBy(c => c.Name).ToList();

            var childNodes = directChildren.Select(c => BuildNode(c.Id, level + 1)).ToList();

            var directContacts = contactCounts.GetValueOrDefault(accId, 0);
            var directOpenDeals = deals.Where(d => d.AccountId == accId && d.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).ToList();
            var directWonDeals = deals.Where(d => d.AccountId == accId && d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();
            var directOpenTickets = openTicketCounts.GetValueOrDefault(accId, 0);

            var totalAccounts = 1 + childNodes.Sum(c => c.Metrics.SubtreeAccountCount);
            var totalContacts = directContacts + childNodes.Sum(c => c.Metrics.TotalContactsCount);
            var totalOpenDealsCount = directOpenDeals.Count + childNodes.Sum(c => c.Metrics.OpenDealsCount);
            var totalOpenDealsValue = directOpenDeals.Sum(d => d.Amount) + childNodes.Sum(c => c.Metrics.OpenDealsValue);
            var totalWonDealsCount = directWonDeals.Count + childNodes.Sum(c => c.Metrics.WonDealsCount);
            var totalWonDealsValue = directWonDeals.Sum(d => d.Amount) + childNodes.Sum(c => c.Metrics.WonDealsValue);
            var totalOpenTicketsCount = directOpenTickets + childNodes.Sum(c => c.Metrics.OpenTicketsCount);

            var metrics = new AccountHierarchyMetricsDto(
                SubtreeAccountCount: totalAccounts,
                DirectChildCount: childNodes.Count,
                TotalContactsCount: totalContacts,
                OpenDealsCount: totalOpenDealsCount,
                OpenDealsValue: totalOpenDealsValue,
                WonDealsCount: totalWonDealsCount,
                WonDealsValue: totalWonDealsValue,
                OpenTicketsCount: totalOpenTicketsCount);

            return new AccountHierarchyNodeDto(
                Id: acc.Id,
                Name: acc.Name,
                Industry: acc.Industry,
                Website: acc.Website,
                OwnerId: acc.OwnerId,
                OwnerName: ownerName,
                ParentAccountId: acc.ParentAccountId,
                Level: level,
                Metrics: metrics,
                Children: childNodes);
        }

        var rootNode = BuildNode(rootAccount.Id, 0);
        return new AccountHierarchyTreeDto(rootNode, focusAccount.Id, rootNode.Metrics);
    }

    public async Task<AccountHierarchyMetricsDto> GetRollupsAsync(Guid accountId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var focusAccount = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (focusAccount == null)
            throw new NotFoundException("account_not_found", "Account was not found.");

        var allAccounts = await db.Accounts.AsNoTracking().ToListAsync(ct);
        var childrenLookup = allAccounts.Where(a => a.ParentAccountId != null).ToLookup(a => a.ParentAccountId!.Value);

        var subtreeIds = new HashSet<Guid> { focusAccount.Id };
        var queue = new Queue<Guid>();
        queue.Enqueue(focusAccount.Id);
        while (queue.Count > 0)
        {
            var currId = queue.Dequeue();
            foreach (var child in childrenLookup[currId])
            {
                if (subtreeIds.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        var directContacts = await db.Contacts.AsNoTracking()
            .Where(c => c.AccountId != null && subtreeIds.Contains(c.AccountId.Value))
            .CountAsync(ct);

        var deals = await db.Deals.AsNoTracking()
            .Where(d => d.AccountId != null && subtreeIds.Contains(d.AccountId.Value))
            .Select(d => new { d.Status, d.Amount })
            .ToListAsync(ct);

        var openDeals = deals.Where(d => d.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)).ToList();
        var wonDeals = deals.Where(d => d.Status.Equals("Won", StringComparison.OrdinalIgnoreCase)).ToList();

        var openTicketsCount = await db.Tickets.AsNoTracking()
            .Where(t => t.AccountId != null && subtreeIds.Contains(t.AccountId.Value) && t.Status != "closed")
            .CountAsync(ct);

        var directChildCount = childrenLookup[focusAccount.Id].Count();

        return new AccountHierarchyMetricsDto(
            SubtreeAccountCount: subtreeIds.Count,
            DirectChildCount: directChildCount,
            TotalContactsCount: directContacts,
            OpenDealsCount: openDeals.Count,
            OpenDealsValue: openDeals.Sum(d => d.Amount),
            WonDealsCount: wonDeals.Count,
            WonDealsValue: wonDeals.Sum(d => d.Amount),
            OpenTicketsCount: openTicketsCount);
    }

    public async Task<bool> SetParentAsync(Guid accountId, Guid? parentAccountId, CancellationToken ct = default)
    {
        if (tenantContext.TenantId is not { } tenantId)
            throw new ForbiddenException();

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account == null)
            throw new NotFoundException("account_not_found", "Account was not found.");

        if (parentAccountId.HasValue)
        {
            if (parentAccountId.Value == accountId)
                throw new ValidationException("cannot_be_own_parent", "An account cannot be its own parent.");

            var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Id == parentAccountId.Value, ct);
            if (parent == null)
                throw new NotFoundException("parent_account_not_found", "Parent account was not found.");

            // Cycle detection: check if accountId is in parent's ancestry
            var current = parent;
            var visited = new HashSet<Guid> { accountId, parent.Id };
            while (current.ParentAccountId.HasValue)
            {
                if (current.ParentAccountId.Value == accountId)
                {
                    throw new ValidationException("circular_dependency", "Setting this parent would create a circular dependency in the account hierarchy.");
                }

                if (!visited.Add(current.ParentAccountId.Value))
                {
                    // Existing cycle or loop detected
                    break;
                }

                current = await db.Accounts.FirstOrDefaultAsync(a => a.Id == current.ParentAccountId.Value, ct);
                if (current == null) break;
            }
        }

        account.ParentAccountId = parentAccountId;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
