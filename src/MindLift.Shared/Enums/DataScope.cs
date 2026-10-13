namespace MindLift.Shared.Enums;

/// <summary>Which records a role can see: only its own, its team's, or all (plan section 4.4.5).</summary>
public enum DataScope
{
    Own = 1,
    Team = 2,
    All = 3,
}
