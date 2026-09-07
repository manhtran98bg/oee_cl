using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609070006_RemoveOeeTablesFromConfigDb")]
public partial class RemoveOeeTablesFromConfigDb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop table if exists ProductionContexts;
            drop table if exists PlcRawIntervals;
            drop table if exists MesSyncOutboxMessages;
            drop table if exists production_context;
            drop table if exists production_period;
            drop table if exists plc_raw_interval;
            drop table if exists production_metric;
            drop table if exists product_metric;
            drop table if exists downtime_event;
            drop table if exists sync_outbox;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
