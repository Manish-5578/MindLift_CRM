using System.Text.Json;

namespace MindLift.Shared.Contracts;

public sealed record ContactListItemDto(
    Guid Id,
    string FullName,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid? AccountId,
    string? AccountName,
    string? Type,
    Guid OwnerId,
    string? OwnerName,
    DateTime CreatedAtUtc);

public sealed record ContactDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    string? Description,
    Guid? AccountId,
    string? AccountName,
    string? Type,
    Guid OwnerId,
    DateTime CreatedAtUtc,
    IReadOnlyDictionary<string, JsonElement> CustomFields);

public sealed record SaveContactRequest(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    string? Description,
    Guid? AccountId,
    string? Type,
    Guid? OwnerId,
    IReadOnlyDictionary<string, JsonElement>? CustomFields);

public sealed record MergeContactsRequest(IReadOnlyList<Guid> DuplicateIds, IReadOnlyDictionary<string, string?>? FieldOverrides = null);

/// <summary>A lead that was converted into a contact, shown on the contact so its history is not lost.</summary>
public sealed record ConvertedLeadDto(Guid LeadId, string FullName, string? Source, DateTime CreatedAtUtc, DateTime ConvertedAtUtc);

/// <summary>Everything about one contact on one screen.</summary>
public sealed record ContactSummaryDto(ContactDto Contact, ConvertedLeadDto? ConvertedFrom, int DealCount, int ActivityCount, IReadOnlyList<DealLinkDto> Deals);

public sealed record DealLinkDto(Guid Id, string Name, string Status, decimal Amount, string Currency);

public sealed record AccountListItemDto(
    Guid Id,
    string Name,
    string? Website,
    string? Phone,
    string? Industry,
    Guid? ParentAccountId,
    string? ParentName,
    int ContactCount,
    Guid OwnerId,
    string? OwnerName,
    DateTime CreatedAtUtc);

public sealed record AccountDto(
    Guid Id,
    string Name,
    string? Website,
    string? Phone,
    string? Industry,
    string? Description,
    Guid? ParentAccountId,
    string? ParentName,
    Guid OwnerId,
    DateTime CreatedAtUtc,
    IReadOnlyDictionary<string, JsonElement> CustomFields);

public sealed record SaveAccountRequest(
    string Name,
    string? Website,
    string? Phone,
    string? Industry,
    string? Description,
    Guid? ParentAccountId,
    Guid? OwnerId,
    IReadOnlyDictionary<string, JsonElement>? CustomFields);

public sealed record AccountContactDto(Guid Id, string FullName, string? Email, string? Phone, string? JobTitle, string? Role = null, bool IsPrimary = true);

public sealed record AccountChildDto(Guid Id, string Name);

/// <summary>The account page: its contacts, child accounts, deals and activity totals (the 360° view).</summary>
public sealed record AccountSummaryDto(
    AccountDto Account,
    IReadOnlyList<AccountContactDto> Contacts,
    IReadOnlyList<AccountChildDto> Children,
    IReadOnlyList<DealLinkDto> Deals,
    decimal OpenDealsAmount,
    decimal WonDealsAmount,
    int ActivityCount);

public sealed record SearchHitDto(string Kind, Guid Id, string Title, string? Subtitle);
