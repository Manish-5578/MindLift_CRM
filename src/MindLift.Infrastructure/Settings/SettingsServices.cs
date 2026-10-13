using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Application.Common.Time;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;
using MindLift.Shared.Validation;

namespace MindLift.Infrastructure.Settings;

/// <summary>
/// A per-tenant version token. Every settings change calls <see cref="BumpAsync"/>, which invalidates
/// every cached bootstrap payload for the tenant (plan 7.2).
/// </summary>
public sealed class SettingsVersion(HybridCache cache, ITenantContext tenant)
{
    private string Key => $"{tenant.TenantId}:settings-version";

    public async ValueTask<string> CurrentAsync(CancellationToken cancellationToken = default)
        => (await cache.GetOrCreateAsync(Key, async _ => Guid.NewGuid().ToString("N"), cancellationToken: cancellationToken));

    public ValueTask BumpAsync(CancellationToken cancellationToken = default)
        => cache.RemoveAsync(Key, cancellationToken);
}

/// <summary>Company profile, currencies, exchange rates and module switches (plan 7.2).</summary>
public sealed class CompanySettingsService(AppDbContext db, ITenantContext tenant, SettingsVersion version, IClock clock)
{
    private static readonly string[] Languages = ["en", "ar"];

    public async Task<CompanySettingsDto> GetCompanyAsync(CancellationToken cancellationToken = default)
        => MapCompany(await LoadSettingsAsync(cancellationToken));

    public async Task<CompanySettingsDto> UpdateCompanyAsync(SaveCompanySettingsRequest request, CancellationToken cancellationToken = default)
    {
        var row = await LoadSettingsAsync(cancellationToken);

        if (!Languages.Contains(request.DefaultLanguage))
        {
            throw Invalid("defaultLanguage", "Language must be en or ar.");
        }

        if (!TimeZoneRules.Exists(request.TimeZoneId))
        {
            throw Invalid("timeZoneId", "Unknown time zone.");
        }

        if (request.WeekStartDay is < 0 or > 6)
        {
            throw Invalid("weekStartDay", "Week start must be 0 (Sunday) to 6 (Saturday).");
        }

        if (request.FiscalYearStartMonth is < 1 or > 12)
        {
            throw Invalid("fiscalYearStartMonth", "Fiscal year start must be a month from 1 to 12.");
        }

        if (request.DefaultTaxRate is < 0 or > 100)
        {
            throw Invalid("defaultTaxRate", "Tax rate must be between 0 and 100.");
        }

        if (BrandColor.Validate(request.PrimaryColor) is { } colorError)
        {
            throw Invalid("primaryColor", colorError);
        }

        row.CompanyName = request.CompanyName.Trim();
        row.Email = request.Email;
        row.Phone = request.Phone;
        row.DefaultLanguage = request.DefaultLanguage;
        row.TimeZoneId = request.TimeZoneId;
        row.WeekStartDay = request.WeekStartDay;
        row.FiscalYearStartMonth = request.FiscalYearStartMonth;
        row.TaxEnabled = request.TaxEnabled;
        row.TaxLabel = request.TaxLabel;
        row.DefaultTaxRate = request.DefaultTaxRate;
        row.TaxRegistrationNumber = request.TaxRegistrationNumber;
        row.PrimaryColor = string.IsNullOrWhiteSpace(request.PrimaryColor) ? null : request.PrimaryColor.ToUpperInvariant();

        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return MapCompany(row);
    }

    public async Task<CurrencySettingsDto> GetCurrenciesAsync(CancellationToken cancellationToken = default)
    {
        var row = await LoadSettingsAsync(cancellationToken);
        return new CurrencySettingsDto(row.BaseCurrency, row.EnabledCurrencies);
    }

    public async Task<CurrencySettingsDto> UpdateCurrenciesAsync(SaveCurrencySettingsRequest request, CancellationToken cancellationToken = default)
    {
        var row = await LoadSettingsAsync(cancellationToken);
        var base_ = request.BaseCurrency.Trim().ToUpperInvariant();
        var enabled = request.EnabledCurrencies.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToArray();

        if (!IsIsoCode(base_) || enabled.Any(c => !IsIsoCode(c)))
        {
            throw Invalid("currencies", "Currency codes must be three letters, for example AED.");
        }

        if (!enabled.Contains(base_))
        {
            enabled = [.. enabled, base_];
        }

        row.BaseCurrency = base_;
        row.EnabledCurrencies = enabled;
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return new CurrencySettingsDto(row.BaseCurrency, row.EnabledCurrencies);
    }

    public async Task<IReadOnlyList<ExchangeRateDto>> ListExchangeRatesAsync(CancellationToken cancellationToken = default)
        => (await db.ExchangeRates.AsNoTracking().OrderByDescending(r => r.EffectiveDate).ThenBy(r => r.Currency).ToListAsync(cancellationToken))
            .Select(r => new ExchangeRateDto(r.Id, r.Currency, r.RateToBase, r.EffectiveDate)).ToList();

