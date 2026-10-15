using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Scheduling;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Oee;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;
using Rostek.Gateway.Infrastructure.Scheduling;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class ProductionScheduleTests
{
    [Fact]
    public async Task Schedule_migration_upgrades_existing_oee_database_without_changing_production_data()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new OeeDbContext(new DbContextOptionsBuilder<OeeDbContext>().UseSqlite(connection).Options);
        // Legacy migrations have shortened timestamp IDs; EF's name resolver handles targeted upgrades by name.
        var previousMigrationName = db.GetService<IMigrationsIdGenerator>().GetName("202609150001_AddProductionMetrics");
        await db.GetService<IMigrator>().MigrateAsync(previousMigrationName);
        db.ProductionContexts.Add(new ProductionContext
        {
            SessionId = "EXISTING", Machine = "M-1", OrderId = "ORDER", ServerOrderId = "SERVER",
            ActivePeriodStartAt = 100, CurrentPlcPeriodIndex = 1, BaselineShotOkTotal = 123,
            ProductsJson = "[{\"product_id\":\"PRODUCT\",\"gain\":2,\"cycle_time\":10}]"
        });
        db.ProductionPeriods.Add(new ProductionPeriod
        {
            PeriodId = "EXISTING", Machine = "M-1", OrderId = "ORDER", PlcPeriodIndex = 1, StartAt = 100
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await db.Database.MigrateAsync();
        var existing = await db.ProductionContexts.SingleAsync();
        Assert.Equal("EXISTING", existing.SessionId);
        Assert.Equal(123, existing.BaselineShotOkTotal);
        Assert.Equal("SERVER", existing.ServerOrderId);
        Assert.Equal("EXISTING", (await db.ProductionPeriods.SingleAsync()).PeriodId);
        Assert.Empty(await db.ProductionSchedules.ToListAsync());
        Assert.Empty(await db.ProductionScheduleRuns.ToListAsync());
        Assert.Empty(await db.ProductionScheduleMachineRuns.ToListAsync());
    }

    [Fact]
    public async Task All_machines_includes_new_machines_and_is_not_limited_to_200()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMachineAsync("M-000");
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, []);
        for (var i = 1; i <= 205; i++) await fixture.AddMachineAsync($"M-{i:000}");
        await fixture.AddMachineAsync("DISABLED", enabled: false);
        await fixture.StartAsync("M-205");
        Assert.Equal(207, (await fixture.Service.ListMachinesAsync(default)).Count);

        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);
        var history = Assert.Single(await fixture.Service.HistoryAsync(null, default));
        Assert.Equal(206, history.Machines.Count);
        Assert.Equal("succeeded", Assert.Single(history.Machines, machine => machine.MachineCode == "M-205").Status);
        Assert.DoesNotContain(history.Machines, machine => machine.MachineCode == "DISABLED");
    }

    [Fact]
    public async Task Selected_machines_rotate_all_active_orders_and_ignore_test_and_unselected_machines()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddMachineAsync("M-1");
        var second = await fixture.AddMachineAsync("M-2");
        await fixture.AddMachineAsync("M-3");
        await fixture.StartAsync("M-1", "ORDER-1");
        await fixture.StartAsync("M-1", "ORDER-2");
        await fixture.StartAsync("M-1", OeeTestProductionContext.OrderId);
        await fixture.StartAsync("M-3");
        var original = await fixture.Oee.ProductionContexts.AsNoTracking().ToListAsync();
        await fixture.SaveOnceAsync(ProductionScheduleTarget.Selected, [first.Id, second.Id]);
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);

        var history = Assert.Single(await fixture.Service.HistoryAsync(null, default));
        Assert.Equal("partial", history.Run.Status);
        Assert.Equal(2, history.Machines.Count);
        Assert.Equal("skipped", Assert.Single(history.Machines, row => row.MachineCode == "M-2").Status);
        var current = await fixture.Oee.ProductionContexts.AsNoTracking().ToListAsync();
        Assert.Equal(4, current.Count);
        Assert.Equal(2, current.Count(context => context.SessionId.StartsWith("SCH-", StringComparison.Ordinal)));
        foreach (var kept in original.Where(context => context.OrderId == OeeTestProductionContext.OrderId || context.Machine == "M-3"))
            Assert.Contains(current, context => context.SessionId == kept.SessionId);
    }

    [Fact]
    public async Task Rotation_finalizes_metric_event_and_outbox_and_keeps_counter_boundary_and_metadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var machine = await fixture.AddMachineAsync("M-1");
        await fixture.StartAsync("M-1");
        var previous = await fixture.Oee.ProductionContexts.SingleAsync();
        previous.ServerOrderId = "SERVER-ORDER";
        previous.ExtraJson = "{\"batch\":\"A\"}";
        var products = previous.ProductsJson;
        var boundary = new PlcRawInterval
        {
            Machine = "M-1", ReadAt = fixture.Clock.Unix, ShotOkTotal = 20, ShotNgTotal = 2,
            RunTimeTotalSec = 30, StopTimeTotalSec = 5, ErrorTimeTotalSec = 0, RunState = "run"
        };
        previous.BaselineRawId = "baseline";
        previous.BaselineCapturedAt = fixture.Clock.Unix - 100;
        previous.BaselineShotOkTotal = 10;
        previous.BaselineShotNgTotal = 1;
        previous.BaselineRunTimeTotalSec = 10;
        fixture.Oee.PlcRawIntervals.Add(boundary);
        fixture.Oee.MachineStateEvents.Add(new MachineStateEvent
        {
            EventId = "EVENT", GatewayId = "GW", Machine = "M-1", OrderId = "ORDER", SessionId = previous.SessionId,
            IsOpen = true, State = "run", StartAt = previous.ActivePeriodStartAt
        });
        await fixture.Oee.SaveChangesAsync();
        fixture.ContextCache.Upsert(previous);
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, []);
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);

        var current = await fixture.Oee.ProductionContexts.SingleAsync();
        Assert.NotEqual(previous.SessionId, current.SessionId);
        Assert.Equal("SERVER-ORDER", current.ServerOrderId);
        Assert.Equal(products, current.ProductsJson);
        Assert.Equal(previous.ExtraJson, current.ExtraJson);
        Assert.Equal(boundary.Id, current.BaselineRawId);
        Assert.Equal(boundary.ShotOkTotal, current.BaselineShotOkTotal);
        Assert.Equal(2, current.CurrentPlcPeriodIndex);
        Assert.Equal(fixture.Clock.Unix, current.ActivePeriodStartAt);
        var final = await fixture.Oee.ProductionMetrics.SingleAsync(metric => metric.IsFinal);
        Assert.Equal(22, final.ActualQty);
        Assert.Equal(22, fixture.QuantityCache.GetCompletedQty("M-1", "ORDER"));
        Assert.False((await fixture.Oee.MachineStateEvents.SingleAsync()).IsOpen);
        Assert.Equal(2, await fixture.Oee.SyncOutboxMessages.CountAsync());
        Assert.Equal("paused", (await fixture.Oee.ProductionPeriods.SingleAsync(period => period.PeriodId == previous.SessionId)).Status);

        var nextRaw = new PlcRawInterval
        {
            Machine = "M-1", ReadAt = fixture.Clock.Unix + 5, ShotOkTotal = 25, ShotNgTotal = 3,
            RunTimeTotalSec = 35, StopTimeTotalSec = 5, ErrorTimeTotalSec = 0, RunState = "run"
        };
        fixture.Oee.PlcRawIntervals.Add(nextRaw);
        await fixture.Oee.SaveChangesAsync();
        var metrics = await fixture.Metrics.BuildAsync("GW", [nextRaw], nextRaw.ReadAt, default);
        Assert.Equal(12, Assert.Single(metrics.Metrics, metric => metric.BucketType == "session").ActualQty);
        Assert.Equal(34, final.ActualQty + metrics.Metrics.Single(metric => metric.BucketType == "session").ActualQty);
        await fixture.RestartAsync();
        Assert.Equal(22, fixture.QuantityCache.GetCompletedQty("M-1", "ORDER"));
    }

    [Fact]
    public async Task Overlapping_and_missed_schedules_rotate_each_machine_only_once_even_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMachineAsync("M-1");
        await fixture.StartAsync("M-1");
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, [], seconds: 10);
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, [], seconds: 20);
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);
        var sessionId = (await fixture.Oee.ProductionContexts.SingleAsync()).SessionId;
        Assert.Equal(2, await fixture.Oee.ProductionPeriods.CountAsync());
        Assert.Equal(1, await fixture.Oee.ProductionScheduleMachineRuns.CountAsync(run => run.CoalescedIntoId != null));
        await fixture.RestartAsync();
        await fixture.Runner.RunDueAsync(default);
        Assert.Equal(sessionId, (await fixture.Oee.ProductionContexts.SingleAsync()).SessionId);
        Assert.Equal(2, await fixture.Oee.ProductionScheduleRuns.CountAsync());
    }

    [Fact]
    public async Task Weekly_schedule_catches_up_latest_occurrence_and_starts_at_actual_time()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMachineAsync("M-1");
        await fixture.StartAsync("M-1");
        var result = await fixture.Service.SaveAsync(new ProductionScheduleInput
        {
            Name = "Daily", Kind = ProductionScheduleKind.Weekly, Target = ProductionScheduleTarget.All, LocalTime = "08:01",
            Weekdays = [0, 1, 2, 3, 4, 5, 6]
        }, default);
        Assert.True(result.Succeeded, result.ErrorMessage);
        fixture.Clock.Advance(3 * 86400 + 120);
        await fixture.RestartAsync();
        await fixture.Runner.RunDueAsync(default);
        var run = await fixture.Oee.ProductionScheduleRuns.SingleAsync();
        Assert.Equal(fixture.Clock.Unix - 60, run.ScheduledAt);
        Assert.Equal(fixture.Clock.Unix, (await fixture.Oee.ProductionContexts.SingleAsync()).ActivePeriodStartAt);
        Assert.True((await fixture.Oee.ProductionSchedules.SingleAsync()).NextRunAt > fixture.Clock.Unix);
        Assert.Equal(2, await fixture.Oee.ProductionPeriods.CountAsync());
    }

    [Fact]
    public async Task Failed_machine_rolls_back_retries_three_times_and_does_not_repeat_successful_machines()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMachineAsync("GOOD");
        await fixture.AddMachineAsync("BAD");
        await fixture.StartAsync("GOOD");
        await fixture.StartAsync("BAD");
        var badContext = await fixture.Oee.ProductionContexts.SingleAsync(context => context.Machine == "BAD");
        var badSession = badContext.SessionId;
        badContext.BaselineRawId = "baseline";
        badContext.BaselineCapturedAt = fixture.Clock.Unix - 100;
        fixture.Oee.PlcRawIntervals.Add(new PlcRawInterval
        {
            Machine = "BAD", ReadAt = fixture.Clock.Unix, ShotOkTotal = 10, RunTimeTotalSec = 30, RunState = "run"
        });
        await fixture.Oee.SaveChangesAsync();
        fixture.ContextCache.Upsert(badContext);
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, []);
        fixture.Faults.MachineCode = "BAD";
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);
        var goodSession = (await fixture.Oee.ProductionContexts.SingleAsync(context => context.Machine == "GOOD")).SessionId;
        Assert.StartsWith("SCH-", goodSession);
        Assert.Equal(badSession, (await fixture.Oee.ProductionContexts.SingleAsync(context => context.Machine == "BAD")).SessionId);
        Assert.Equal(badSession, fixture.ContextCache.GetActive("BAD", "ORDER")!.SessionId);
        Assert.Equal("active", (await fixture.Oee.ProductionPeriods.SingleAsync(period => period.PeriodId == badSession)).Status);
        Assert.Equal(0, fixture.QuantityCache.GetCompletedQty("BAD", "ORDER"));
        Assert.False(await fixture.Oee.ProductionMetrics.AnyAsync(metric => metric.Machine == "BAD"));
        Assert.False(await fixture.Oee.SyncOutboxMessages.AnyAsync());
        await fixture.AddMachineAsync("LATER");
        await fixture.StartAsync("LATER");
        await fixture.RestartAsync();
        await fixture.Runner.RunDueAsync(default);
        Assert.Equal(1, (await fixture.Oee.ProductionScheduleMachineRuns.SingleAsync(row => row.MachineCode == "BAD")).Attempts);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            fixture.Clock.Advance(5);
            await fixture.Runner.RunDueAsync(default);
        }
        Assert.Equal(goodSession, (await fixture.Oee.ProductionContexts.SingleAsync(context => context.Machine == "GOOD")).SessionId);
        Assert.Equal(badSession, (await fixture.Oee.ProductionContexts.SingleAsync(context => context.Machine == "BAD")).SessionId);
        var history = Assert.Single(await fixture.Service.HistoryAsync(null, default));
        Assert.Equal("partial", history.Run.Status);
        Assert.Equal(2, history.Machines.Count);
        var failed = Assert.Single(history.Machines, row => row.MachineCode == "BAD");
        Assert.Equal("failed", failed.Status);
        Assert.Equal(3, failed.Attempts);
    }

    [Fact]
    public async Task Editing_and_disabling_validate_dates_and_preserve_history_on_delete()
    {
        await using var fixture = await Fixture.CreateAsync();
        var machine = await fixture.AddMachineAsync("OLD-CODE");
        await fixture.StartAsync("OLD-CODE");
        var id = await fixture.SaveOnceAsync(ProductionScheduleTarget.Selected, [machine.Id]);
        machine.Code = "NEW-CODE";
        await fixture.Config.SaveChangesAsync();
        var input = (await fixture.Service.GetInputAsync(id, default))!;
        Assert.Equal(machine.Id, Assert.Single(input.MachineIds));
        Assert.Contains("NEW-CODE", Assert.Single(await fixture.Service.ListAsync(default)).MachineSummary);
        Assert.True((await fixture.Service.SetEnabledAsync(id, false, default)).Succeeded);
        fixture.Clock.Advance(120);
        Assert.False((await fixture.Service.SetEnabledAsync(id, true, default)).Succeeded);
        input.OnceLocalAt = ProductionScheduleClock.ToLocal(fixture.Clock.Unix - 1);
        Assert.False((await fixture.Service.SaveAsync(input, default)).Succeeded);
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, []);
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);
        var history = Assert.Single(await fixture.Service.HistoryAsync(null, default));
        Assert.True((await fixture.Service.DeleteAsync(history.Run.ScheduleId, default)).Succeeded);
        Assert.Single(await fixture.Service.HistoryAsync(history.Run.ScheduleId, default));
    }

    [Fact]
    public async Task Empty_weekdays_and_machine_selection_are_rejected_and_disabled_or_deleted_machines_are_skipped()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.False((await fixture.Service.SaveAsync(new ProductionScheduleInput
        {
            Name = "No days", Kind = ProductionScheduleKind.Weekly, LocalTime = "08:00", Weekdays = []
        }, default)).Succeeded);
        Assert.False((await fixture.Service.SaveAsync(new ProductionScheduleInput
        {
            Name = "No machines", Target = ProductionScheduleTarget.Selected, OnceLocalAt = ProductionScheduleClock.ToLocal(fixture.Clock.Unix + 60)
        }, default)).Succeeded);
        var disabled = await fixture.AddMachineAsync("DISABLED");
        var deleted = await fixture.AddMachineAsync("DELETED");
        await fixture.SaveOnceAsync(ProductionScheduleTarget.Selected, [disabled.Id, deleted.Id]);
        disabled.Enabled = false;
        fixture.Config.Machines.Remove(deleted);
        await fixture.Config.SaveChangesAsync();
        fixture.Clock.Advance(60);
        await fixture.Runner.RunDueAsync(default);
        var history = Assert.Single(await fixture.Service.HistoryAsync(null, default));
        Assert.Equal("skipped", history.Run.Status);
        Assert.Equal(2, history.Machines.Count);
        Assert.All(history.Machines, row => Assert.Equal("skipped", row.Status));
    }

    [Fact]
    public async Task Concurrent_mes_start_and_scheduler_keep_one_active_session_per_order()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMachineAsync("M-1");
        await fixture.StartAsync("M-1");
        await fixture.SaveOnceAsync(ProductionScheduleTarget.All, []);
        fixture.Clock.Advance(60);
        await Task.WhenAll(fixture.Runner.RunDueAsync(default), fixture.StartAsync("M-1"));
        Assert.Single(await fixture.Oee.ProductionContexts.ToListAsync());
        Assert.Equal(2, await fixture.Oee.ProductionPeriods.CountAsync());
    }

    private sealed class ManualClock : TimeProvider
    {
        public long Unix { get; private set; } = ProductionScheduleClock.FromLocal(new(2026, 10, 15, 8, 0, 0));
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(Unix);
        public void Advance(int seconds) => Unix += seconds;
    }

    private sealed class RotationFaults : DbCommandInterceptor
    {
        public string? MachineCode { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            var values = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value?.ToString()).ToList();
            if (MachineCode is not null && command.CommandText.Contains("INSERT INTO \"production_context\"", StringComparison.Ordinal) &&
                values.Contains(MachineCode) && values.Any(value => value?.StartsWith("SCH-", StringComparison.Ordinal) == true))
                throw new InvalidOperationException("Injected rotation failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _configConnection = new("Data Source=:memory:");
        private readonly SqliteConnection _oeeConnection = new("Data Source=:memory:");
        private readonly OeeProcessingGate _gate = new();
        private readonly MachineTemplate _template = new() { Code = "TPL", Name = "Template", Protocol = GatewayProtocol.ModbusTcp };
        public ManualClock Clock { get; } = new();
        public RotationFaults Faults { get; } = new();
        public GatewayDbContext Config { get; private set; } = null!;
        public OeeDbContext Oee { get; private set; } = null!;
        public ProductionContextCache ContextCache { get; private set; } = null!;
        public OrderQuantityCache QuantityCache { get; private set; } = null!;
        public ProductionMetricBuilder Metrics { get; private set; } = null!;
        public ProductionScheduleService Service { get; private set; } = null!;
        public ProductionScheduleRunner Runner { get; private set; } = null!;
        private ProductionCommandService _commands = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture._configConnection.OpenAsync();
            await fixture._oeeConnection.OpenAsync();
            fixture.Config = new(new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(fixture._configConnection).Options);
            await fixture.Config.Database.MigrateAsync();
            fixture.Config.MachineTemplates.Add(fixture._template);
            await fixture.Config.SaveChangesAsync();
            await fixture.RestartAsync();
            return fixture;
        }

        public async Task RestartAsync()
        {
            if (Oee is not null) await Oee.DisposeAsync();
            Oee = new(new DbContextOptionsBuilder<OeeDbContext>().UseSqlite(_oeeConnection).AddInterceptors(Faults).Options);
            await Oee.Database.MigrateAsync();
            var repository = new EfCoreOeeLocalRepository(Oee);
            var config = new EfCoreConfigRepository(Config);
            var schedules = new EfCoreProductionScheduleRepository(Oee);
            ContextCache = new();
            ContextCache.Replace(await repository.ListProductionContextsAsync(default));
            QuantityCache = new();
            QuantityCache.ReplaceCompletedSessions(await repository.ListFinalSessionProductionMetricsAsync(default));
            var options = Options.Create(new MesSyncOptions());
            Metrics = new(repository, ContextCache, options, NullLogger<ProductionMetricBuilder>.Instance);
            _commands = new(repository, ContextCache, QuantityCache, Metrics, options, config, NullLogger<ProductionCommandService>.Instance, _gate);
            Service = new(schedules, config, repository, _gate, Clock);
            Runner = new(schedules, config, repository, _commands, _gate, Clock, NullLogger<ProductionScheduleRunner>.Instance, "GW");
        }

        public async Task<Machine> AddMachineAsync(string code, bool enabled = true)
        {
            var machine = new Machine { Code = code, Name = code, TemplateId = _template.Id, Enabled = enabled };
            Config.Machines.Add(machine);
            await Config.SaveChangesAsync();
            return machine;
        }

        public async Task StartAsync(string machine, string order = "ORDER")
        {
            var result = await _commands.HandleAsync(new ProductionCommandRequest
            {
                CommandCode = Guid.NewGuid().ToString(), MachineCode = machine, Action = "start", ProductionOrderCode = order,
                OccurredAtUnixTimeSeconds = Clock.Unix - 100,
                Products = [new() { ProductCode = "PRODUCT", MoldCode = "MOLD", Cavity = 2, CycleTime = 10, TargetQty = 100 }]
            }, default);
            Assert.True(result.Accepted, result.Message);
        }

        public async Task<Guid> SaveOnceAsync(ProductionScheduleTarget target, List<Guid> machineIds, int seconds = 60)
        {
            var result = await Service.SaveAsync(new ProductionScheduleInput
            {
                Name = Guid.NewGuid().ToString(), Target = target, MachineIds = machineIds,
                Kind = ProductionScheduleKind.Once, OnceLocalAt = ProductionScheduleClock.ToLocal(Clock.Unix + seconds)
            }, default);
            Assert.True(result.Succeeded, result.ErrorMessage);
            return result.Value;
        }

        public async ValueTask DisposeAsync()
        {
            await Oee.DisposeAsync();
            await Config.DisposeAsync();
            await _oeeConnection.DisposeAsync();
            await _configConnection.DisposeAsync();
        }
    }
}
