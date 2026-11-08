using System.Collections.Concurrent;

namespace MindLift.Infrastructure.Chat;

/// <summary>
/// Who has the mobile app open right now. The app pings while it is in the foreground; a person counts as
/// online only if the last ping is recent, so closing or backgrounding the app turns them offline quickly.
/// Held in memory (single instance); it resets on restart, which only means everyone shows offline until the next ping.
/// </summary>
public sealed class ChatPresence
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(75);
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();

    public void Touch(Guid userId) => _lastSeen[userId] = DateTime.UtcNow;

    public void Clear(Guid userId) => _lastSeen.TryRemove(userId, out _);

    public bool IsOnline(Guid userId) => _lastSeen.TryGetValue(userId, out var t) && DateTime.UtcNow - t <= Window;
}
