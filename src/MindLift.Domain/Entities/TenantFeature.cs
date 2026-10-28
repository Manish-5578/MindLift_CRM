using MindLift.Domain.Common;

namespace MindLift.Domain.Entities;

/// <summary>
/// A switch for one new capability in one company. A row that does not exist means the capability is OFF,
/// so every new feature is closed until the company owner deliberately turns it on.
/// </summary>
[Audited]
public sealed class TenantFeature : TenantEntity
{
    /// <summary>Stable key from <c>FeatureCatalog</c>, for example <c>deal-stage-gating</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }
}
