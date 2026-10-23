using MindLift.Shared.Enums;

namespace MindLift.Application.Abstractions;

/// <summary>
/// What the platform owner allows the current company to use (plan 6.10). A module switched off here is unavailable to everyone in the company:
/// it is refused by the API and left out of the menu, whatever the company's own settings say.
/// </summary>
public interface IPlatformEntitlements
{
    /// <summary>True when the module is available to the current company. Core modules and an unknown company are always allowed.</summary>
    Task<bool> IsModuleAllowedAsync(AppModule module, CancellationToken cancellationToken = default);

    /// <summary>The modules the platform owner switched off for the current company.</summary>
    Task<IReadOnlySet<AppModule>> DisabledModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the company may switch on AI reading of documents.</summary>
    Task<bool> IsAiAllowedAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets what is remembered for a company, after its entitlements changed.</summary>
    ValueTask InvalidateAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
