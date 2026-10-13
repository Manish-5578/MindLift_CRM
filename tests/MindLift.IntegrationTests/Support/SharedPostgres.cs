using Npgsql;
using Testcontainers.PostgreSql;

namespace MindLift.IntegrationTests.Support;

/// <summary>
/// One PostgreSQL 16 container shared by every integration test in the run (requires Docker).
/// Testcontainers removes it when the test process exits.
/// </summary>
public static class SharedPostgres
{
    private static readonly object Gate = new();
    private static PostgreSqlContainer? _container;

    /// <summary>Connection string to the container's default database (superuser, so pg_trgm can be created).</summary>
    public static string ConnectionString
    {
        get
        {
            lock (Gate)
            {
                _container ??= StartContainer();
                return _container.GetConnectionString();
            }
        }
    }

    /// <summary>
    /// Creates an empty database with a unique name and returns a connection string to it.
    /// Each API test host gets its own database, so hosts that migrate at startup never race each other.
    /// </summary>
    public static async Task<string> NewDatabaseConnectionStringAsync()
    {
        var name = "test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        // A small pool per host keeps the total low. Tests send one request at a time per host.
        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name, MaxPoolSize = 12 }.ConnectionString;
    }

    private static PostgreSqlContainer StartContainer()
    {
        // Every API test host keeps its own connection pool and Hangfire workers, and hosts run in parallel. A default
        // Postgres allows 100 connections, which a full run reached (peak 102), giving random "too many clients" 500s.
        var container = new PostgreSqlBuilder().WithImage("postgres:16").WithCommand("-c", "max_connections=400").Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    }
}
