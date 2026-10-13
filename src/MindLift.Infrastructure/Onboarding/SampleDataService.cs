using System.Text.Json;
using Bogus;
using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Application.Common.Exceptions;
using MindLift.Application.Common.Fields;
using MindLift.Domain.Entities;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Settings;
using MindLift.Shared.Contracts;
using MindLift.Shared.Filtering;

namespace MindLift.Infrastructure.Onboarding;

/// <summary>
/// Loads realistic demo records (plan 6.7). The generator uses a fixed seed, so the same workspace always gets the same data.
/// Every record is marked IsSample, so the whole set can be removed in one step.
/// Not yet generated (their tables come later): stage history (M8) and monthly sales targets (M13).
/// </summary>
public sealed class SampleDataService(AppDbContext db, ITenantContext tenant, IClock clock, SettingsVersion version)
{
    public const int Seed = 20261009;

    private static readonly string[] LeadStatuses =
    [
        "converted", "converted", "converted", "converted", "converted", "converted",
        "new", "new", "new", "new", "new", "new", "new", "new",
        "working", "working", "working", "working", "working", "working", "working", "working",
        "qualified", "qualified", "qualified", "qualified",
        "disqualified", "disqualified", "disqualified", "disqualified",
    ];

    public async Task<SampleDataDto> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.TenantSettings.FirstAsync(cancellationToken);
        if (settings.SampleDataLoaded)
        {
            throw new ConflictException("sample_loaded", "Sample data is already loaded.");
        }

        var pipeline = await db.Pipelines.Where(p => p.IsDefault).FirstOrDefaultAsync(cancellationToken)
            ?? await db.Pipelines.OrderBy(p => p.SortOrder).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("no_pipeline", "Create a pipeline before loading sample data.");
        var stages = await db.PipelineStages.Where(s => s.PipelineId == pipeline.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var openStages = stages.Where(s => s.StageType == "Open").ToList();
        var wonStage = stages.Single(s => s.StageType == "Won");
        var lostStage = stages.Single(s => s.StageType == "Lost");

        var ownerId = await db.Users.Where(u => u.IsTenantOwner).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken)
            ?? tenant.UserId ?? throw new ForbiddenException("No owner is available for the sample records.");

        var now = clock.UtcNow;
        var faker = new Faker("en") { Random = new Randomizer(Seed) };
        var currency = settings.BaseCurrency;
        var customs = await db.CustomFieldDefinitions.AsNoTracking().Where(d => d.IsActive).ToListAsync(cancellationToken);

        var accounts = Enumerable.Range(0, 8).Select(_ =>
        {
            var name = faker.Company.CompanyName();
            return new Account
            {
                Name = name,
                Website = "https://www." + faker.Internet.DomainWord() + ".example",
                OwnerId = ownerId,
                IsSample = true,
                CustomFields = Custom(faker, customs, "accounts"),
            };
        }).ToList();

        var contacts = Enumerable.Range(0, 20).Select(i =>
        {
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            return new Contact
            {
                FirstName = first,
                LastName = last,
                Email = faker.Internet.Email(first, last, "example.test"),
                Phone = "+9715" + faker.Random.Number(10_000_000, 99_999_999),
                AccountId = i < 18 ? faker.PickRandom(accounts).Id : null,
                Type = faker.PickRandom("client", "prospect", "partner", "supplier"),
                OwnerId = ownerId,
                IsSample = true,
                CustomFields = Custom(faker, customs, "contacts"),
            };
        }).ToList();

        var leads = LeadStatuses.Select(status =>
        {
            var first = faker.Name.FirstName();
            var last = faker.Name.LastName();
            return new Lead
            {
                FirstName = first,
                LastName = last,
                Email = faker.Internet.Email(first, last, "example.test"),
                Phone = "+9715" + faker.Random.Number(10_000_000, 99_999_999),
                CompanyName = faker.Company.CompanyName(),
                Status = status,
                Source = faker.PickRandom("website", "referral", "social_media", "walk_in", "campaign", "whatsapp", "phone"),
                Rating = faker.PickRandom("hot", "warm", "cold"),
                OwnerId = ownerId,
                IsSample = true,
                CustomFields = Custom(faker, customs, "leads"),
            };
        }).ToList();

        var deals = new List<Deal>();
        for (var i = 0; i < 25; i++)
        {
            var isWon = i < 8;
            var isLost = !isWon && i < 13;
            var closedAt = now.AddDays(-faker.Random.Int(1, 180));
            var stage = isWon ? wonStage : isLost ? lostStage : faker.PickRandom(openStages);
            deals.Add(new Deal
            {
                Name = $"{faker.PickRandom(accounts).Name} â€“ {faker.Commerce.ProductAdjective()} {faker.Commerce.Product()}",
                PipelineId = pipeline.Id,
                StageId = stage.Id,
                Status = isWon ? "Won" : isLost ? "Lost" : "Open",
                Amount = faker.Random.Decimal(20_000, 3_000_000),
                Currency = currency,
                Probability = stage.Probability,
                StageChangedAt = isWon || isLost ? closedAt : now.AddDays(-faker.Random.Int(0, 30)),
                ExpectedCloseDate = isWon || isLost ? null : UtcDay(now).AddDays(faker.Random.Int(10, 120)),
                ClosedAt = isWon || isLost ? closedAt : null,
                LostReason = isLost ? faker.PickRandom("price", "competitor", "no_budget", "no_response", "timing") : null,
                AccountId = faker.PickRandom(accounts).Id,
                OwnerId = ownerId,
                IsSample = true,
                CustomFields = Custom(faker, customs, "deals"),
            });
        }

