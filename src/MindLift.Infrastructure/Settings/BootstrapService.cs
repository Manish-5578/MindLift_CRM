using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Infrastructure.Auth;
using MindLift.Shared.Contracts;
using MindLift.Shared.Enums;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// The shared, tenant-wide configuration a staff app needs (modules, fields, picklists, pipelines, layouts, currencies, company).
/// Cached per tenant and settings version, so it is computed once and reused until the next settings change.
/// </summary>
public sealed class TenantConfigSnapshot
{
    public List<ModuleSettingDto> Modules { get; set; } = [];
    public Dictionary<string, List<FieldDto>> Fields { get; set; } = [];
    public List<PicklistDto> Picklists { get; set; } = [];
    public List<PipelineDto> Pipelines { get; set; } = [];
    public Dictionary<string, List<LayoutDto>> Layouts { get; set; } = [];
    public CurrencySettingsDto Currencies { get; set; } = new("USD", []);
    public CompanySettingsDto Company { get; set; } = null!;
}

/// <summary>Builds the one-call payload for login and app start (plan 7.2). The user's own part is computed per call.</summary>
public sealed class BootstrapService(
    ITenantContext tenant,
    AuthService auth,
    IPermissionService permissions,
    SettingsVersion version,
    CompanySettingsService company,
    FieldSettingsService fields,
    PicklistPipelineService picklistsAndPipelines,
    HybridCache cache)
{
    public async Task<BootstrapDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var userId = tenant.UserId ?? throw new UnauthorizedException("not_signed_in", "Please sign in.");
        var currentVersion = await version.CurrentAsync(cancellationToken);
        var snapshot = await cache.GetOrCreateAsync(
            $"{tenant.TenantId}:config:{currentVersion}",
            async token => await BuildSnapshotAsync(token),
            cancellationToken: cancellationToken);

        var me = await auth.GetMeAsync(userId, cancellationToken);
        var matrix = new List<ModulePermissionDto>();
        foreach (var module in Enum.GetValues<AppModule>())
        {
            var permission = await permissions.GetModulePermissionAsync(module, cancellationToken);
            matrix.Add(permission is null
                ? new ModulePermissionDto(module.ToString(), false, false, false, false, false, false, DataScope.Own.ToString())
                : new ModulePermissionDto(module.ToString(), permission.CanView, permission.CanCreate, permission.CanEdit, permission.CanDelete, permission.CanExport, permission.CanImport, permission.Scope.ToString()));
        }

        return new BootstrapDto(
            me,
            snapshot.Modules,
            snapshot.Fields.ToDictionary(p => p.Key, p => (IReadOnlyList<FieldDto>)p.Value),
            snapshot.Picklists,
            snapshot.Pipelines,
            snapshot.Layouts.ToDictionary(p => p.Key, p => (IReadOnlyList<LayoutDto>)p.Value),
            snapshot.Currencies,
            snapshot.Company,
            matrix,
            currentVersion);
    }

    private async Task<TenantConfigSnapshot> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = new TenantConfigSnapshot
        {
            Modules = (await company.ListModulesAsync(cancellationToken)).ToList(),
            Picklists = (await picklistsAndPipelines.ListPicklistsAsync(cancellationToken)).ToList(),
            Pipelines = (await picklistsAndPipelines.ListPipelinesAsync(cancellationToken)).ToList(),
            Currencies = await company.GetCurrenciesAsync(cancellationToken),
            Company = await company.GetCompanyAsync(cancellationToken),
        };

        foreach (var entity in MindLift.Application.Common.Fields.SystemFields.Entities)
        {
            snapshot.Fields[entity.Key] = (await fields.GetFieldsAsync(entity.Key, cancellationToken)).ToList();
            snapshot.Layouts[entity.Key] =
            [
                await fields.GetLayoutAsync(entity.Key, "Form", cancellationToken),
                await fields.GetLayoutAsync(entity.Key, "Detail", cancellationToken),
            ];
        }

        return snapshot;
    }
}
