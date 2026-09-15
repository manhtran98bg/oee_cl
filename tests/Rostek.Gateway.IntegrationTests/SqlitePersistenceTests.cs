using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Oee;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;
using Xunit;
using GatewayConfigurationBuilder = Rostek.Gateway.Application.Configurations.ConfigurationBuilder;

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
        Assert.DoesNotContain("plc_raw_interval", await ReadTableNamesAsync(connection));
    }

    [Fact]
    public async Task Oee_migration_creates_python_local_oee_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateOeeContext(connection);
        await db.Database.MigrateAsync();

        var tableNames = await ReadTableNamesAsync(connection);

        Assert.Contains("production_context", tableNames);
        Assert.Contains("production_period", tableNames);
        Assert.Contains("plc_raw_interval", tableNames);
        Assert.Contains("machine_state_event", tableNames);
        Assert.Contains("sync_outbox", tableNames);
        Assert.DoesNotContain("production_metric", tableNames);
        Assert.DoesNotContain("product_metric", tableNames);
        Assert.DoesNotContain("downtime_event", tableNames);
        Assert.DoesNotContain("Machines", tableNames);
        Assert.DoesNotContain("TemplateSignals", tableNames);
    }

    [Fact]
    public async Task Config_migrations_do_not_create_oee_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);

        await db.Database.MigrateAsync();

        var tableNames = await ReadTableNamesAsync(connection);
        Assert.DoesNotContain("production_context", tableNames);
        Assert.DoesNotContain("production_period", tableNames);
        Assert.DoesNotContain("plc_raw_interval", tableNames);
        Assert.DoesNotContain("production_metric", tableNames);
        Assert.DoesNotContain("product_metric", tableNames);
        Assert.DoesNotContain("downtime_event", tableNames);
        Assert.DoesNotContain("sync_outbox", tableNames);
        Assert.DoesNotContain("machine_state_event", tableNames);
        Assert.Contains("Machines", tableNames);
        Assert.Contains("TemplateSignals", tableNames);
        Assert.Contains("Model", await ReadColumnNamesAsync(connection, "Machines"));
        Assert.Contains("Serial", await ReadColumnNamesAsync(connection, "Machines"));
        Assert.Contains("Manufacturer", await ReadColumnNamesAsync(connection, "Machines"));
        Assert.Contains("Location", await ReadColumnNamesAsync(connection, "Machines"));
    }

    [Fact]
    public async Task Ef_migrations_upgrade_database_that_already_recorded_removed_oee_migrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await ExecuteAsync(connection, """
            create table __EFMigrationsHistory (
                MigrationId TEXT not null primary key,
                ProductVersion TEXT not null
            );

            insert into __EFMigrationsHistory (MigrationId, ProductVersion) values
                ('202607270001_InitialConfigSchema', '10.0.0'),
                ('202609070001_AddProductionContextAndMesSyncOutbox', '10.0.0'),
                ('202609070002_AddOeeRawIntervals', '10.0.0'),
                ('202609070003_UseUnixTimestampsForMesOee', '10.0.0'),
                ('202609070004_AddProductionSessionToOeeRawIntervals', '10.0.0');

            create table MachineGroups (
                Id TEXT not null primary key,
                Code TEXT not null,
                Name TEXT not null,
                Description TEXT null,
                DisplayOrder INTEGER not null,
                CreatedAtUtc TEXT not null,
                UpdatedAtUtc TEXT not null
            );

            create table ProductionContexts (
                Id TEXT not null primary key,
                MachineCode TEXT not null,
                CommandCode TEXT not null,
                Status TEXT not null
            );

            create table Machines (
                Id TEXT not null primary key,
                Code TEXT not null,
                Name TEXT not null,
                GroupId TEXT null,
                TemplateId TEXT not null,
                Enabled INTEGER not null,
                DisplayOrder INTEGER not null,
                Description TEXT null,
                CreatedAtUtc TEXT not null,
                UpdatedAtUtc TEXT not null
            );

            create table production_context (
                machine TEXT not null primary key
            );
            """);
        await using var db = CreateContext(connection);

        await db.Database.MigrateAsync();

        var tableNames = await ReadTableNamesAsync(connection);
        Assert.Contains("MachineGroups", tableNames);
        Assert.DoesNotContain("production_context", tableNames);
        Assert.DoesNotContain("ProductionContexts", tableNames);
        Assert.Contains(
            "202609070006_RemoveOeeTablesFromConfigDb",
            await ReadAppliedMigrationIdsAsync(connection));
        Assert.Contains(
            "202609140001_AddMachineMesMetadata",
            await ReadAppliedMigrationIdsAsync(connection));
        Assert.Contains("Model", await ReadColumnNamesAsync(connection, "Machines"));
    }

    [Fact]
    public async Task Plc_raw_interval_enforces_unique_machine_read_at()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateOeeContext(connection);
        await db.Database.EnsureCreatedAsync();

        db.PlcRawIntervals.Add(Raw("M16-01", 10, 100));
        db.PlcRawIntervals.Add(Raw("M16-01", 10, 101));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Oee_local_repository_inserts_missing_raw_intervals_and_context_period()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateOeeContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreOeeLocalRepository(db);

        var context = await repository.EnsureTestProductionContextAsync("M16-01", 10, CancellationToken.None);
        var period = await repository.EnsureProductionPeriodAsync(context, 10, CancellationToken.None);
        var insertedFirst = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 10, 100)], CancellationToken.None);
        var duplicate = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 10, 101)], CancellationToken.None);
        var insertedSecond = await repository.InsertMissingRawIntervalsAsync([Raw("M16-01", 15, 108)], CancellationToken.None);

        Assert.Equal("TEST_ORDER", context.OrderId);
        Assert.Equal(context.SessionId, period.PeriodId);
        Assert.Single(insertedFirst);
        Assert.Empty(duplicate);
        Assert.Single(insertedSecond);
        Assert.Equal(2, await db.PlcRawIntervals.CountAsync());
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

    [Fact]
    public async Task Machine_service_saves_and_loads_mes_metadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreConfigRepository(db);
        var service = new MachineService(
            repository,
            new GatewayConfigurationBuilder(repository),
            NullLogger<MachineService>.Instance);
        var template = new MachineTemplate { Code = "TPL-MODBUS", Name = "Template Modbus", Protocol = GatewayProtocol.ModbusTcp };
        await db.MachineTemplates.AddAsync(template);
        await db.SaveChangesAsync();

        var save = await service.SaveAsync(
            new MachineEditInput
            {
                Code = "2-10",
                Name = "Machine 2-10",
                Model = "J350ADS-890H",
                Serial = "SN-001",
                Manufacturer = "JSW",
                Location = "Line 2",
                TemplateId = template.Id
            },
            null,
            CancellationToken.None);
        var input = await service.GetInputAsync(save.Value, CancellationToken.None);

        Assert.True(save.Succeeded);
        Assert.NotNull(input);
        Assert.Equal("J350ADS-890H", input!.Model);
        Assert.Equal("SN-001", input.Serial);
        Assert.Equal("JSW", input.Manufacturer);
        Assert.Equal("Line 2", input.Location);
    }

    [Fact]
    public async Task Machine_service_deletes_machine_from_draft_configuration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var repository = new EfCoreConfigRepository(db);
        var service = new MachineService(
            repository,
            new GatewayConfigurationBuilder(repository),
            NullLogger<MachineService>.Instance);
        var template = new MachineTemplate { Code = "TPL-MODBUS", Name = "Template Modbus", Protocol = GatewayProtocol.ModbusTcp };
        var machine = new Machine { Code = "2-10", Name = "Machine 2-10", TemplateId = template.Id };
        await db.MachineTemplates.AddAsync(template);
        await db.Machines.AddAsync(machine);
        await db.SaveChangesAsync();

        var result = await service.DeleteAsync(machine.Id, "tester", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, await db.Machines.CountAsync());
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal("Delete machine", audit.Action);
        Assert.Equal(machine.Id.ToString(), audit.EntityId);
    }

    private static GatewayDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        return new GatewayDbContext(options);
    }

    private static OeeDbContext CreateOeeContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<OeeDbContext>().UseSqlite(connection).Options;
        return new OeeDbContext(options);
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

    private static async Task<HashSet<string>> ReadAppliedMigrationIdsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "select MigrationId from __EFMigrationsHistory";
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task<HashSet<string>> ReadColumnNamesAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"pragma table_info({tableName})";
        await using var reader = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
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

}
