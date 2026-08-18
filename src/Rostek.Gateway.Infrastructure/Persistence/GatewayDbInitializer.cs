using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rostek.Gateway.Infrastructure.Persistence;

public sealed class GatewayDbInitializer(GatewayDbContext dbContext)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
        await using var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        foreach (var pragma in new[] { "PRAGMA journal_mode = WAL;", "PRAGMA foreign_keys = ON;", "PRAGMA busy_timeout = 5000;", "PRAGMA synchronous = NORMAL;" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = pragma;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
