using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Ocr;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Contacts;
using MindLift.Infrastructure.Deals;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Documents.Extraction;

/// <summary>
/// Saves the values read from a document into the record the document is linked to: a contact, an account or a deal. A document field names its
/// target as "contacts.first_name" (entity, then a standard or custom field key). Each record is saved through its own service, so the same validation,
/// automation and audit apply as when a person edits it, and only the fields named change.
/// </summary>
public sealed class DocumentRecordApplier(AppDbContext db, ContactService contacts, AccountService accounts, DealService deals, FieldSettingsService fields, MindLift.Application.Abstractions.IPermissionService permissions)
{
    /// <summary>A reviewer with only Documents rights must not be able to change CRM records they cannot edit directly.</summary>
    private async Task RequireEditAsync(string entity, CancellationToken cancellationToken)
    {
        var module = entity switch
        {
            Contacts => MindLift.Shared.Enums.AppModule.Contacts,
            Accounts => MindLift.Shared.Enums.AppModule.Accounts,
            _ => MindLift.Shared.Enums.AppModule.Deals,
        };
        if (!await permissions.HasAsync(module, MindLift.Shared.Enums.PermissionAction.Edit, cancellationToken))
        {
            throw new ForbiddenException();
        }
    }

    public const string Contacts = "contacts";
    public const string Accounts = "accounts";
    public const string Deals = "deals";

    private static readonly string[] Entities = [Contacts, Accounts, Deals];

    private static RecordTargetDto Std(string entity, string key, string label, string type) => new(entity, key, label, type, false);

    /// <summary>The standard record fields a document value can be saved to.</summary>
    private static readonly IReadOnlyList<RecordTargetDto> Standard =
    [
        Std(Contacts, "first_name", "First name", DocumentValueTypes.Text),
        Std(Contacts, "last_name", "Last name", DocumentValueTypes.Text),
        Std(Contacts, "email", "Email", DocumentValueTypes.Email),
        Std(Contacts, "phone", "Phone", DocumentValueTypes.Phone),
        Std(Contacts, "job_title", "Job title", DocumentValueTypes.Text),
        Std(Contacts, "description", "Description", DocumentValueTypes.Text),
        Std(Accounts, "name", "Name", DocumentValueTypes.Text),
        Std(Accounts, "website", "Website", DocumentValueTypes.Text),
        Std(Accounts, "phone", "Phone", DocumentValueTypes.Phone),
        Std(Accounts, "industry", "Industry", DocumentValueTypes.Text),
        Std(Accounts, "description", "Description", DocumentValueTypes.Text),
        Std(Deals, "name", "Name", DocumentValueTypes.Text),
        Std(Deals, "amount", "Amount", DocumentValueTypes.Amount),
        Std(Deals, "expected_close_date", "Expected close date", DocumentValueTypes.Date),
        Std(Deals, "description", "Description", DocumentValueTypes.Text),
    ];

    /// <summary>The target as "entity.key", split. Null when it is not in that form.</summary>
    public static (string Entity, string Key)? Split(string? target)
    {
        var dot = target?.IndexOf('.') ?? -1;
        return target is null || dot <= 0 || dot == target.Length - 1 ? null : (target[..dot].ToLowerInvariant(), target[(dot + 1)..]);
    }

    /// <summary>Every field a document value can be saved to: the standard ones above and the workspace's own custom fields on those records.</summary>
    public async Task<IReadOnlyList<RecordTargetDto>> TargetsAsync(CancellationToken cancellationToken)
    {
        var custom = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => Entities.Contains(d.EntityType) && d.IsActive)
            .OrderBy(d => d.EntityType).ThenBy(d => d.SortOrder)
            .ToListAsync(cancellationToken);

        var list = new List<RecordTargetDto>(Standard);
        foreach (var d in custom)
        {
            if (ValueTypeOf(Enum.Parse<FieldType>(d.FieldType)) is { } type)
            {
                list.Add(new RecordTargetDto(d.EntityType, d.Key, d.Label, type, true));
            }
        }

