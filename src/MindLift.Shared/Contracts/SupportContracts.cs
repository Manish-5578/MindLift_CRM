namespace MindLift.Shared.Contracts;

/// <summary>Opening a read-only support view of a company. The reason is kept in the audit trail.</summary>
public sealed record StartSupportSessionRequest(string Reason);

/// <summary>The one-time code opens the session in a new tab (at /support/{Code}) within two minutes.</summary>
public sealed record SupportSessionStartedDto(Guid SessionId, string Code, DateTime ExpiresAt);

public sealed record SupportExchangeRequest(string Code);

public sealed record SupportSessionDto(Guid Id, string OpenedBy, string Reason, DateTime StartedAt, DateTime? EndedAt, IReadOnlyList<string> PagesViewed);
