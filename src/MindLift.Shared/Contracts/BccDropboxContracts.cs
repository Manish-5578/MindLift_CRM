namespace MindLift.Shared.Contracts;

public sealed record BccDropboxAddressDto(
    Guid Id,
    Guid UserId,
    string DropboxToken,
    string DropboxEmail,
    bool IsActive,
    int TotalEmailsReceived,
    DateTime? LastReceivedAtUtc);

public sealed record BccDropboxSettingsDto(
    string DropboxDomain,
    bool AutoCreateLead,
    bool AutoCreateContact,
    bool MatchDeals,
    string DefaultPrivacy,
    string[] ExcludedDomains,
    bool LogNonMatched);

public sealed record UpdateBccDropboxSettingsRequest(
    string? DropboxDomain,
    bool AutoCreateLead,
    bool AutoCreateContact,
    bool MatchDeals,
    string? DefaultPrivacy,
    string[]? ExcludedDomains,
    bool LogNonMatched);

public sealed record InboundBccEmailRequest
{
    public string Token { get; init; } = string.Empty;
    public string From { get; init; } = string.Empty;
    public string[] To { get; init; } = [];
    public string[]? Cc { get; init; }
    public string? Subject { get; init; }
    public string? BodyPlain { get; init; }
    public string? BodyHtml { get; init; }
    public string? MessageId { get; init; }
    public DateTime? DateUtc { get; init; }
    public string[]? Attachments { get; init; }
}

public sealed record InboundBccEmailResult(
    bool Success,
    string ActionTaken,
    Guid? MatchedLeadId,
    Guid? MatchedContactId,
    Guid? MatchedAccountId,
    Guid? MatchedDealId,
    Guid? CreatedMessageId,
    string? Message);

public sealed record BccDropboxLogDto(
    Guid Id,
    Guid UserId,
    string FromAddress,
    string[] RecipientAddresses,
    string Subject,
    Guid? MatchedLeadId,
    Guid? MatchedContactId,
    Guid? MatchedAccountId,
    Guid? MatchedDealId,
    string ActionTaken,
    DateTime ReceivedAtUtc);
