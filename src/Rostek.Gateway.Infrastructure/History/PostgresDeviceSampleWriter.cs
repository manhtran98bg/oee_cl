using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Rostek.Gateway.Application.History;

namespace Rostek.Gateway.Infrastructure.History;

public sealed class PostgresDeviceSampleWriter(
    IOptions<HistoryOptions> options,
    ILogger<PostgresDeviceSampleWriter> logger) : IDeviceSampleWriter
{
    private const string SchemaSql = """
        create table if not exists device_samples (
            id bigserial primary key,
            gateway_id text not null,
            machine_code text not null,
            sampled_at_utc timestamptz not null,
            created_at_utc timestamptz not null default now(),

            machine_state smallint null,
            shot_ok_delta integer null,
            shot_ng_delta integer null,
            cycle_time_ms integer null,
            run_time_delta_ms integer null,
            stop_time_delta_ms integer null,
            error_time_delta_ms integer null
        );

        create index if not exists ix_device_samples_sampled_at
            on device_samples (sampled_at_utc);

        create index if not exists ix_device_samples_machine_sampled_at
            on device_samples (machine_code, sampled_at_utc);

        create index if not exists ix_device_samples_id
            on device_samples (id);
        """;

    private const string InsertSql = """
        insert into device_samples (
            gateway_id,
            machine_code,
            sampled_at_utc,
            created_at_utc,
            machine_state,
            shot_ok_delta,
            shot_ng_delta,
            cycle_time_ms,
            run_time_delta_ms,
            stop_time_delta_ms,
            error_time_delta_ms)
        values (
            @gateway_id,
            @machine_code,
            @sampled_at_utc,
            @created_at_utc,
            @machine_state,
            @shot_ok_delta,
            @shot_ng_delta,
            @cycle_time_ms,
            @run_time_delta_ms,
            @stop_time_delta_ms,
            @error_time_delta_ms);
        """;

    private const string DeleteOlderThanSql = """
        delete from device_samples
        where sampled_at_utc < @cutoff_utc;
        """;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var current = GetOptions();
        await using var connection = await OpenConnectionAsync(current, cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection)
        {
            CommandTimeout = current.CommandTimeoutSeconds
        };

        await command.ExecuteNonQueryAsync(cancellationToken);
        logger.LogInformation("PostgreSQL device history schema is ready");
    }

    public async Task WriteAsync(IReadOnlyCollection<DeviceSample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return;
        }

        var current = GetOptions();
        await using var connection = await OpenConnectionAsync(current, cancellationToken);
        foreach (var chunk in samples.Chunk(Math.Max(1, current.BatchSize)))
        {
            await using var batch = new NpgsqlBatch(connection)
            {
                Timeout = current.CommandTimeoutSeconds
            };

            foreach (var sample in chunk)
            {
                batch.BatchCommands.Add(CreateInsertCommand(sample));
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var current = GetOptions();
        await using var connection = await OpenConnectionAsync(current, cancellationToken);
        await using var command = new NpgsqlCommand(DeleteOlderThanSql, connection)
        {
            CommandTimeout = current.CommandTimeoutSeconds
        };

        AddTimestamp(command, "cutoff_utc", cutoffUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private HistoryOptions GetOptions()
    {
        var current = options.Value;
        if (string.IsNullOrWhiteSpace(current.ConnectionString))
        {
            throw new InvalidOperationException("History:ConnectionString is required when PostgreSQL history is enabled.");
        }

        return current;
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(HistoryOptions current, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(current.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static NpgsqlBatchCommand CreateInsertCommand(DeviceSample sample)
    {
        var command = new NpgsqlBatchCommand(InsertSql);
        AddText(command, "gateway_id", sample.GatewayId);
        AddText(command, "machine_code", sample.MachineCode);
        AddTimestamp(command, "sampled_at_utc", sample.SampledAtUtc);
        AddTimestamp(command, "created_at_utc", sample.CreatedAtUtc);
        AddNullableInt16(command, "machine_state", sample.MachineState);
        AddNullableInt32(command, "shot_ok_delta", sample.ShotOkDelta);
        AddNullableInt32(command, "shot_ng_delta", sample.ShotNgDelta);
        AddNullableInt32(command, "cycle_time_ms", sample.CycleTimeMs);
        AddNullableInt32(command, "run_time_delta_ms", sample.RunTimeDeltaMs);
        AddNullableInt32(command, "stop_time_delta_ms", sample.StopTimeDeltaMs);
        AddNullableInt32(command, "error_time_delta_ms", sample.ErrorTimeDeltaMs);
        return command;
    }

    private static void AddText(NpgsqlBatchCommand command, string name, string value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value;
    }

    private static void AddTimestamp(NpgsqlBatchCommand command, string name, DateTimeOffset value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.UtcDateTime;
    }

    private static void AddTimestamp(NpgsqlCommand command, string name, DateTimeOffset value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.UtcDateTime;
    }

    private static void AddNullableInt16(NpgsqlBatchCommand command, string name, short? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Smallint);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AddNullableInt32(NpgsqlBatchCommand command, string name, int? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Integer);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }
}
