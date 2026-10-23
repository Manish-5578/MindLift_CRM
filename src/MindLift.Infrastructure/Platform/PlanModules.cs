using MindLift.Domain.Entities;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Platform;

/// <summary>What a company can use is what its plan includes, less anything the platform owner switched off for that company.</summary>
public static class PlanModules
{
    /// <summary>The modules the plan leaves out. A plan that lists none includes everything.</summary>
    public static IReadOnlyList<string> Excluded(Plan? plan)
    {
        if (plan is null || plan.IncludedModules.Length == 0)
        {
            return [];
        }

        return ModuleCatalog.Switchable.Select(m => m.Module.ToString()).Where(m => !plan.IncludedModules.Contains(m, StringComparer.Ordinal)).ToList();
    }

    /// <summary>Every module the company cannot use: its plan leaves it out, or the owner switched it off.</summary>
    public static string[] Disabled(Tenant tenant, Plan? plan)
        => tenant.PlatformDisabledModules.Concat(Excluded(plan)).Distinct(StringComparer.Ordinal).ToArray();
}
