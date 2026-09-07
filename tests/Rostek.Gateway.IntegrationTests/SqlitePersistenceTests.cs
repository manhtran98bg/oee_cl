using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task Migration_creates_production_context_and_mes_sync_outbox_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        db.ProductionContexts.Add(new ProductionContext
        {
            MachineCode = "M16-01",
            CommandCode = "CMD-001",
            Status = ProductionContextStatus.Started,
            ProductionOrderCode = "MO-001",
            SessionId = "SESSION-001"
        });
        db.MesSyncOutboxMessages.Add(new MesSyncOutboxMessage
        {
            Topic = "metric.second",
            Endpoint = "/secondly-production/sync",
            PayloadJson = "{\"data\":[]}",
            Status = MesSyncOutboxStatus.Pending
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.ProductionContexts.CountAsync());
        Assert.Equal(1, await db.MesSyncOutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Migration_creates_plc_raw_intervals_with_unique_machine_interval()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var readAt = DateTimeOffset.FromUnixTimeMilliseconds(10_000);

        db.PlcRawIntervals.Add(new PlcRawInterval
        {
            MachineCode = "M16-01",
            ProductionOrderCode = "MO-001",
            SessionId = "SESSION-001",
            ReadAtUnixTimeSeconds = readAt.ToUnixTimeSeconds(),
            ShotOkTotal = 100
        });
        db.PlcRawIntervals.Add(new PlcRawInterval
        {
            MachineCode = "M16-01",
            ProductionOrderCode = "MO-001",
            SessionId = "SESSION-001",
            ReadAtUnixTimeSeconds = readAt.ToUnixTimeSeconds(),
            ShotOkTotal = 101
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Oee_raw_interval_repository_inserts_missing_and_reads_previous()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreOeeRawIntervalRepository(db);
        var first = DateTimeOffset.FromUnixTimeMilliseconds(10_000);
        var second = DateTimeOffset.FromUnixTimeMilliseconds(15_000);

        var insertedFirst = await repository.InsertMissingAsync([Raw("M16-01", first, 100)], CancellationToken.None);
        var duplicate = await repository.InsertMissingAsync([Raw("M16-01", first, 101)], CancellationToken.None);
        var insertedSecond = await repository.InsertMissingAsync([Raw("M16-01", second, 108)], CancellationToken.None);
        var previous = await repository.GetPreviousInContextAsync("M16-01", "MO-001", "SESSION-001", 0, second.ToUnixTimeSeconds(), CancellationToken.None);

        Assert.Single(insertedFirst);
        Assert.Empty(duplicate);
        Assert.Single(insertedSecond);
        Assert.NotNull(previous);
        Assert.Equal(100, previous.ShotOkTotal);
    }

    [Fact]
    public async Task Oee_raw_interval_repository_reads_previous_only_in_same_production_session()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreOeeRawIntervalRepository(db);
        var first = DateTimeOffset.FromUnixTimeSeconds(10);
        var second = DateTimeOffset.FromUnixTimeSeconds(15);

        await repository.InsertMissingAsync(
            [
                Raw("M16-01", first, 100, sessionId: "OLD-SESSION"),
                Raw("M16-01", first, 200, sessionId: "SESSION-001")
            ],
            CancellationToken.None);

        var previous = await repository.GetPreviousInContextAsync("M16-01", "MO-001", "SESSION-001", 0, second.ToUnixTimeSeconds(), CancellationToken.None);

        Assert.NotNull(previous);
        Assert.Equal("SESSION-001", previous.SessionId);
        Assert.Equal(200, previous.ShotOkTotal);
    }

    [Fact]
    public async Task Mes_sync_outbox_repository_takes_pending_without_sqlite_datetimeoffset_translation_error()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreMesSyncOutboxRepository(db);
        var now = DateTimeOffset.UtcNow;
        db.MesSyncOutboxMessages.AddRange(
            OutboxMessage(MesSyncOutboxStatus.Failed, now.AddMinutes(1)),
            OutboxMessage(MesSyncOutboxStatus.Pending, now.AddSeconds(-1)));
        await db.SaveChangesAsync();

        var messages = await repository.TakePendingAsync(now.ToUnixTimeSeconds(), 10, CancellationToken.None);

        var message = Assert.Single(messages);
        Assert.Equal(MesSyncOutboxStatus.InProgress, message.Status);
        Assert.True(message.NextAttemptUnixTimeSeconds <= now.ToUnixTimeSeconds());
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

    private static PlcRawInterval Raw(string machineCode, DateTimeOffset readAtUtc, long shotOkTotal, string sessionId = "SESSION-001") =>
        new()
        {
            MachineCode = machineCode,
            ProductionOrderCode = "MO-001",
            SessionId = sessionId,
            ReadAtUnixTimeSeconds = readAtUtc.ToUnixTimeSeconds(),
            ShotOkTotal = shotOkTotal,
            CreatedUnixTimeSeconds = readAtUtc.ToUnixTimeSeconds()
        };

    private static MesSyncOutboxMessage OutboxMessage(MesSyncOutboxStatus status, DateTimeOffset nextAttemptAtUtc)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new()
        {
            Topic = "metric.second",
            Endpoint = "/secondly-production/sync",
            PayloadJson = "{\"data\":[]}",
            Status = status,
            NextAttemptUnixTimeSeconds = nextAttemptAtUtc.ToUnixTimeSeconds(),
            CreatedUnixTimeSeconds = now,
            UpdatedUnixTimeSeconds = now
        };
    }
}
