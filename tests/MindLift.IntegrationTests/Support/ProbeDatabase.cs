using Microsoft.EntityFrameworkCore;
using MindLift.Application.Abstractions;
using MindLift.Domain.Common;
using MindLift.Infrastructure.Persistence;
using MindLift.Infrastructure.Persistence.Interceptors;
using MindLift.Infrastructure.Tenancy;
using Npgsql;

namespace MindLift.IntegrationTests.Support;

/// <summary>A test-only tenant entity. Lives only in the test project; it never reaches the production model.</summary>
[Audited]
public sealed class ProbeEntity : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Adds the probe table to the production model so the tenant filter can be exercised end to end.</summary>
public sealed class ProbeDbContext(DbContextOptions options, ITenantContext tenant) : AppDbContext(options, tenant)
{
    public DbSet<ProbeEntity> Probes { get; set; } = null!;
}

/// <summary>A clock the test controls.</summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
}

/// <summary>
/// Creates a brand-new database with the probe schema for each test, so tests never share rows.
/// </summary>
public sealed class ProbeDatabase
{
    public TenantContext Tenant { get; } = new();
    public FixedClock Clock { get; } = new(new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc));

    private readonly string _connectionString;

    private ProbeDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static async Task<ProbeDatabase> CreateAsync()
    {
        var name = "probe_" + Guid.NewGuid().ToString("N");

        await using (var admin = new NpgsqlConnection(SharedPostgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(SharedPostgres.ConnectionString) { Database = name };
        var database = new ProbeDatabase(builder.ConnectionString);

        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        return database;
    }

    /// <summary>A new context (like a new request) sharing this database, tenant context and clock.</summary>
    public ProbeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditableEntityInterceptor(Tenant, Clock))
            .Options;

        return new ProbeDbContext(options, Tenant);
    }
}