    public async Task<ExchangeRateDto> AddExchangeRateAsync(SaveExchangeRateRequest request, CancellationToken cancellationToken = default)
    {
        var currency = request.Currency.Trim().ToUpperInvariant();
        var settings = await LoadSettingsAsync(cancellationToken);
        if (currency == settings.BaseCurrency)
        {
            throw Invalid("currency", "The base currency does not need a rate.");
        }

        if (!settings.EnabledCurrencies.Contains(currency))
        {
            throw new BusinessRuleException("currency_not_enabled", $"Enable {currency} in Currencies before adding a rate.");
        }

        if (request.RateToBase <= 0)
        {
            throw Invalid("rateToBase", "The rate must be greater than zero.");
        }

        var rate = new ExchangeRate { Currency = currency, RateToBase = request.RateToBase, EffectiveDate = DateTime.SpecifyKind(request.EffectiveDate.Date, DateTimeKind.Utc) };
        db.ExchangeRates.Add(rate);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return new ExchangeRateDto(rate.Id, rate.Currency, rate.RateToBase, rate.EffectiveDate);
    }

    public async Task DeleteExchangeRateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rate = await db.ExchangeRates.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException("Exchange rate", id);
        db.ExchangeRates.Remove(rate);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ModuleSettingDto>> ListModulesAsync(CancellationToken cancellationToken = default)
        => (await db.ModuleSettings.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(cancellationToken))
            .Select(MapModule).ToList();

    public async Task<ModuleSettingDto> UpdateModuleAsync(string module, SaveModuleSettingRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<MindLift.Shared.Enums.AppModule>(module, out var parsed))
        {
            throw new NotFoundException("Module", module);
        }

        var row = await db.ModuleSettings.FirstOrDefaultAsync(m => m.Module == parsed, cancellationToken)
            ?? throw new NotFoundException("Module", module);

        if (string.IsNullOrWhiteSpace(request.SingularLabel) || string.IsNullOrWhiteSpace(request.PluralLabel))
        {
            throw Invalid("label", "Both labels are required.");
        }

        // Dashboard, Users and Settings are how an admin runs the workspace. Switching them off would lock everyone out of fixing it.
        if (!request.IsEnabled && parsed is MindLift.Shared.Enums.AppModule.Dashboard or MindLift.Shared.Enums.AppModule.Users or MindLift.Shared.Enums.AppModule.Settings)
        {
            throw new BusinessRuleException("core_module", "Dashboard, Users and Settings are always on.");
        }

        row.IsEnabled = request.IsEnabled;
        row.SingularLabel = request.SingularLabel.Trim();
        row.PluralLabel = request.PluralLabel.Trim();
        row.SortOrder = request.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return MapModule(row);
    }

    internal async Task<TenantSettings> LoadSettingsAsync(CancellationToken cancellationToken)
        => await db.TenantSettings.FirstOrDefaultAsync(cancellationToken)
           ?? throw new NotFoundException("Settings", tenant.TenantId);

    private static CompanySettingsDto MapCompany(TenantSettings s) => new(
        s.CompanyName, s.Email, s.Phone, s.BaseCurrency, s.DefaultLanguage, s.TimeZoneId, s.WeekStartDay,
        s.FiscalYearStartMonth, s.TaxEnabled, s.TaxLabel, s.DefaultTaxRate, s.TaxRegistrationNumber, s.PrimaryColor);

    private static ModuleSettingDto MapModule(ModuleSetting m) => new(
        m.Module.ToString(), m.IsEnabled, m.SingularLabel, m.PluralLabel, m.SortOrder);

    private static bool IsIsoCode(string code) => code.Length == 3 && code.All(char.IsAsciiLetterUpper);

    internal static ValidationException Invalid(string key, string message)
        => new(new Dictionary<string, string[]> { [key] = [message] });
}

/// <summary>Saved list views (plan 4.6.6). Shared views are visible to everyone with access to the entity.</summary>
public sealed class SavedViewService(AppDbContext db, ITenantContext tenant, SettingsVersion version)
{
    public async Task<IReadOnlyList<SavedViewDto>> ListAsync(string entityKey, CancellationToken cancellationToken = default)
    {
        SystemFields.Get(entityKey);
        var me = tenant.UserId;
        var rows = await db.SavedViews.AsNoTracking()
            .Where(v => v.EntityType == entityKey && (v.IsShared || v.OwnerUserId == me))
            .OrderBy(v => v.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<SavedViewDto> CreateAsync(SaveSavedViewRequest request, CancellationToken cancellationToken = default)
    {
        SystemFields.Get(request.EntityType);
        var view = new SavedView
        {
            EntityType = request.EntityType,
            Name = request.Name.Trim(),
            // The creator is always recorded; IsShared only controls who else can see the view.
            OwnerUserId = tenant.UserId,
            IsShared = request.IsShared,
            IsDefault = request.IsDefault,
            Filter = request.Filter is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(request.Filter)),
            Columns = JsonSerializer.Serialize(request.Columns),
            SortField = request.SortField,
            SortDir = request.SortDir,
        };

        db.SavedViews.Add(view);
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);
        return Map(view);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var view = await db.SavedViews.FirstOrDefaultAsync(v => v.Id == id, cancellationToken) ?? throw new NotFoundException("View", id);
        if (view.OwnerUserId != tenant.UserId)
        {
            throw new ForbiddenException("You can only delete views you created.");
        }

        db.SavedViews.Remove(view);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static SavedViewDto Map(SavedView v) => new(
        v.Id,
        v.EntityType,
        v.Name,
        v.IsShared,
        v.IsDefault,
        v.OwnerUserId,
        v.Filter is null ? null : JsonSerializer.Deserialize<FilterGroup>(v.Filter.RootElement.GetRawText()),
        JsonSerializer.Deserialize<List<string>>(v.Columns) ?? [],
        v.SortField,
        v.SortDir);
}

