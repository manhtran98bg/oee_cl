using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.MesSync;
using Rostek.Gateway.Infrastructure.Oee;
using Rostek.Gateway.Infrastructure.Persistence;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Migration_creates_config_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);

        await db.Database.EnsureCreatedAsync();

        db.MachineGroups.Add(new MachineGroup { Code = "MOLDING-A", Name = "Molding A" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.MachineGroups.CountAsync());
    }

    [Fact]
    public async Task Migration_creates_python_local_oee_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        var tableNames = await ReadTableNamesAsync(connection);

        Assert.Contains("production_context", tableNames);
        Assert.Contains("production_period", tableNames);
        Assert.Contains("plc_raw_interval", tableNames);
        Assert.Contains("production_metric", tableNames);
        Assert.Contains("product_metric", tableNames);
        Assert.Contains("downtime_event", tableNames);
        Assert.Contains("sync_outbox", tableNames);
        Assert.Contains("Machines", tableNames);
        Assert.Contains("TemplateSignals", tableNames);
    }

    [Fact]
    public async Task Ef_migrations_apply_python_local_oee_schema_without_removing_config_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);

        await db.Database.MigrateAsync();

        var tableNames = await ReadTableNamesAsync(connection);
        Assert.Contains("production_context", tableNames);
        Assert.Contains("production_period", tableNames);
        Assert.Contains("plc_raw_interval", tableNames);
        Assert.Contains("production_metric", tableNames);
        Assert.Contains("product_metric", tableNames);
        Assert.Contains("downtime_event", tableNames);
        Assert.Contains("sync_outbox", tableNames);
        Assert.Contains("Machines", tableNames);
        Assert.Contains("TemplateSignals", tableNames);
    }

    [Fact]
    public async Task Plc_raw_interval_enforces_unique_machine_read_at()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        db.PlcRawIntervals.Add(Raw("M16-01", 10, 100));
        db.PlcRawIntervals.Add(Raw("M16-01", 10, 101));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Oee_local_repository_inserts_missing_and_reads_previous_by_plc_period()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreOeeRawIntervalRepository(db);

        var insertedFirst = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 10, 100)], CancellationToken.None);
        var duplicate = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 10, 101)], CancellationToken.None);
        var insertedSecond = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 15, 108)], CancellationToken.None);
        var previous = await repository.GetPreviousRawInPeriodAsync("M16-01", 1, 0, 15, CancellationToken.None);

        Assert.Single(insertedFirst);
        Assert.Empty(duplicate);
        Assert.Single(insertedSecond);
        Assert.NotNull(previous);
        Assert.Equal(100, previous.ShotOkTotal);
    }

    [Fact]
    public async Task Local_pipeline_can_create_context_period_metric_product_metric_and_outbox()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreOeeRawIntervalRepository(db);
        var context = await repository.EnsureTestProductionContextAsync("M16-01", 10, CancellationToken.None);
        await repository.EnsureTestProductionPeriodAsync(context, 10, CancellationToken.None);
        await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 10, 100, runTimeTotalSec: 10)], CancellationToken.None);
        var current = Raw("M16-01", 15, 108, runTimeTotalSec: 15);
        await repository.InsertMissingRawIntervalsAsync([current], CancellationToken.None);
        var store = new ProductionContextStore();
        store.Replace(await repository.ListProductionContextsAsync(CancellationToken.None));
        var builder = new OeeMetricBuilder(repository, store, Microsoft.Extensions.Logging.Abstractions.NullLogger<OeeMetricBuilder>.Instance);

        var result = await builder.BuildMetricsAsync([current], CancellationToken.None);
        foreach (var metric in result.ProductionMetrics)
        {
            await repository.EnqueueOutboxAsync(new MesSyncOutboxMessage
            {
                Topic = $"metric.{metric.MetricType}",
                SourceTable = "production_metric",
                SourceId = $"{metric.MetricType}|{metric.PeriodId}|{metric.ProductId}|{metric.RunState}|{metric.StartAt}",
                PayloadJson = "{}",
                CreatedAt = metric.CreatedAt,
                UpdatedAt = metric.UpdatedAt
            }, CancellationToken.None);
        }

        Assert.True(await db.ProductionMetrics.CountAsync() > 0);
        Assert.Single(await db.ProductMetrics.ToListAsync());
        Assert.True(await db.MesSyncOutboxMessages.CountAsync() > 0);
    }

    [Fact]
    public async Task Mes_sync_outbox_repository_takes_pending_without_sqlite_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreMesSyncOutboxRepository(db);
        db.MesSyncOutboxMessages.AddRange(
            OutboxMessage("failed", 11),
            OutboxMessage("pending", 10));
        await db.SaveChangesAsync();

        var messages = await repository.TakePendingAsync(10, CancellationToken.None);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, message => Assert.Equal("sending", message.Status));
    }

    [Fact]
    public async Task Unique_template_signal_code_is_enforced_per_template()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        var template = new MachineTemplate { Code = "SUMITOMO-OPCUA", Name = "Sumitomo", Protocol = GatewayProtocol.OpcUa };
        template.Signals.Add(new TemplateSignal { SignalCode = "machine_state", DisplayName = "State", SourceAddress = "ns=2;s=State", DataType = SignalDataType.Int16 });
        template.Signals.Add(new TemplateSignal { SignalCode = "machine_state", DisplayName = "State 2", SourceAddress = "ns=2;s=State2", DataType = SignalDataType.Int16 });
        db.MachineTemplates.Add(template);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static GatewayDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        return new GatewayDbContext(options);
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select name from sqlite_master where type = 'table'";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static PlcRawInterval Raw(string machine, long readAt, long shotOkTotal, long runTimeTotalSec = 0) =>
        new()
        {
            Machine = machine,
            ReadAt = readAt,
            PlcPeriodIndex = 1,
            RunState = "run",
            ShotOkTotal = shotOkTotal,
            CycleTimeMs = 1000,
            RunTimeTotalSec = runTimeTotalSec,
            PeriodActive = 1
        };

    private static MesSyncOutboxMessage OutboxMessage(string status, long updatedAt) =>
        new()
        {
            Topic = "metric.second",
            SourceTable = "production_metric",
            SourceId = Guid.NewGuid().ToString("N"),
            PayloadJson = "{}",
            Status = status,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
}
