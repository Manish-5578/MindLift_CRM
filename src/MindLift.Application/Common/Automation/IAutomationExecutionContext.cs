namespace MindLift.Application.Common.Automation;

/// <summary>
/// Scoped execution context to guard against infinite recursion and runaway workflow loops (Zoho Loop Protection).
/// </summary>
public interface IAutomationExecutionContext
{
    int Depth { get; }
    int TotalExecutions { get; }
    IDisposable EnterScope();
    bool CanExecute();
    void RecordExecution();
}

public sealed class AutomationExecutionContext : IAutomationExecutionContext
{
    private int _depth;
    private int _totalExecutions;

    public const int MaxDepth = 3;
    public const int MaxTotalExecutions = 25;

    public int Depth => _depth;
    public int TotalExecutions => _totalExecutions;

    public bool CanExecute() => _depth < MaxDepth && _totalExecutions < MaxTotalExecutions;

    public void RecordExecution() => _totalExecutions++;

    public IDisposable EnterScope()
    {
        _depth++;
        return new ScopeDisposable(this);
    }

    private sealed class ScopeDisposable(AutomationExecutionContext parent) : IDisposable
    {
        public void Dispose()
        {
            if (parent._depth > 0)
            {
                parent._depth--;
            }
        }
    }
}
