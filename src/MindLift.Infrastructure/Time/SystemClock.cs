using MindLift.Application.Abstractions;

namespace MindLift.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
