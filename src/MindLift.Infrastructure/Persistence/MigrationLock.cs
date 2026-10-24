using System.Data;
using Microsoft.EntityFrameworkCore;

namespace MindLift.Infrastructure.Persistence;

/// <summary>Runs start-up database work under a PostgreSQL advisory lock, so only one server at a time migrates the schema.</summary>
public static class MigrationLock
{
    /// <summary>Any fixed number; every server must use the same one.</summary>
    private const long Key = 7_240_118_001;

    public static async Task RunAsync(AppDbContext db, Func<Task> work)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using (var take = connection.CreateCommand())
            {
                take.CommandText = $"SELECT pg_advisory_lock({Key})";
                take.CommandTimeout = 600;
                await take.ExecuteNonQueryAsync();
            }

            try
            {
                await work();
            }
            finally
            {
                await using var release = connection.CreateCommand();
                release.CommandText = $"SELECT pg_advisory_unlock({Key})";
                await release.ExecuteNonQueryAsync();
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