        return list;
    }

    /// <summary>The kind of value a custom field holds, in the document's terms. Null for kinds a document value cannot fill (lookups, tags, yes/no, lists of options).</summary>
    private static string? ValueTypeOf(FieldType type) => type switch
    {
        FieldType.Text or FieldType.TextArea or FieldType.Url or FieldType.Picklist => DocumentValueTypes.Text,
        FieldType.Email => DocumentValueTypes.Email,
        FieldType.Phone => DocumentValueTypes.Phone,
        FieldType.Number or FieldType.Decimal or FieldType.Percent => DocumentValueTypes.Number,
        FieldType.Currency => DocumentValueTypes.Amount,
        FieldType.Date or FieldType.DateTime => DocumentValueTypes.Date,
        _ => null,
    };

    /// <summary>True when a value of the document field's kind can be put in a record field of the target's kind.</summary>
    public static bool Compatible(string documentType, string targetType) => DocumentValueTypes.Compatible(documentType, targetType);

    /// <summary>
    /// Writes the values to the linked records. Nothing is changed if a record cannot take a value: the caller approves the document only when this returns.
    /// The older target "accounts.trade_license_number" is still understood and adds the number to the account's description.
    /// </summary>
    public async Task ApplyAsync(BusinessDocument document, IReadOnlyList<ApplyFieldToRecordItem> items, CancellationToken cancellationToken)
    {
        var wanted = items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var targets = await TargetsAsync(cancellationToken);
        var grouped = new Dictionary<string, List<(RecordTargetDto Target, ApplyFieldToRecordItem Item)>>();
        foreach (var item in wanted)
        {
            if (string.Equals(item.TargetField, "accounts.trade_license_number", StringComparison.OrdinalIgnoreCase))
            {
                await AppendLicenceAsync(document, item, cancellationToken);
                continue;
            }

            var split = Split(item.TargetField);
            var target = split is { } s ? targets.FirstOrDefault(t => t.Entity == s.Entity && string.Equals(t.Key, s.Key, StringComparison.OrdinalIgnoreCase)) : null;
            if (target is null)
            {
                throw new ValidationException("applyFields", $"{item.FieldName} cannot be saved to \"{item.TargetField}\". Choose a field of a contact, account or deal in Settings > Document types.");
            }

            if (!grouped.TryGetValue(target.Entity, out var list))
            {
                grouped[target.Entity] = list = [];
            }

            list.Add((target, item));
        }

        // All records or none: if one cannot take its value, the ones already saved are rolled back.
        foreach (var entity in grouped.Keys)
        {
            await RequireEditAsync(entity, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var (entity, list) in grouped)
        {
            var id = entity switch
            {
                Contacts => document.AssociatedContactId,
                Accounts => document.AssociatedAccountId,
                _ => document.AssociatedDealId,
            };
            if (id is null)
            {
                var names = string.Join(", ", list.Select(l => l.Item.FieldName));
                throw new ValidationException("applyFields", $"{names} would be saved to a {Singular(entity)}, but this document is not linked to one. Link it to a {Singular(entity)} first.");
            }

            switch (entity)
            {
                case Contacts: await ApplyToContactAsync(id.Value, list, cancellationToken); break;
                case Accounts: await ApplyToAccountAsync(id.Value, list, cancellationToken); break;
                default: await ApplyToDealAsync(id.Value, list, cancellationToken); break;
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task AppendLicenceAsync(BusinessDocument document, ApplyFieldToRecordItem item, CancellationToken cancellationToken)
    {
        if (document.AssociatedAccountId is not { } accountId)
        {
            return;
        }

        await RequireEditAsync(Accounts, cancellationToken);
        await accounts.GetAsync(accountId, cancellationToken); // 404s when the account is outside the caller's scope.
        var account = await db.Set<Account>().FirstOrDefaultAsync(a => a.Id == accountId && !a.IsDeleted, cancellationToken);
        if (account is not null)
        {
            account.Description = (account.Description ?? string.Empty) + $" [License: {item.Value}]";
        }
    }

    private async Task ApplyToContactAsync(Guid id, List<(RecordTargetDto Target, ApplyFieldToRecordItem Item)> list, CancellationToken cancellationToken)
    {
        var c = await contacts.GetAsync(id, cancellationToken);
        var save = new SaveContactRequest(c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.Description, c.AccountId, c.Type, c.OwnerId, c.CustomFields);
        var custom = c.CustomFields.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (target, item) in list)
        {
            var value = Stored(target, item);
            switch (target.IsCustom ? "custom" : target.Key)
            {
                case "first_name": save = save with { FirstName = value }; break;
                case "last_name": save = save with { LastName = value }; break;
                case "email": save = save with { Email = value }; break;
                case "phone": save = save with { Phone = value }; break;
                case "job_title": save = save with { JobTitle = value }; break;
                case "description": save = save with { Description = value }; break;
                default: custom[target.Key] = await CustomValueAsync(target, value, cancellationToken); break;
            }
        }

        await contacts.UpdateAsync(id, save with { CustomFields = custom }, cancellationToken);
    }

    private async Task ApplyToAccountAsync(Guid id, List<(RecordTargetDto Target, ApplyFieldToRecordItem Item)> list, CancellationToken cancellationToken)
    {
        var a = await accounts.GetAsync(id, cancellationToken);
        var save = new SaveAccountRequest(a.Name, a.Website, a.Phone, a.Industry, a.Description, a.ParentAccountId, a.OwnerId, a.CustomFields);
        var custom = a.CustomFields.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (target, item) in list)
        {
            var value = Stored(target, item);
            switch (target.IsCustom ? "custom" : target.Key)
            {
                case "name": save = save with { Name = value }; break;
                case "website": save = save with { Website = value }; break;
                case "phone": save = save with { Phone = value }; break;
                case "industry": save = save with { Industry = value }; break;
                case "description": save = save with { Description = value }; break;
                default: custom[target.Key] = await CustomValueAsync(target, value, cancellationToken); break;
            }
        }

        await accounts.UpdateAsync(id, save with { CustomFields = custom }, cancellationToken);
    }

    private async Task ApplyToDealAsync(Guid id, List<(RecordTargetDto Target, ApplyFieldToRecordItem Item)> list, CancellationToken cancellationToken)
    {
        var d = await deals.GetAsync(id, cancellationToken);

        // Contacts stay as they are: a null list in the request means "unchanged".
        var save = new SaveDealRequest(d.Name, d.PipelineId, d.StageId, d.Amount, d.Currency, d.ProbabilityOverridden ? d.Probability : null, d.ExpectedCloseDate,
            d.AccountId, null, d.Description, d.OwnerId, d.CustomFields);
        var custom = d.CustomFields.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var (target, item) in list)
        {
            var value = Stored(target, item);
            switch (target.IsCustom ? "custom" : target.Key)
            {
                case "name": save = save with { Name = value }; break;
                case "amount": save = save with { Amount = decimal.Parse(value, CultureInfo.InvariantCulture) }; break;
                case "expected_close_date": save = save with { ExpectedCloseDate = DateTime.SpecifyKind(DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) }; break;
                case "description": save = save with { Description = value }; break;
                default: custom[target.Key] = await CustomValueAsync(target, value, cancellationToken); break;
            }
        }

        await deals.UpdateAsync(id, save with { CustomFields = custom }, cancellationToken);
    }

    /// <summary>The value in the form the record field stores, or a validation error naming the field when it does not fit.</summary>
    private static string Stored(RecordTargetDto target, ApplyFieldToRecordItem item)
    {
        var value = CustomDocumentExtractor.Normalize(target.ValueType, item.Value.Trim());
        if (value is null)
        {
            throw new ValidationException("applyFields", $"{item.FieldName}: \"{item.Value}\" is not a valid {target.Label.ToLowerInvariant()} ({target.ValueType.ToLowerInvariant()}).");
        }

        return value;
    }

    /// <summary>A custom field's value as JSON in the type the field holds: a number for numeric fields, a yyyy-MM-dd string for dates, text otherwise.</summary>
    private async Task<JsonElement> CustomValueAsync(RecordTargetDto target, string value, CancellationToken cancellationToken)
    {
        var type = await db.CustomFieldDefinitions.AsNoTracking()
            .Where(d => d.EntityType == target.Entity && d.Key == target.Key)
            .Select(d => d.FieldType).FirstOrDefaultAsync(cancellationToken);
        return type is not null && Enum.Parse<FieldType>(type) is FieldType.Number or FieldType.Decimal or FieldType.Currency or FieldType.Percent
            ? JsonSerializer.SerializeToElement(decimal.Parse(value, CultureInfo.InvariantCulture))
            : JsonSerializer.SerializeToElement(value);
    }

    private static string Singular(string entity) => entity switch { Contacts => "contact", Accounts => "account", _ => "deal" };
}
