using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(OeeDbContext))]
[Migration("202609090001_RebuildRealtimeOeeSchema")]
public partial class RebuildRealtimeOeeSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists sync_outbox;
            drop table if exists downtime_event;
            drop table if exists product_metric;
            drop table if exists production_metric;
            drop table if exists plc_raw_interval;
            drop table if exists production_period;
            drop table if exists production_context;

            create table production_context (
                machine TEXT not null primary key,
                status TEXT not null default 'active',
                order_code TEXT not null default '',
                server_order_id TEXT not null default '',
                active_period_id TEXT not null default '',
                active_period_start_at INTEGER not null default 0,
                current_plc_period_index INTEGER not null default 0,
                products_json TEXT not null default '[]',
                extra_json TEXT not null default '{}',
                baseline_raw_id TEXT not null default '',
                baseline_captured_at INTEGER not null default 0,
                baseline_shot_ok_total INTEGER not null default 0,
                baseline_shot_ng_total INTEGER not null default 0,
                baseline_run_time_total_sec INTEGER not null default 0,
                baseline_stop_time_total_sec INTEGER not null default 0,
                baseline_error_time_total_sec INTEGER not null default 0,
                baseline_cycle_time_ms INTEGER not null default 0,
                updated_at INTEGER not null
            );

            create table production_period (
                period_id TEXT not null primary key,
                machine TEXT not null,
                plc_period_index INTEGER not null,
                order_code TEXT not null default '',
                server_order_id TEXT not null default '',
                products_json TEXT not null default '[]',
                extra_json TEXT not null default '{}',
                start_at INTEGER not null,
                end_at INTEGER not null default 0,
                status TEXT not null,
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create unique index ux_production_period_machine_order_plc
            on production_period(machine, order_code, plc_period_index);

            create index ix_production_period_machine_status
            on production_period(machine, status);

            create table plc_raw_interval (
                id TEXT not null primary key,
                machine TEXT not null,
                read_at INTEGER not null,
                plc_period_index INTEGER not null default 0,
                run_state TEXT not null,
                period_active INTEGER not null default 0,
                shot_ok_total INTEGER not null default 0,
                shot_ng_total INTEGER not null default 0,
                run_time_total_sec INTEGER not null default 0,
                stop_time_total_sec INTEGER not null default 0,
                error_time_total_sec INTEGER not null default 0,
                cycle_time_ms INTEGER not null default 0
            );

            create unique index ux_plc_raw_interval_machine_read
            on plc_raw_interval(machine, read_at);

            create index ix_plc_raw_interval_machine_period_read
            on plc_raw_interval(machine, plc_period_index, read_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists plc_raw_interval;
            drop table if exists production_period;
            drop table if exists production_context;
            """);
    }
}
