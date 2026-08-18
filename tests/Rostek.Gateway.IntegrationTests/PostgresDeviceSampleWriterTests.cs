using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Rostek.Gateway.Application.History;
using Rostek.Gateway.Infrastructure.History;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class PostgresDeviceSampleWriterTests
{
    private const string ConnectionStringEnvironmentVariable = "ROSTEK_GATEWAY_TEST_POSTGRES";

    [Fact]
    public async Task Writer_creates_schema_and_writes_samples_in_id_order()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var gatewayId = $"TEST-{Guid.NewGuid():N}";
        var writer = new PostgresDeviceSampleWriter(
            Options.Create(new HistoryOptions
            {
                ConnectionString = connectionString,
                BatchSize = 2,
                CommandTimeoutSeconds = 10
            }),
            NullLogger<PostgresDeviceSampleWriter>.Instance);

        try
        {
            await writer.EnsureSchemaAsync(CancellationToken.None);
            await writer.WriteAsync(
                [
                    new DeviceSample(gatewayId, "M16-01", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 10, 1, 1400, 5000, 0, 0),
                    new DeviceSample(gatewayId, "M16-02", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 2, 20, 2, 1500, 4000, 1000, 0)
                ],
                CancellationToken.None);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                select id, machine_code
                from device_samples
                where gateway_id = @gateway_id
                order by id
                """,
                connection);
            command.Parameters.AddWithValue("gateway_id", gatewayId);

            var rows = new List<(long Id, string MachineCode)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1)));
            }

            Assert.Collection(
                rows,
                row => Assert.Equal("M16-01", row.MachineCode),
                row => Assert.Equal("M16-02", row.MachineCode));
            Assert.True(rows[0].Id < rows[1].Id);
        }
        finally
        {
            await DeleteTestRowsAsync(connectionString, gatewayId);
        }
    }

    [Fact]
    public async Task Writer_deletes_only_samples_older_than_cutoff()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var gatewayId = $"TEST-{Guid.NewGuid():N}";
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-90);
        var writer = new PostgresDeviceSampleWriter(
            Options.Create(new HistoryOptions
            {
                ConnectionString = connectionString,
                BatchSize = 10,
                CommandTimeoutSeconds = 10
            }),
            NullLogger<PostgresDeviceSampleWriter>.Instance);

        try
        {
            await writer.EnsureSchemaAsync(CancellationToken.None);
            await writer.WriteAsync(
                [
                    new DeviceSample(gatewayId, "OLD", cutoffUtc.AddSeconds(-1), DateTimeOffset.UtcNow, 1, null, null, null, null, null, null),
                    new DeviceSample(gatewayId, "EQUAL", cutoffUtc, DateTimeOffset.UtcNow, 1, null, null, null, null, null, null),
                    new DeviceSample(gatewayId, "NEW", cutoffUtc.AddSeconds(1), DateTimeOffset.UtcNow, 1, null, null, null, null, null, null)
                ],
                CancellationToken.None);

            var deleted = await writer.DeleteOlderThanAsync(cutoffUtc, CancellationToken.None);
            var remaining = await ReadMachineCodesAsync(connectionString, gatewayId);

            Assert.Equal(1, deleted);
            Assert.Equal(new[] { "EQUAL", "NEW" }, remaining);
        }
        finally
        {
            await DeleteTestRowsAsync(connectionString, gatewayId);
        }
    }

    private static async Task DeleteTestRowsAsync(string connectionString, string gatewayId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("delete from device_samples where gateway_id = @gateway_id", connection);
        command.Parameters.AddWithValue("gateway_id", gatewayId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadMachineCodesAsync(string connectionString, string gatewayId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select machine_code
            from device_samples
            where gateway_id = @gateway_id
            order by machine_code
            """,
            connection);
        command.Parameters.AddWithValue("gateway_id", gatewayId);

        var machineCodes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            machineCodes.Add(reader.GetString(0));
        }

        return machineCodes;
    }
}
