using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609070004_AddProductionSessionToOeeRawIntervals")]
public partial class AddProductionSessionToOeeRawIntervals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PlcRawIntervals_MachineCode_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals");

        migrationBuilder.AddColumn<string>(
            name: "SessionId",
            table: "ProductionContexts",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ProductionOrderCode",
            table: "PlcRawIntervals",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "SessionId",
            table: "PlcRawIntervals",
            type: "TEXT",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_MachineCode_ProductionOrderCode_SessionId_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals",
            columns: new[] { "MachineCode", "ProductionOrderCode", "SessionId", "ReadAtUnixTimeSeconds" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PlcRawIntervals_MachineCode_ProductionOrderCode_SessionId_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals");

        migrationBuilder.DropColumn(
            name: "SessionId",
            table: "ProductionContexts");

        migrationBuilder.DropColumn(
            name: "ProductionOrderCode",
            table: "PlcRawIntervals");

        migrationBuilder.DropColumn(
            name: "SessionId",
            table: "PlcRawIntervals");

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_MachineCode_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals",
            columns: new[] { "MachineCode", "ReadAtUnixTimeSeconds" },
            unique: true);
    }
}