        var activities = Enumerable.Range(0, 60).Select(i =>
        {
            var type = faker.PickRandom("Task", "Call", "Meeting", "Note");
            var dueAt = i < 20 ? now.AddDays(-faker.Random.Int(1, 10))
                : i < 40 ? UtcDay(now).AddHours(faker.Random.Int(8, 18))
                : now.AddDays(faker.Random.Int(1, 20));
            var target = i % 3;
            return new Activity
            {
                Type = type,
                Subject = $"{type}: {faker.Lorem.Sentence(4)}",
                Status = i % 7 == 0 ? "Completed" : "Open",
                DueAt = type == "Note" ? null : dueAt,
                CallOutcome = type == "Call" ? faker.PickRandom("connected", "no_answer", "busy", "left_voicemail") : null,
                LeadId = target == 0 ? faker.PickRandom(leads).Id : null,
                ContactId = target == 1 ? faker.PickRandom(contacts).Id : null,
                DealId = target == 2 ? faker.PickRandom(deals).Id : null,
                OwnerId = ownerId,
                IsSample = true,
            };
        }).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Accounts.AddRange(accounts);
        db.Contacts.AddRange(contacts);
        db.Leads.AddRange(leads);
        db.Deals.AddRange(deals);
        db.Activities.AddRange(activities);
        settings.SampleDataLoaded = true;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return new SampleDataDto(accounts.Count, contacts.Count, leads.Count, deals.Count, activities.Count);
    }

    /// <summary>Removes every sample record of the tenant with real deletes. Leaves the audit trail in place.</summary>
    public async Task<SampleDataDto> RemoveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No workspace is selected.");

        var activities = await db.Activities.IgnoreQueryFilters().Where(a => a.TenantId == tenantId && a.IsSample).ExecuteDeleteAsync(cancellationToken);
        var deals = await db.Deals.IgnoreQueryFilters().Where(d => d.TenantId == tenantId && d.IsSample).ExecuteDeleteAsync(cancellationToken);
        var leads = await db.Leads.IgnoreQueryFilters().Where(l => l.TenantId == tenantId && l.IsSample).ExecuteDeleteAsync(cancellationToken);
        var contacts = await db.Contacts.IgnoreQueryFilters().Where(c => c.TenantId == tenantId && c.IsSample).ExecuteDeleteAsync(cancellationToken);
        var accounts = await db.Accounts.IgnoreQueryFilters().Where(a => a.TenantId == tenantId && a.IsSample).ExecuteDeleteAsync(cancellationToken);

        var settings = await db.TenantSettings.FirstAsync(cancellationToken);
        settings.SampleDataLoaded = false;
        await db.SaveChangesAsync(cancellationToken);
        await version.BumpAsync(cancellationToken);

        return new SampleDataDto(accounts, contacts, leads, deals, activities);
    }

    /// <summary>Midnight of the UTC day, keeping the UTC kind that PostgreSQL requires.</summary>
    private static DateTime UtcDay(DateTime utc) => DateTime.SpecifyKind(utc.Date, DateTimeKind.Utc);

    /// <summary>A value for every active custom field of the entity, shaped by its type.</summary>
    private static JsonDocument? Custom(Faker faker, List<CustomFieldDefinition> definitions, string entityKey)
    {
        var values = new Dictionary<string, object?>();
        foreach (var definition in definitions.Where(d => d.EntityType == entityKey))
        {
            values[definition.Key] = definition.FieldType switch
            {
                "Number" => faker.Random.Int(1, 10),
                "Decimal" or "Currency" or "Percent" => faker.Random.Decimal(1, 500_000),
                "Date" => faker.Date.Soon(60).ToString("yyyy-MM-dd"),
                "DateTime" => faker.Date.Soon(60).ToUniversalTime().ToString("O"),
                "Boolean" => faker.Random.Bool(),
                "Picklist" => PickOption(faker, definition),
                "MultiPicklist" => new[] { PickOption(faker, definition) },
                _ => faker.Lorem.Word(),
            };
        }

        return values.Count == 0 ? null : JsonDocument.Parse(JsonSerializer.Serialize(values));
    }

    private static string? PickOption(Faker faker, CustomFieldDefinition definition)
    {
        if (definition.Options is null)
        {
            return null;
        }

        var options = JsonSerializer.Deserialize<List<PicklistOptionDto>>(definition.Options.RootElement.GetRawText()) ?? [];
        return options.Count == 0 ? null : faker.PickRandom(options).Value;
    }
}

