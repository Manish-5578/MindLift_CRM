using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface IMeetingLinkService
{
    Task<MeetingLinkDto> CreateMeetingLinkAsync(CreateMeetingLinkRequest request, Guid userId, string userName, CancellationToken ct = default);
    Task<MeetingLinkDto> UpdateMeetingLinkAsync(Guid id, UpdateMeetingLinkRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MeetingLinkDto>> GetMeetingLinksAsync(MeetingLinkQuery query, CancellationToken ct = default);
    Task<MeetingLinkDto> GetMeetingLinkAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteMeetingLinkAsync(Guid id, CancellationToken ct = default);

    Task<bool> TrackClickAsync(string slug, CancellationToken ct = default);
    Task<PublicMeetingLinkDto> GetPublicMeetingLinkAsync(string slug, CancellationToken ct = default);
    Task<BookingConfirmationDto> BookThroughLinkAsync(string slug, BookThroughMeetingLinkRequest request, CancellationToken ct = default);
}
