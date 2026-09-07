using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609070002_AddOeeRawIntervals")]
public partial class AddOeeRawIntervals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlcRawIntervals",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                MachineCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                ReadAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                ReadAtUnixMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                MachineState = table.Column<int>(type: "INTEGER", nullable: true),
                ShotOkTotal = table.Column<long>(type: "INTEGER", nullable: true),
                ShotNgTotal = table.Column<long>(type: "INTEGER", nullable: true),
                CycleTimeMs = table.Column<int>(type: "INTEGER", nullable: true),
                RunTimeTotal = table.Column<long>(type: "INTEGER", nullable: true),
                StopTimeTotal = table.Column<long>(type: "INTEGER", nullable: true),
                ErrorTimeTotal = table.Column<long>(type: "INTEGER", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PlcRawIntervals", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_MachineCode_ReadAtUnixMilliseconds",
            table: "PlcRawIntervals",
            columns: new[] { "MachineCode", "ReadAtUnixMilliseconds" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_ReadAtUnixMilliseconds",
            table: "PlcRawIntervals",
            column: "ReadAtUnixMilliseconds");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PlcRawIntervals");
    }
}
