using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(OeeDbContext))]
[Migration("202609150001_AddProductionMetrics")]
public partial class AddProductionMetrics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create table if not exists production_metric (
                metric_id TEXT not null primary key,
                bucket_type TEXT not null,
                bucket_start INTEGER not null,
                bucket_end INTEGER not null,
                is_final INTEGER not null,
                gateway_id TEXT not null,
                machine TEXT not null,
                order_id TEXT not null,
                session_id TEXT null,
                product_code TEXT not null,
                mold_code TEXT null,
                machine_state TEXT not null,
                actual_qty INTEGER not null,
                total_qty INTEGER not null,
                planned_qty TEXT not null,
                target_qty INTEGER not null,
                run_time INTEGER not null,
                stop_time INTEGER not null,
                error_time INTEGER not null,
                production_time INTEGER not null,
                availability TEXT not null,
                performance TEXT not null,
                quality TEXT not null,
                oee TEXT not null,
                extra_json TEXT not null,
                created_at INTEGER not null,
                updated_at INTEGER not null
            );

            create index if not exists ix_production_metric_bucket
            on production_metric(bucket_type, bucket_start, bucket_end);

            create index if not exists ix_production_metric_machine_order_bucket
            on production_metric(machine, order_id, bucket_type);

            create index if not exists ix_production_metric_final_updated
            on production_metric(is_final, updated_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists production_metric;");
    }
}
