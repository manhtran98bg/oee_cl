using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609070001_AddProductionContextAndMesSyncOutbox")]
public partial class AddProductionContextAndMesSyncOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MesSyncOutboxMessages",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Topic = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Endpoint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                SyncedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_MesSyncOutboxMessages", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ProductionContexts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CommandCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                ProductionOrderCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                OperatorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                ReasonCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                Note = table.Column<string>(type: "TEXT", nullable: true),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                PausedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                StoppedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProductionContexts", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_MesSyncOutboxMessages_CreatedAtUtc",
            table: "MesSyncOutboxMessages",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_MesSyncOutboxMessages_Status_NextAttemptAtUtc",
            table: "MesSyncOutboxMessages",
            columns: new[] { "Status", "NextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_MesSyncOutboxMessages_Topic_Status",
            table: "MesSyncOutboxMessages",
            columns: new[] { "Topic", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_ProductionContexts_MachineCode",
            table: "ProductionContexts",
            column: "MachineCode",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProductionContexts_Status",
            table: "ProductionContexts",
            column: "Status");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MesSyncOutboxMessages");
        migrationBuilder.DropTable("ProductionContexts");
    }
}
