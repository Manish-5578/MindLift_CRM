namespace MindLift.Application.Abstractions;

/// <summary>Remembers work that has already been started so retries and replays do nothing twice. Always for the current company.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// True the first time this scope and key is seen (the caller should do the work); false when it was already seen and has not expired.
    /// Two calls at the same moment cannot both get true.
    /// </summary>
    Task<bool> TryBeginAsync(string scope, string key, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    /// <summary>Marks the work as finished.</summary>
    Task CompleteAsync(string scope, string key, CancellationToken cancellationToken = default);

    /// <summary>Removes records that have expired, for every company. Run by a background job, not by a request.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>What the vault shows about a stored credential. The secret itself is never part of this.</summary>
public sealed record CredentialInfo(Guid Id, string Provider, string Label, bool IsActive, string? SettingsJson, DateTime? LastUsedAt, DateTime? RotatedAt);

/// <summary>
/// Keeps the keys and tokens a company gives us for other services, encrypted, one set per company.
/// For server features only: nothing here is shown to a browser, and the secret can be read back only by code that asks for it by name.
/// </summary>
public interface ICredentialVault
{
    /// <summary>Saves a secret, or replaces the one with the same provider and label (and records the change as a rotation).</summary>
    Task<CredentialInfo> StoreAsync(string provider, string label, string secret, string? settingsJson = null, CancellationToken cancellationToken = default);

    /// <summary>The decrypted secret, or null when there is none or it was revoked.</summary>
    Task<string?> GetSecretAsync(string provider, string label, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CredentialInfo>> ListAsync(string? provider = null, CancellationToken cancellationToken = default);

    /// <summary>Deactivates the credential and erases the stored secret. Returns false when there was none.</summary>
    Task<bool> RevokeAsync(string provider, string label, CancellationToken cancellationToken = default);
}

/// <summary>What a feature reports to the timeline of a record.</summary>
public sealed record TimelineEventInput(
    string EntityType,
    Guid EntityId,
    string Kind,
    string Summary,
    string Source,
    string? DetailJson = null,
    Guid? ActorUserId = null,
    DateTime? OccurredAt = null);

public sealed record TimelineEventDto(Guid Id, string EntityType, Guid EntityId, string Kind, string Summary, string Source, string? DetailJson, Guid? ActorUserId, DateTime OccurredAt);

/// <summary>
/// One place for features to write "something happened to this record". The caller must already have checked that the person
/// or the system may touch the record; reading is by record, so whoever shows a timeline must check the record is visible first.
/// </summary>
public interface ITimelineEventBus
{
    Task<TimelineEventDto> PublishAsync(TimelineEventInput input, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineEventDto>> ListAsync(string entityType, Guid entityId, int take = 50, CancellationToken cancellationToken = default);
}

/// <summary>The answer to "should this incoming webhook be processed?".</summary>
public enum WebhookDecision
{
    /// <summary>New and recent: process it.</summary>
    Accepted,

    /// <summary>Seen before: reply OK and do nothing.</summary>
    Duplicate,

    /// <summary>Too old or from the future: refuse it, it may be a replay.</summary>
    Stale,
}

/// <summary>Replay protection for incoming webhooks, after the caller has verified the signature. Runs for the current company.</summary>
public interface IWebhookIngress
{
    Task<WebhookDecision> AcceptAsync(string provider, string eventId, DateTimeOffset sentAt, CancellationToken cancellationToken = default);
}
