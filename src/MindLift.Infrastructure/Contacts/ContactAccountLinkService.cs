using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Contacts;

/// <summary>
/// A contact's extra accounts. The contact must be inside the caller's contact scope and the account inside the
/// caller's account scope, so a link can never reach a record the caller could not open on its own.
/// </summary>
public sealed class ContactAccountLinkService(AppDbContext db, IDataScopeService scope)
{
    private const int MaxLinksPerContact = 50;
    private const int MaxRoleLength = 100;

    public async Task<IReadOnlyList<ContactAccountDto>> ListAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await LoadContactAsync(contactId, ct);
        var accounts = await ScopedAccountsAsync(ct);
        var result = new List<ContactAccountDto>();

        if (contact.AccountId is { } primaryId)
        {
            var name = await accounts.Where(a => a.Id == primaryId).Select(a => a.Name).FirstOrDefaultAsync(ct);
            if (name is not null)
            {
                result.Add(new ContactAccountDto(null, primaryId, name, null, true));
            }
        }

        var links = await (from l in db.ContactAccountLinks.AsNoTracking()
                           join a in accounts on l.AccountId equals a.Id
                           where l.ContactId == contactId
                           orderby l.CreatedAt
                           select new ContactAccountDto(l.Id, a.Id, a.Name, l.Role, false)).ToListAsync(ct);
        result.AddRange(links);
        return result;
    }

    public async Task<IReadOnlyList<ContactAccountDto>> AddAsync(Guid contactId, AddContactAccountRequest request, CancellationToken ct = default)
    {
        var contact = await LoadContactAsync(contactId, ct);
        var role = request.Role?.Trim();
        if (role is { Length: > MaxRoleLength })
        {
            throw new ValidationException("role", $"Role can be at most {MaxRoleLength} characters.");
        }

        if (string.IsNullOrEmpty(role))
        {
            role = null;
        }

        // The account must be visible to the caller; otherwise answer exactly as if it did not exist.
        var account = await (await ScopedAccountsAsync(ct)).FirstOrDefaultAsync(a => a.Id == request.AccountId, ct)
            ?? throw new NotFoundException("Account", request.AccountId);

        if (contact.AccountId == account.Id)
        {
            throw new ValidationException("accountId", "That is already the contact's primary account.");
        }

        if (await db.ContactAccountLinks.AnyAsync(l => l.ContactId == contactId && l.AccountId == account.Id, ct))
        {
            throw new ConflictException("already_linked", "The contact is already linked to that account.");
        }

        if (await db.ContactAccountLinks.CountAsync(l => l.ContactId == contactId, ct) >= MaxLinksPerContact)
        {
            throw new ValidationException("accountId", $"A contact can have at most {MaxLinksPerContact} extra accounts.");
        }

        db.ContactAccountLinks.Add(new ContactAccountLink { ContactId = contactId, AccountId = account.Id, Role = role });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race against the unique index.
            throw new ConflictException("already_linked", "The contact is already linked to that account.");
        }

        return await ListAsync(contactId, ct);
    }

    public async Task RemoveAsync(Guid contactId, Guid linkId, CancellationToken ct = default)
    {
        await LoadContactAsync(contactId, ct);
        var link = await db.ContactAccountLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.ContactId == contactId, ct)
            ?? throw new NotFoundException("Link", linkId);
        link.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Contact> LoadContactAsync(Guid id, CancellationToken ct)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Contacts, ct);
        var query = db.Contacts.AsQueryable();
        if (ids is not null)
        {
            query = query.Where(c => ids.Contains(c.OwnerId));
        }

        return await query.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Contact", id);
    }

    private async Task<IQueryable<Account>> ScopedAccountsAsync(CancellationToken ct)
    {
        var ids = await scope.GetScopeUserIdsAsync(AppModule.Accounts, ct);
        var query = db.Accounts.AsNoTracking();
        return ids is null ? query : query.Where(a => ids.Contains(a.OwnerId));
    }
}
