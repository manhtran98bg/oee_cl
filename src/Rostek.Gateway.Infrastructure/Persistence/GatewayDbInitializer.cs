using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rostek.Gateway.Infrastructure.Persistence;

public sealed class GatewayDbInitializer(GatewayDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await SqlitePragmaInitializer.ApplyAsync((SqliteConnection)dbContext.Database.GetDbConnection(), cancellationToken);
    }
}

public sealed class OeeDbInitializer(OeeDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await SqlitePragmaInitializer.ApplyAsync((SqliteConnection)dbContext.Database.GetDbConnection(), cancellationToken);
    }
}

internal static class SqlitePragmaInitializer
{
    private static readonly string[] Pragmas =
    [
        "PRAGMA journal_mode = WAL;",
        "PRAGMA foreign_keys = ON;",
        "PRAGMA busy_timeout = 5000;",
        "PRAGMA synchronous = NORMAL;"
    ];

    public static async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        foreach (var pragma in Pragmas)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
