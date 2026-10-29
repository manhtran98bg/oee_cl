using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(OeeDbContext))]
[Migration("202610290001_AddProductionQuantitySource")]
public sealed class AddProductionQuantitySource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create table if not exists production_quantity_source_event (
                event_code TEXT not null primary key,
                request_hash TEXT not null,
                gateway_id TEXT not null,
                request_created_at INTEGER not null,
                machine TEXT not null,
                order_id TEXT not null,
                session_id TEXT null,
                product_code TEXT not null,
                previous_source TEXT null,
                quantity_update_source TEXT not null,
                accepted INTEGER not null,
                error_code TEXT null,
                error_message TEXT null,
                processed_at INTEGER not null
            );

            create index if not exists ix_quantity_source_machine_order_product_time
            on production_quantity_source_event(machine, order_id, product_code, processed_at);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop table if exists production_quantity_source_event;");
    }
}
