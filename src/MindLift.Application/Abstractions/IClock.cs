namespace MindLift.Application.Abstractions;

/// <summary>Source of the current time. Inject this instead of DateTime.UtcNow so tests can control time.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
