using MindLift.Shared.Contracts;

namespace MindLift.Application.Abstractions;

/// <summary>
/// Which new capabilities the current company has turned on. Everything is OFF until switched on,
/// an unknown key is always OFF, and with no company set the answer is always OFF.
/// </summary>
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureFlagDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns a capability on or off. Throws a validation error for a key that is not in the catalog.</summary>
    Task<FeatureFlagDto> SetAsync(string key, bool enabled, CancellationToken cancellationToken = default);
}
