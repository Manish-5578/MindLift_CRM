namespace MindLift.Shared.Contracts;

public sealed record CalendarProviderDto(string Key, string Name, bool Configured);

public sealed record CalendarConnectionDto(Guid Id, string Provider, string AccountEmail, DateTime? LastSyncAt, string? LastError);

/// <summary>What a person sees on the calendar sync page: which services their company has set up, what they have connected, and their feed.</summary>
public sealed record CalendarSyncDto(IReadOnlyList<CalendarProviderDto> Providers, IReadOnlyList<CalendarConnectionDto> Connections, bool HasFeed);

public sealed record CalendarConnectUrlDto(string Url);

/// <summary>The feed address, shown once. Anyone who has it can read the person's meetings, so it is kept private.</summary>
public sealed record CalendarFeedDto(string Url);
