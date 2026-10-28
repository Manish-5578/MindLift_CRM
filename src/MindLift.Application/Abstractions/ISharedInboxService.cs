using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

public interface ISharedInboxService
{
    Task<SharedMailboxDto> CreateMailboxAsync(CreateSharedMailboxRequest request, CancellationToken ct = default);
    Task<SharedMailboxDto> UpdateMailboxAsync(Guid id, UpdateSharedMailboxRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SharedMailboxDto>> GetMailboxesAsync(CancellationToken ct = default);
    Task<SharedMailboxDto> GetMailboxAsync(Guid id, CancellationToken ct = default);

    Task<SharedMailboxMemberDto> AddMemberAsync(Guid mailboxId, AddMailboxMemberRequest request, CancellationToken ct = default);
    Task<bool> RemoveMemberAsync(Guid mailboxId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SharedMailboxMemberDto>> GetMembersAsync(Guid mailboxId, CancellationToken ct = default);

    Task<SharedThreadDto> CreateThreadAsync(CreateSharedThreadRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SharedThreadDto>> GetThreadsAsync(SharedThreadQuery query, CancellationToken ct = default);
    Task<SharedThreadDto> GetThreadAsync(Guid id, CancellationToken ct = default);
    Task<SharedThreadDto> AssignThreadAsync(Guid id, AssignThreadRequest request, CancellationToken ct = default);
    Task<SharedThreadDto> UpdateThreadStatusAsync(Guid id, UpdateThreadStatusRequest request, CancellationToken ct = default);

    Task<SharedThreadMessageDto> PostMessageAsync(Guid threadId, PostThreadMessageRequest request, Guid currentUserId, string currentUserName, string currentUserEmail, CancellationToken ct = default);
    Task<IReadOnlyList<SharedThreadMessageDto>> GetThreadMessagesAsync(Guid threadId, CancellationToken ct = default);

    Task<CollisionStatusDto> AcquireLockAsync(Guid threadId, AcquireLockRequest request, Guid currentUserId, string currentUserName, CancellationToken ct = default);
    Task<bool> ReleaseLockAsync(Guid threadId, Guid currentUserId, CancellationToken ct = default);
    Task<CollisionStatusDto> GetCollisionStatusAsync(Guid threadId, Guid currentUserId, CancellationToken ct = default);
}
