using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(OeeDbContext))]
[Migration("202609070001_InitialOeeSchema")]
public partial class InitialOeeSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create table production_context (
                machine TEXT not null primary key,
                mode TEXT not null,
                order_id TEXT not null default '',
                server_order_id TEXT not null default '',
                active_period_id TEXT not null default '',
                current_plc_period_index INTEGER not null default 0,
                products_json TEXT not null default '[]',
                tags_json TEXT not null default '[]',
                extra_json TEXT not null default '{}',
                status TEXT not null default 'active',
                updated_at INTEGER not null
            );

            create table production_period (
                period_id TEXT not null primary key,
                machine TEXT not null,
                plc_period_index INTEGER not null,
                mode TEXT not null,
                order_id TEXT not null default '',
                server_order_id TEXT not null default '',
                products_json TEXT not null default '[]',
                tags_json TEXT not null default '[]',
                extra_json TEXT not null default '{}',
                start_at INTEGER not null,
                end_at INTEGER not null default 0,
                status TEXT not null,
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create unique index ux_production_period_machine_order_plc
            on production_period(machine, order_id, plc_period_index);

            create index ix_production_period_order_start
            on production_period(order_id, start_at);

            create table plc_raw_interval (
                id TEXT not null primary key,
                machine TEXT not null,
                read_at INTEGER not null,
                plc_period_index INTEGER not null default 0,
                run_state TEXT not null,
                plc_boot_counter INTEGER not null default 0,
                status_flags INTEGER not null default 0,
                period_active INTEGER not null default 0,
                plc_restarted INTEGER not null default 0,
                shot_ok_total INTEGER not null default 0,
                shot_ng_total INTEGER not null default 0,
                mold_open_total INTEGER not null default 0,
                run_time_total_sec INTEGER not null default 0,
                stop_time_total_sec INTEGER not null default 0,
                error_time_total_sec INTEGER not null default 0,
                cycle_time_ms INTEGER not null default 0,
                cycle_avg10_time_ms INTEGER not null default 0
            );

            create unique index ux_plc_raw_interval_machine_read
            on plc_raw_interval(machine, read_at);

            create index ix_plc_raw_interval_machine_read
            on plc_raw_interval(machine, read_at);

            create index ix_plc_raw_interval_machine_period_read
            on plc_raw_interval(machine, plc_period_index, read_at);

            create table production_metric (
                id TEXT not null primary key,
                metric_type TEXT not null,
                machine TEXT not null,
                order_id TEXT not null default '',
                server_order_id TEXT not null default '',
                period_id TEXT not null default '',
                product_id TEXT not null default '',
                run_state TEXT not null default '',
                start_at INTEGER not null,
                end_at INTEGER not null,
                total_qty INTEGER not null default 0,
                ng_qty INTEGER not null default 0,
                plan_qty REAL not null default 0,
                prod_time_sec INTEGER not null default 0,
                run_time_sec INTEGER not null default 0,
                stop_time_sec INTEGER not null default 0,
                error_time_sec INTEGER not null default 0,
                availability REAL not null default 0,
                performance REAL not null default 0,
                quality REAL not null default 0,
                actual_cycle_sec REAL not null default 0,
                oee REAL not null default 0,
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create unique index ux_production_metric_identity
            on production_metric(metric_type, period_id, product_id, run_state, start_at);

            create index ix_production_metric_type_order_start
            on production_metric(metric_type, order_id, start_at);

            create index ix_production_metric_periodid_productid_start
            on production_metric(period_id, product_id, start_at);

            create table product_metric (
                machine TEXT not null,
                server_order_id TEXT not null default '',
                order_id TEXT not null default '',
                product_id TEXT not null default '',
                start_at INTEGER not null,
                end_at INTEGER not null,
                total_qty INTEGER not null default 0,
                ng_qty INTEGER not null default 0,
                count_check_qty INTEGER not null default 0,
                plan_qty REAL not null default 0,
                prod_time_sec INTEGER not null default 0,
                run_time_sec INTEGER not null default 0,
                stop_time_sec INTEGER not null default 0,
                error_time_sec INTEGER not null default 0,
                availability REAL not null default 0,
                performance REAL not null default 0,
                quality REAL not null default 0,
                actual_cycle_sec REAL not null default 0,
                oee REAL not null default 0,
                target_qty INTEGER not null default 0,
                status TEXT not null default 'process',
                created_at INTEGER not null,
                updated_at INTEGER not null,
                primary key (order_id, product_id)
            );

            create index ix_product_metric_status_updated
            on product_metric(status, updated_at);

            create table downtime_event (
                id TEXT not null primary key,
                machine TEXT not null,
                order_id TEXT not null default '',
                server_order_id TEXT not null default '',
                period_id TEXT not null default '',
                plc_period_index INTEGER not null default 0,
                state TEXT not null,
                start_at INTEGER not null,
                end_at INTEGER not null default 0,
                duration_sec INTEGER not null default 0,
                category TEXT not null default '',
                error TEXT not null default '',
                description TEXT not null default '',
                order_extra_json TEXT not null default '{}',
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create index ix_downtime_event_machine_start
            on downtime_event(machine, start_at);

            create index ix_downtime_event_order_period_start
            on downtime_event(order_id, period_id, start_at);

            create table sync_outbox (
                id TEXT not null primary key,
                topic TEXT not null,
                source_table TEXT not null default '',
                source_id TEXT not null default '',
                payload_json TEXT not null default '{}',
                status TEXT not null default 'pending',
                retry_count INTEGER not null default 0,
                last_error TEXT not null default '',
                created_at INTEGER not null,
                updated_at INTEGER not null,
                synced_at INTEGER not null default 0
            );

            create index ix_sync_outbox_topic_status_updated
            on sync_outbox(topic, status, updated_at);

            create index ix_sync_outbox_source
            on sync_outbox(source_table, source_id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists production_context;
            drop table if exists production_period;
            drop table if exists plc_raw_interval;
            drop table if exists production_metric;
            drop table if exists product_metric;
            drop table if exists downtime_event;
            drop table if exists sync_outbox;
            """);
    }
}
