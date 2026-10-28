namespace MindLift.Shared.Contracts;

/// <summary>An account a contact is tied to. <paramref name="IsPrimary"/> is the contact's own account.</summary>
public sealed record ContactAccountDto(Guid? LinkId, Guid AccountId, string AccountName, string? Role, bool IsPrimary);

/// <summary>Adds an extra account to a contact.</summary>
public sealed record AddContactAccountRequest(Guid AccountId, string? Role);
