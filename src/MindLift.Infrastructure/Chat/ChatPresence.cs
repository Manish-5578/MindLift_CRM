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

    private readonly ConcurrentDictionary<Guid, (Guid To, DateTime At)> _typing = new();

    public void SetTyping(Guid from, Guid to) => _typing[from] = (to, DateTime.UtcNow);

    public void ClearTyping(Guid from) => _typing.TryRemove(from, out _);

    /// <summary>True while the sender has typed to the other person in the last few seconds.</summary>
    public bool IsTyping(Guid from, Guid to) => _typing.TryGetValue(from, out var t) && t.To == to && DateTime.UtcNow - t.At <= TimeSpan.FromSeconds(5);

    public bool IsOnline(Guid userId) => _lastSeen.TryGetValue(userId, out var t) && DateTime.UtcNow - t <= Window;
}
