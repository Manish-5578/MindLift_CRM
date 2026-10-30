namespace MindLift.Web.Services;

public sealed class QuickLogDockState
{
    public bool IsOpen { get; private set; }
    public string? TargetType { get; private set; }
    public Guid? TargetId { get; private set; }
    public string? TargetName { get; private set; }
    public string InitialTab { get; private set; } = "call";

    public event Action? OnStateChanged;

    public void Open(string targetType, Guid targetId, string? targetName = null, string initialTab = "call")
    {
        TargetType = targetType.ToLowerInvariant();
        TargetId = targetId;
        TargetName = targetName;
        InitialTab = initialTab;
        IsOpen = true;
        OnStateChanged?.Invoke();
    }

    public void OpenGeneric(string initialTab = "call")
    {
        InitialTab = initialTab;
        IsOpen = true;
        OnStateChanged?.Invoke();
    }

    public void Close()
    {
        IsOpen = false;
        OnStateChanged?.Invoke();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        OnStateChanged?.Invoke();
    }
}
