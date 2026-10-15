using Microsoft.Extensions.DependencyInjection;

namespace MindLift.UI.Localization;

public static class LocalizationServiceExtensions
{
    /// <summary>Adds the translator the shared components use. One instance per app session, so every screen sees the same language.</summary>
    public static IServiceCollection AddMindLiftLocalization(this IServiceCollection services)
        => services.AddScoped<Translator>();
}
