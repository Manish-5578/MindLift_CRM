using System.Text.Json;
using MindLift.Shared.Filtering;

namespace MindLift.Shared.Contracts;

/// <summary>Standard list query (plan 4.6.5). PageSize is capped at 200. <c>Ids</c>, when given, narrows the list to those records (an export of the rows a user ticked).</summary>
public sealed record ListQuery(
    string? Search,
    FilterGroup? Filter,
    string? SortField,
    string? SortDir,
    int Page = 1,
    int PageSize = 50,
    string? ViewId = null,
    IReadOnlyList<Guid>? Ids = null);

public sealed record LeadListItemDto(
    Guid Id,
    string FullName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string Status,
    string? Source,
    string? Rating,
    Guid OwnerId,
    string? OwnerName,
    DateTime CreatedAtUtc,
    DateTime? FirstContactedAtUtc,
    bool IsConverted,
    bool IsDisqualified,
    string FirstName,
    string LastName);

public sealed record LeadDto(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string Status,
    string? Source,
    string? Rating,
    string? Description,
    IReadOnlyList<string> Tags,
    Guid OwnerId,
    string? DisqualifyReason,
    DateTime CreatedAtUtc,
    DateTime? FirstContactedAtUtc,
    DateTime? ConvertedAtUtc,
    Guid? ConvertedContactId,
    Guid? ConvertedAccountId,
    Guid? ConvertedDealId,
    IReadOnlyDictionary<string, JsonElement> CustomFields);

public sealed record SaveLeadRequest(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? CompanyName,
    string? Status,
    string? Source,
    string? Rating,
    string? Description,
    IReadOnlyList<string>? Tags,
    Guid? OwnerId,
    IReadOnlyDictionary<string, JsonElement>? CustomFields);

public sealed record DisqualifyLeadRequest(string Reason, string? Notes);

/// <summary>How the converted records are created or linked (plan 7.3, Convert).</summary>
public sealed record ConvertLeadRequest(
    string AccountMode,
    Guid? ExistingAccountId,
    string? AccountName,
    string ContactMode,
    Guid? ExistingContactId,
    bool CreateDeal,
    string? DealName,
    Guid? PipelineId,
    Guid? StageId,
    decimal? Amount,
    string? Currency,
    DateTime? ExpectedCloseDate);

public sealed record ConvertLeadResult(Guid ContactId, Guid? AccountId, Guid? DealId);

public sealed record LeadConvertOptionsDto(
    Guid LeadId,
    string LeadName,
    string? CompanyName,
    string? Email,
    string? Phone,
    IReadOnlyList<SearchHitDto> MatchingAccounts,
    IReadOnlyList<SearchHitDto> MatchingContacts,
    IReadOnlyList<PipelineDto> AvailablePipelines,
    string BaseCurrency);

public sealed record DuplicateCandidateDto(Guid Id, string Kind, string FullName, string? Email, string? Phone, string Reason);

public sealed record CheckDuplicatesRequest(string FirstName, string LastName, string? Email, string? Phone, string? CompanyName);

public sealed record ImportResultDto(int Total, int Created, int Skipped, int Failed, IReadOnlyList<string> Errors);

public sealed record AssignmentRuleDto(Guid Id, string Name, bool IsActive, int Priority, string Method, IReadOnlyList<Guid> UserIds, FilterGroup? Conditions);

public sealed record SaveAssignmentRuleRequest(string Name, bool IsActive, int Priority, string Method, IReadOnlyList<Guid> UserIds, FilterGroup? Conditions);

public sealed record WebFormDto(Guid Id, string Name, string PublicToken, string DefaultSource, string SuccessMessage, bool IsActive, int SubmissionCount, IReadOnlyList<string> Fields);

public sealed record SaveWebFormRequest(string Name, string DefaultSource, string SuccessMessage, bool IsActive, IReadOnlyList<string> Fields);

/// <summary>Public submission. Field keys must be in the form's field list. Hp is a honeypot that must stay empty.</summary>
public sealed record PublicFormSubmission(string FirstName, string LastName, string? Email, string? Phone, string? CompanyName, string? Message, string? Hp);

public sealed record PublicFormResult(string Message, bool Duplicate);

public sealed record MergeLeadsRequest(IReadOnlyList<Guid> DuplicateIds, IReadOnlyDictionary<string, string?>? FieldOverrides = null);
