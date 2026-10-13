namespace MindLift.Domain.Common;

/// <summary>
/// Entity changes are written to the audit log (plan section 4.5).
/// Apply to every tenant entity that holds business data.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AuditedAttribute : Attribute;

/// <summary>
/// Rows with this attribute are really deleted instead of soft-deleted
/// (join tables, refresh tokens, sample-data purge).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class HardDeleteAttribute : Attribute;
