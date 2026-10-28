namespace MindLift.Shared.Contracts;

public static class BookingStatus
{
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
}

public sealed record TimeRangeDto(string From, string To);

public sealed record DateOverrideDto(string Date, IReadOnlyList<TimeRangeDto> Ranges);

/// <summary>Opening hours of a booking page. Weekly is keyed mon, tue, wed, thu, fri, sat, sun. A date override with no ranges closes that day.</summary>
public sealed record AvailabilityDto(string TimeZone, IReadOnlyDictionary<string, IReadOnlyList<TimeRangeDto>> Weekly, IReadOnlyList<DateOverrideDto> DateOverrides);

public sealed record BookingQuestionDto(string Key, string Label, bool Required, string Type);

public sealed record BookingPageDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int SlotStepMinutes,
    int MinNoticeHours,
    int MaxDaysAhead,
    string AssignmentMode,
    IReadOnlyList<Guid> UserIds,
    AvailabilityDto Availability,
    IReadOnlyList<BookingQuestionDto> Questions,
    string LocationType,
    string? LocationValue,
    IReadOnlyList<int> ReminderOffsetsMinutes,
    bool IsActive,
    string PublicPath);

public sealed record SaveBookingPageRequest(
    string Slug,
    string Name,
    string? Description,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int SlotStepMinutes,
    int MinNoticeHours,
    int MaxDaysAhead,
    string AssignmentMode,
    IReadOnlyList<Guid> UserIds,
    AvailabilityDto Availability,
    IReadOnlyList<BookingQuestionDto> Questions,
    string LocationType,
    string? LocationValue,
    IReadOnlyList<int> ReminderOffsetsMinutes,
    bool IsActive);

/// <summary>What a visitor sees before choosing a time.</summary>
public sealed record PublicBookingPageDto(
    string CompanyName,
    string Name,
    string? Description,
    int DurationMinutes,
    IReadOnlyList<BookingQuestionDto> Questions,
    string LocationType,
    string? LocationValue,
    int MaxDaysAhead,
    string PageTimeZone);

public sealed record SlotDto(DateTime StartUtc, DateTime EndUtc);

public sealed record BookRequest(
    DateTime Start,
    string Name,
    string Email,
    string? Phone,
    IReadOnlyDictionary<string, string>? Answers,
    string? TimeZone,
    string? Hp);

public sealed record BookingConfirmationDto(string PageName, DateTime StartUtc, DateTime EndUtc, string ManageToken, string ManagePath);

/// <summary>A booking as the customer sees it from their manage link.</summary>
public sealed record ManageBookingDto(
    string CompanyName,
    string PageName,
    string TenantSlug,
    string PageSlug,
    string Name,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    string LocationType,
    string? LocationValue,
    int DurationMinutes,
    bool CanChange,
    string TimeZone);

public sealed record CancelBookingRequest(string? Reason, string? Token = null);

public sealed record RescheduleBookingRequest(DateTime Start, string? TimeZone, string? Token = null);

public sealed record BookingListItemDto(Guid Id, string PageName, string Name, string Email, DateTime StartUtc, DateTime EndUtc, string Status, Guid UserId, string? UserName);

/// <summary>One thing on the calendar. Kind is meeting, task or booking.</summary>
public sealed record CalendarEventDto(
    Guid Id,
    string Kind,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    Guid OwnerId,
    string? OwnerName,
    string Status,
    string? Href,
    string? LinkedName);

public sealed record ScheduleActivityRequest(DateTime StartAt, DateTime? EndAt);
