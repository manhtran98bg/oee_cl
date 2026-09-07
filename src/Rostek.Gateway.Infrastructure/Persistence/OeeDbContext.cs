using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Infrastructure.Persistence;

public sealed class OeeDbContext(DbContextOptions<OeeDbContext> options) : DbContext(options)
{
    public DbSet<ProductionContext> ProductionContexts => Set<ProductionContext>();
    public DbSet<MesSyncOutboxMessage> MesSyncOutboxMessages => Set<MesSyncOutboxMessage>();
    public DbSet<PlcRawInterval> PlcRawIntervals => Set<PlcRawInterval>();
    public DbSet<ProductionPeriod> ProductionPeriods => Set<ProductionPeriod>();
    public DbSet<ProductionMetric> ProductionMetrics => Set<ProductionMetric>();
    public DbSet<ProductMetric> ProductMetrics => Set<ProductMetric>();
    public DbSet<DowntimeEvent> DowntimeEvents => Set<DowntimeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProductionContext>(entity =>
        {
            entity.ToTable("production_context");
            entity.HasKey(context => context.Machine);
            entity.Property(context => context.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(context => context.Mode).HasColumnName("mode").HasMaxLength(50).IsRequired();
            entity.Property(context => context.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.ActivePeriodId).HasColumnName("active_period_id").HasMaxLength(100).IsRequired();
            entity.Property(context => context.CurrentPlcPeriodIndex).HasColumnName("current_plc_period_index").IsRequired();
            entity.Property(context => context.ProductsJson).HasColumnName("products_json").IsRequired();
            entity.Property(context => context.TagsJson).HasColumnName("tags_json").IsRequired();
            entity.Property(context => context.ExtraJson).HasColumnName("extra_json").IsRequired();
            entity.Property(context => context.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(context => context.UpdatedAt).HasColumnName("updated_at").IsRequired();
        });

        modelBuilder.Entity<ProductionPeriod>(entity =>
        {
            entity.ToTable("production_period");
            entity.HasKey(period => period.PeriodId);
            entity.Property(period => period.PeriodId).HasColumnName("period_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(period => period.PlcPeriodIndex).HasColumnName("plc_period_index").IsRequired();
            entity.Property(period => period.Mode).HasColumnName("mode").HasMaxLength(50).IsRequired();
            entity.Property(period => period.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(period => period.ProductsJson).HasColumnName("products_json").IsRequired();
            entity.Property(period => period.TagsJson).HasColumnName("tags_json").IsRequired();
            entity.Property(period => period.ExtraJson).HasColumnName("extra_json").IsRequired();
            entity.Property(period => period.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(period => period.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(period => period.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(period => period.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(period => period.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(period => new { period.Machine, period.OrderId, period.PlcPeriodIndex })
                .IsUnique()
                .HasDatabaseName("ux_production_period_machine_order_plc");
            entity.HasIndex(period => new { period.OrderId, period.StartAt })
                .HasDatabaseName("ix_production_period_order_start");
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
            entity.Property(raw => raw.PlcBootCounter).HasColumnName("plc_boot_counter").IsRequired();
            entity.Property(raw => raw.StatusFlags).HasColumnName("status_flags").IsRequired();
            entity.Property(raw => raw.PeriodActive).HasColumnName("period_active").IsRequired();
            entity.Property(raw => raw.PlcRestarted).HasColumnName("plc_restarted").IsRequired();
            entity.Property(raw => raw.ShotOkTotal).HasColumnName("shot_ok_total").IsRequired();
            entity.Property(raw => raw.ShotNgTotal).HasColumnName("shot_ng_total").IsRequired();
            entity.Property(raw => raw.MoldOpenTotal).HasColumnName("mold_open_total").IsRequired();
            entity.Property(raw => raw.RunTimeTotalSec).HasColumnName("run_time_total_sec").IsRequired();
            entity.Property(raw => raw.StopTimeTotalSec).HasColumnName("stop_time_total_sec").IsRequired();
            entity.Property(raw => raw.ErrorTimeTotalSec).HasColumnName("error_time_total_sec").IsRequired();
            entity.Property(raw => raw.CycleTimeMs).HasColumnName("cycle_time_ms").IsRequired();
            entity.Property(raw => raw.CycleAvg10TimeMs).HasColumnName("cycle_avg10_time_ms").IsRequired();
            entity.HasIndex(raw => new { raw.Machine, raw.ReadAt }).IsUnique().HasDatabaseName("ux_plc_raw_interval_machine_read");
            entity.HasIndex(raw => new { raw.Machine, raw.PlcPeriodIndex, raw.ReadAt }).HasDatabaseName("ix_plc_raw_interval_machine_period_read");
        });

        modelBuilder.Entity<ProductionMetric>(entity =>
        {
            entity.ToTable("production_metric");
            entity.HasKey(metric => metric.Id);
            entity.Property(metric => metric.Id).HasColumnName("id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.MetricType).HasColumnName("metric_type").HasMaxLength(50).IsRequired();
            entity.Property(metric => metric.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.PeriodId).HasColumnName("period_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.ProductId).HasColumnName("product_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.RunState).HasColumnName("run_state").HasMaxLength(50).IsRequired();
            entity.Property(metric => metric.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(metric => metric.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(metric => metric.TotalQty).HasColumnName("total_qty").IsRequired();
            entity.Property(metric => metric.NgQty).HasColumnName("ng_qty").IsRequired();
            entity.Property(metric => metric.PlanQty).HasColumnName("plan_qty").IsRequired();
            entity.Property(metric => metric.ProdTimeSec).HasColumnName("prod_time_sec").IsRequired();
            entity.Property(metric => metric.RunTimeSec).HasColumnName("run_time_sec").IsRequired();
            entity.Property(metric => metric.StopTimeSec).HasColumnName("stop_time_sec").IsRequired();
            entity.Property(metric => metric.ErrorTimeSec).HasColumnName("error_time_sec").IsRequired();
            entity.Property(metric => metric.Availability).HasColumnName("availability").IsRequired();
            entity.Property(metric => metric.Performance).HasColumnName("performance").IsRequired();
            entity.Property(metric => metric.Quality).HasColumnName("quality").IsRequired();
            entity.Property(metric => metric.ActualCycleSec).HasColumnName("actual_cycle_sec").IsRequired();
            entity.Property(metric => metric.Oee).HasColumnName("oee").IsRequired();
            entity.Property(metric => metric.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(metric => metric.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(metric => new { metric.MetricType, metric.PeriodId, metric.ProductId, metric.RunState, metric.StartAt })
                .IsUnique()
                .HasDatabaseName("ux_production_metric_identity");
            entity.HasIndex(metric => new { metric.MetricType, metric.OrderId, metric.StartAt })
                .HasDatabaseName("ix_production_metric_type_order_start");
            entity.HasIndex(metric => new { metric.PeriodId, metric.ProductId, metric.StartAt })
                .HasDatabaseName("ix_production_metric_periodid_productid_start");
        });

        modelBuilder.Entity<ProductMetric>(entity =>
        {
            entity.ToTable("product_metric");
            entity.HasKey(metric => new { metric.OrderId, metric.ProductId });
            entity.Property(metric => metric.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.ProductId).HasColumnName("product_id").HasMaxLength(100).IsRequired();
            entity.Property(metric => metric.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(metric => metric.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(metric => metric.TotalQty).HasColumnName("total_qty").IsRequired();
            entity.Property(metric => metric.NgQty).HasColumnName("ng_qty").IsRequired();
            entity.Property(metric => metric.CountCheckQty).HasColumnName("count_check_qty").IsRequired();
            entity.Property(metric => metric.PlanQty).HasColumnName("plan_qty").IsRequired();
            entity.Property(metric => metric.ProdTimeSec).HasColumnName("prod_time_sec").IsRequired();
            entity.Property(metric => metric.RunTimeSec).HasColumnName("run_time_sec").IsRequired();
            entity.Property(metric => metric.StopTimeSec).HasColumnName("stop_time_sec").IsRequired();
            entity.Property(metric => metric.ErrorTimeSec).HasColumnName("error_time_sec").IsRequired();
            entity.Property(metric => metric.Availability).HasColumnName("availability").IsRequired();
            entity.Property(metric => metric.Performance).HasColumnName("performance").IsRequired();
            entity.Property(metric => metric.Quality).HasColumnName("quality").IsRequired();
            entity.Property(metric => metric.ActualCycleSec).HasColumnName("actual_cycle_sec").IsRequired();
            entity.Property(metric => metric.Oee).HasColumnName("oee").IsRequired();
            entity.Property(metric => metric.TargetQty).HasColumnName("target_qty").IsRequired();
            entity.Property(metric => metric.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(metric => metric.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(metric => metric.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(metric => new { metric.Status, metric.UpdatedAt }).HasDatabaseName("ix_product_metric_status_updated");
        });

        modelBuilder.Entity<DowntimeEvent>(entity =>
        {
            entity.ToTable("downtime_event");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(100).IsRequired();
            entity.Property(item => item.Machine).HasColumnName("machine").HasMaxLength(100).IsRequired();
            entity.Property(item => item.OrderId).HasColumnName("order_id").HasMaxLength(100).IsRequired();
            entity.Property(item => item.ServerOrderId).HasColumnName("server_order_id").HasMaxLength(100).IsRequired();
            entity.Property(item => item.PeriodId).HasColumnName("period_id").HasMaxLength(100).IsRequired();
            entity.Property(item => item.PlcPeriodIndex).HasColumnName("plc_period_index").IsRequired();
            entity.Property(item => item.State).HasColumnName("state").HasMaxLength(50).IsRequired();
            entity.Property(item => item.StartAt).HasColumnName("start_at").IsRequired();
            entity.Property(item => item.EndAt).HasColumnName("end_at").IsRequired();
            entity.Property(item => item.DurationSec).HasColumnName("duration_sec").IsRequired();
            entity.Property(item => item.Category).HasColumnName("category").HasMaxLength(100).IsRequired();
            entity.Property(item => item.Error).HasColumnName("error").HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasColumnName("description").HasMaxLength(1000).IsRequired();
            entity.Property(item => item.OrderExtraJson).HasColumnName("order_extra_json").IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(item => new { item.Machine, item.StartAt }).HasDatabaseName("ix_downtime_event_machine_start");
            entity.HasIndex(item => new { item.OrderId, item.PeriodId, item.StartAt }).HasDatabaseName("ix_downtime_event_order_period_start");
        });

        modelBuilder.Entity<MesSyncOutboxMessage>(entity =>
        {
            entity.ToTable("sync_outbox");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).HasColumnName("id").HasMaxLength(100).IsRequired();
            entity.Property(message => message.Topic).HasColumnName("topic").HasMaxLength(100).IsRequired();
            entity.Property(message => message.SourceTable).HasColumnName("source_table").HasMaxLength(100).IsRequired();
            entity.Property(message => message.SourceId).HasColumnName("source_id").HasMaxLength(200).IsRequired();
            entity.Property(message => message.PayloadJson).HasColumnName("payload_json").IsRequired();
            entity.Property(message => message.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(message => message.RetryCount).HasColumnName("retry_count").IsRequired();
            entity.Property(message => message.LastError).HasColumnName("last_error").HasMaxLength(2000).IsRequired();
            entity.Property(message => message.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(message => message.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.Property(message => message.SyncedAt).HasColumnName("synced_at").IsRequired();
            entity.HasIndex(message => new { message.Topic, message.Status, message.UpdatedAt })
                .HasDatabaseName("ix_sync_outbox_topic_status_updated");
            entity.HasIndex(message => new { message.SourceTable, message.SourceId })
                .HasDatabaseName("ix_sync_outbox_source");
        });
    }
}
