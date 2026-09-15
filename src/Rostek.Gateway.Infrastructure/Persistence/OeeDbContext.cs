using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Infrastructure.Persistence;

public sealed class OeeDbContext(DbContextOptions<OeeDbContext> options) : DbContext(options)
{
    public DbSet<ProductionContext> ProductionContexts => Set<ProductionContext>();
    public DbSet<PlcRawInterval> PlcRawIntervals => Set<PlcRawInterval>();
    public DbSet<ProductionPeriod> ProductionPeriods => Set<ProductionPeriod>();
    public DbSet<MachineStateEvent> MachineStateEvents => Set<MachineStateEvent>();
    public DbSet<SyncOutboxMessage> SyncOutboxMessages => Set<SyncOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductionContext>(entity =>
        {
            entity.ToTable("production_context");
            entity.HasKey(context => context.SessionId);
            entity.Property(context => context.SessionId).HasColumnName("session_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(context => context.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(context => context.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.ActivePeriodStartAt).HasColumnName("active_period_start_at").IsRequired();
            entity.Property(context => context.CurrentPlcPeriodIndex).HasColumnName("current_plc_period_index").IsRequired();
            entity.Property(context => context.ProductsJson).HasColumnName("products_json").IsRequired();
            entity.Property(context => context.ExtraJson).HasColumnName("extra_json").IsRequired();
            entity.Property(context => context.BaselineRawId).HasColumnName("baseline_raw_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.BaselineCapturedAt).HasColumnName("baseline_captured_at").IsRequired();
            entity.Property(context => context.BaselineShotOkTotal).HasColumnName("baseline_shot_ok_total").IsRequired();
            entity.Property(context => context.BaselineShotNgTotal).HasColumnName("baseline_shot_ng_total").IsRequired();
            entity.Property(context => context.BaselineRunTimeTotalSec).HasColumnName("baseline_run_time_total_sec").IsRequired();
            entity.Property(context => context.BaselineStopTimeTotalSec).HasColumnName("baseline_stop_time_total_sec").IsRequired();
            entity.Property(context => context.BaselineErrorTimeTotalSec).HasColumnName("baseline_error_time_total_sec").IsRequired();
            entity.Property(context => context.BaselineCycleTimeMs).HasColumnName("baseline_cycle_time_ms").IsRequired();
            entity.Property(context => context.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(context => new { context.Machine, context.OrderId, context.Status })
                .HasDatabaseName("ix_production_context_machine_order_status");
            entity.HasIndex(context => new { context.Machine, context.OrderId })
                .IsUnique()
                .HasDatabaseName("ux_production_context_machine_order");
        });

        modelBuilder.Entity<ProductionPeriod>(entity =>
        {
            entity.ToTable("production_period");
            entity.HasKey(period => period.PeriodId);
            entity.Property(period => period.PeriodId).HasColumnName("period_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(period => period.PlcPeriodIndex).HasColumnName("plc_period_index").IsRequired();
            entity.Property(period => period.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.ProductsJson).HasColumnName("products_json").IsRequired();
            entity.Property(period => period.ExtraJson).HasColumnName("extra_json").IsRequired();
            entity.Property(period => period.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(period => period.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(period => period.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(period => period.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(period => period.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(period => new { period.Machine, period.OrderId, period.PlcPeriodIndex })
                .IsUnique()
                .HasDatabaseName("ux_production_period_machine_order_plc");
            entity.HasIndex(period => new { period.Machine, period.Status })
                .HasDatabaseName("ix_production_period_machine_status");
        });

        modelBuilder.Entity<PlcRawInterval>(entity =>
        {
            entity.ToTable("plc_raw_interval");
            entity.HasKey(raw => raw.Id);
            entity.Property(raw => raw.Id).HasColumnName("id").HasMaxLength(100).IsRequired();
            entity.Property(raw => raw.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(raw => raw.ReadAt).HasColumnName("read_at").IsRequired();
            entity.Property(raw => raw.PlcPeriodIndex).HasColumnName("plc_period_index").IsRequired();
            entity.Property(raw => raw.RunState).HasColumnName("run_state").HasMaxLength(50).IsRequired();
            entity.Property(raw => raw.PeriodActive).HasColumnName("period_active").IsRequired();
            entity.Property(raw => raw.ShotOkTotal).HasColumnName("shot_ok_total").IsRequired();
            entity.Property(raw => raw.ShotNgTotal).HasColumnName("shot_ng_total").IsRequired();
            entity.Property(raw => raw.RunTimeTotalSec).HasColumnName("run_time_total_sec").IsRequired();
            entity.Property(raw => raw.StopTimeTotalSec).HasColumnName("stop_time_total_sec").IsRequired();
            entity.Property(raw => raw.ErrorTimeTotalSec).HasColumnName("error_time_total_sec").IsRequired();
            entity.Property(raw => raw.CycleTimeMs).HasColumnName("cycle_time_ms").IsRequired();
            entity.HasIndex(raw => new { raw.Machine, raw.ReadAt }).IsUnique().HasDatabaseName("ux_plc_raw_interval_machine_read");
            entity.HasIndex(raw => new { raw.Machine, raw.PlcPeriodIndex, raw.ReadAt }).HasDatabaseName("ix_plc_raw_interval_machine_period_read");
        });

        modelBuilder.Entity<MachineStateEvent>(entity =>
        {
            entity.ToTable("machine_state_event");
            entity.HasKey(stateEvent => stateEvent.EventId);
            entity.Property(stateEvent => stateEvent.EventId).HasColumnName("event_id").HasMaxLength(200).IsRequired();
            entity.Property(stateEvent => stateEvent.GatewayId).HasColumnName("gateway_id").HasMaxLength(100).IsRequired();
            entity.Property(stateEvent => stateEvent.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(stateEvent => stateEvent.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(stateEvent => stateEvent.SessionId).HasColumnName("session_id").HasMaxLength(100).IsRequired();
            entity.Property(stateEvent => stateEvent.State).HasColumnName("state").HasMaxLength(50).IsRequired();
            entity.Property(stateEvent => stateEvent.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(stateEvent => stateEvent.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(stateEvent => stateEvent.DurationSec).HasColumnName("duration_sec").IsRequired();
            entity.Property(stateEvent => stateEvent.IsOpen).HasColumnName("is_open").IsRequired();
            entity.Property(stateEvent => stateEvent.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(stateEvent => stateEvent.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(stateEvent => new { stateEvent.Machine, stateEvent.SessionId, stateEvent.IsOpen })
                .HasDatabaseName("ix_machine_state_event_machine_session_open");
            entity.HasIndex(stateEvent => new { stateEvent.Machine, stateEvent.StartAt })
                .HasDatabaseName("ix_machine_state_event_machine_start");
            entity.HasIndex(stateEvent => new { stateEvent.OrderId, stateEvent.StartAt })
                .HasDatabaseName("ix_machine_state_event_order_start");
        });

        modelBuilder.Entity<SyncOutboxMessage>(entity =>
        {
            entity.ToTable("sync_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(message => message.Topic).HasColumnName("topic").HasMaxLength(100).IsRequired();
            entity.Property(message => message.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(300).IsRequired();
            entity.Property(message => message.EndpointPath).HasColumnName("endpoint_path").HasMaxLength(300).IsRequired();
            entity.Property(message => message.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(message => message.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(message => message.AttemptCount).HasColumnName("attempt_count").IsRequired();
            entity.Property(message => message.NextAttemptAt).HasColumnName("next_attempt_at").IsRequired();
            entity.Property(message => message.LastError).HasColumnName("last_error");
            entity.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(message => message.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(message => message.SyncedAt).HasColumnName("synced_at");
            entity.HasIndex(message => message.DedupeKey).IsUnique().HasDatabaseName("ux_sync_outbox_dedupe_key");
            entity.HasIndex(message => new { message.Status, message.NextAttemptAt }).HasDatabaseName("ix_sync_outbox_status_next_attempt");
            entity.HasIndex(message => new { message.Topic, message.Status }).HasDatabaseName("ix_sync_outbox_topic_status");
        });
    }
}
