using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609070003_UseUnixTimestampsForMesOee")]
public partial class UseUnixTimestampsForMesOee : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_MesSyncOutboxMessages_CreatedAtUtc", "MesSyncOutboxMessages");
        migrationBuilder.DropIndex("IX_MesSyncOutboxMessages_Status_NextAttemptAtUtc", "MesSyncOutboxMessages");
        migrationBuilder.DropIndex("IX_PlcRawIntervals_MachineCode_ReadAtUnixMilliseconds", "PlcRawIntervals");
        migrationBuilder.DropIndex("IX_PlcRawIntervals_ReadAtUnixMilliseconds", "PlcRawIntervals");

        migrationBuilder.Sql("""
            create table MesSyncOutboxMessages_new (
                Id INTEGER not null constraint PK_MesSyncOutboxMessages primary key autoincrement,
                Topic TEXT not null,
                Endpoint TEXT not null,
                PayloadJson TEXT not null,
                Status TEXT not null,
                RetryCount INTEGER not null,
                LastError TEXT null,
                NextAttemptUnixTimeSeconds INTEGER not null,
                CreatedUnixTimeSeconds INTEGER not null,
                UpdatedUnixTimeSeconds INTEGER not null,
                SyncedUnixTimeSeconds INTEGER null
            );

            insert into MesSyncOutboxMessages_new (
                Id,
                Topic,
                Endpoint,
                PayloadJson,
                Status,
                RetryCount,
                LastError,
                NextAttemptUnixTimeSeconds,
                CreatedUnixTimeSeconds,
                UpdatedUnixTimeSeconds,
                SyncedUnixTimeSeconds
            )
            select
                Id,
                Topic,
                Endpoint,
                PayloadJson,
                Status,
                RetryCount,
                LastError,
                coalesce(cast(strftime('%s', NextAttemptAtUtc) as integer), 0),
                coalesce(cast(strftime('%s', CreatedAtUtc) as integer), 0),
                coalesce(cast(strftime('%s', UpdatedAtUtc) as integer), 0),
                case
                    when SyncedAtUtc is null then null
                    else cast(strftime('%s', SyncedAtUtc) as integer)
                end
            from MesSyncOutboxMessages;

            drop table MesSyncOutboxMessages;
            alter table MesSyncOutboxMessages_new rename to MesSyncOutboxMessages;
            """);

        migrationBuilder.Sql("""
            create table ProductionContexts_new (
                Id TEXT not null constraint PK_ProductionContexts primary key,
                MachineCode TEXT not null,
                CommandCode TEXT not null,
                Status TEXT not null,
                ProductionOrderCode TEXT null,
                OperatorCode TEXT null,
                ReasonCode TEXT null,
                Note TEXT null,
                StartedUnixTimeSeconds INTEGER null,
                PausedUnixTimeSeconds INTEGER null,
                StoppedUnixTimeSeconds INTEGER null,
                UpdatedUnixTimeSeconds INTEGER not null
            );

            insert into ProductionContexts_new (
                Id,
                MachineCode,
                CommandCode,
                Status,
                ProductionOrderCode,
                OperatorCode,
                ReasonCode,
                Note,
                StartedUnixTimeSeconds,
                PausedUnixTimeSeconds,
                StoppedUnixTimeSeconds,
                UpdatedUnixTimeSeconds
            )
            select
                Id,
                MachineCode,
                CommandCode,
                Status,
                ProductionOrderCode,
                OperatorCode,
                ReasonCode,
                Note,
                case
                    when StartedAtUtc is null then null
                    else cast(strftime('%s', StartedAtUtc) as integer)
                end,
                case
                    when PausedAtUtc is null then null
                    else cast(strftime('%s', PausedAtUtc) as integer)
                end,
                case
                    when StoppedAtUtc is null then null
                    else cast(strftime('%s', StoppedAtUtc) as integer)
                end,
                coalesce(cast(strftime('%s', UpdatedAtUtc) as integer), 0)
            from ProductionContexts;

            drop table ProductionContexts;
            alter table ProductionContexts_new rename to ProductionContexts;
            """);

        migrationBuilder.Sql("""
            create table PlcRawIntervals_new (
                Id INTEGER not null constraint PK_PlcRawIntervals primary key autoincrement,
                MachineCode TEXT not null,
                ReadAtUnixTimeSeconds INTEGER not null,
                MachineState INTEGER null,
                ShotOkTotal INTEGER null,
                ShotNgTotal INTEGER null,
                CycleTimeMs INTEGER null,
                RunTimeTotal INTEGER null,
                StopTimeTotal INTEGER null,
                ErrorTimeTotal INTEGER null,
                CreatedUnixTimeSeconds INTEGER not null
            );

            insert into PlcRawIntervals_new (
                Id,
                MachineCode,
                ReadAtUnixTimeSeconds,
                MachineState,
                ShotOkTotal,
                ShotNgTotal,
                CycleTimeMs,
                RunTimeTotal,
                StopTimeTotal,
                ErrorTimeTotal,
                CreatedUnixTimeSeconds
            )
            select
                Id,
                MachineCode,
                ReadAtUnixMilliseconds / 1000,
                MachineState,
                ShotOkTotal,
                ShotNgTotal,
                CycleTimeMs,
                RunTimeTotal,
                StopTimeTotal,
                ErrorTimeTotal,
                coalesce(cast(strftime('%s', CreatedAtUtc) as integer), 0)
            from PlcRawIntervals;

            drop table PlcRawIntervals;
            alter table PlcRawIntervals_new rename to PlcRawIntervals;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_MesSyncOutboxMessages_CreatedUnixTimeSeconds",
            table: "MesSyncOutboxMessages",
            column: "CreatedUnixTimeSeconds");

        migrationBuilder.CreateIndex(
            name: "IX_MesSyncOutboxMessages_Status_NextAttemptUnixTimeSeconds",
            table: "MesSyncOutboxMessages",
            columns: new[] { "Status", "NextAttemptUnixTimeSeconds" });

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

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_MachineCode_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals",
            columns: new[] { "MachineCode", "ReadAtUnixTimeSeconds" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlcRawIntervals_ReadAtUnixTimeSeconds",
            table: "PlcRawIntervals",
            column: "ReadAtUnixTimeSeconds");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_MesSyncOutboxMessages_CreatedUnixTimeSeconds", "MesSyncOutboxMessages");
        migrationBuilder.DropIndex("IX_MesSyncOutboxMessages_Status_NextAttemptUnixTimeSeconds", "MesSyncOutboxMessages");
        migrationBuilder.DropIndex("IX_MesSyncOutboxMessages_Topic_Status", "MesSyncOutboxMessages");
        migrationBuilder.DropIndex("IX_ProductionContexts_MachineCode", "ProductionContexts");
        migrationBuilder.DropIndex("IX_ProductionContexts_Status", "ProductionContexts");
        migrationBuilder.DropIndex("IX_PlcRawIntervals_MachineCode_ReadAtUnixTimeSeconds", "PlcRawIntervals");
        migrationBuilder.DropIndex("IX_PlcRawIntervals_ReadAtUnixTimeSeconds", "PlcRawIntervals");

        migrationBuilder.Sql("""
            create table MesSyncOutboxMessages_old (
                Id INTEGER not null constraint PK_MesSyncOutboxMessages primary key autoincrement,
                Topic TEXT not null,
                Endpoint TEXT not null,
                PayloadJson TEXT not null,
                Status TEXT not null,
                RetryCount INTEGER not null,
                LastError TEXT null,
                NextAttemptAtUtc TEXT not null,
                CreatedAtUtc TEXT not null,
                UpdatedAtUtc TEXT not null,
                SyncedAtUtc TEXT null
            );

            insert into MesSyncOutboxMessages_old (
                Id,
                Topic,
                Endpoint,
                PayloadJson,
                Status,
                RetryCount,
                LastError,
                NextAttemptAtUtc,
                CreatedAtUtc,
                UpdatedAtUtc,
                SyncedAtUtc
            )
            select
                Id,
                Topic,
                Endpoint,
                PayloadJson,
                Status,
                RetryCount,
                LastError,
                datetime(NextAttemptUnixTimeSeconds, 'unixepoch'),
                datetime(CreatedUnixTimeSeconds, 'unixepoch'),
                datetime(UpdatedUnixTimeSeconds, 'unixepoch'),
                case
                    when SyncedUnixTimeSeconds is null then null
                    else datetime(SyncedUnixTimeSeconds, 'unixepoch')
                end
            from MesSyncOutboxMessages;

            drop table MesSyncOutboxMessages;
            alter table MesSyncOutboxMessages_old rename to MesSyncOutboxMessages;
            """);

        migrationBuilder.Sql("""
            create table ProductionContexts_old (
                Id TEXT not null constraint PK_ProductionContexts primary key,
                MachineCode TEXT not null,
                CommandCode TEXT not null,
                Status TEXT not null,
                ProductionOrderCode TEXT null,
                OperatorCode TEXT null,
                ReasonCode TEXT null,
                Note TEXT null,
                StartedAtUtc TEXT null,
                PausedAtUtc TEXT null,
                StoppedAtUtc TEXT null,
                UpdatedAtUtc TEXT not null
            );

            insert into ProductionContexts_old (
                Id,
                MachineCode,
                CommandCode,
                Status,
                ProductionOrderCode,
                OperatorCode,
                ReasonCode,
                Note,
                StartedAtUtc,
                PausedAtUtc,
                StoppedAtUtc,
                UpdatedAtUtc
            )
            select
                Id,
                MachineCode,
                CommandCode,
                Status,
                ProductionOrderCode,
                OperatorCode,
                ReasonCode,
                Note,
                case
                    when StartedUnixTimeSeconds is null then null
                    else datetime(StartedUnixTimeSeconds, 'unixepoch')
                end,
                case
                    when PausedUnixTimeSeconds is null then null
                    else datetime(PausedUnixTimeSeconds, 'unixepoch')
                end,
                case
                    when StoppedUnixTimeSeconds is null then null
                    else datetime(StoppedUnixTimeSeconds, 'unixepoch')
                end,
                datetime(UpdatedUnixTimeSeconds, 'unixepoch')
            from ProductionContexts;

            drop table ProductionContexts;
            alter table ProductionContexts_old rename to ProductionContexts;
            """);

        migrationBuilder.Sql("""
            create table PlcRawIntervals_old (
                Id INTEGER not null constraint PK_PlcRawIntervals primary key autoincrement,
                MachineCode TEXT not null,
                ReadAtUtc TEXT not null,
                ReadAtUnixMilliseconds INTEGER not null,
                MachineState INTEGER null,
                ShotOkTotal INTEGER null,
                ShotNgTotal INTEGER null,
                CycleTimeMs INTEGER null,
                RunTimeTotal INTEGER null,
                StopTimeTotal INTEGER null,
                ErrorTimeTotal INTEGER null,
                CreatedAtUtc TEXT not null
            );

            insert into PlcRawIntervals_old (
                Id,
                MachineCode,
                ReadAtUtc,
                ReadAtUnixMilliseconds,
                MachineState,
                ShotOkTotal,
                ShotNgTotal,
                CycleTimeMs,
                RunTimeTotal,
                StopTimeTotal,
                ErrorTimeTotal,
                CreatedAtUtc
            )
            select
                Id,
                MachineCode,
                datetime(ReadAtUnixTimeSeconds, 'unixepoch'),
                ReadAtUnixTimeSeconds * 1000,
                MachineState,
                ShotOkTotal,
                ShotNgTotal,
                CycleTimeMs,
                RunTimeTotal,
                StopTimeTotal,
                ErrorTimeTotal,
                datetime(CreatedUnixTimeSeconds, 'unixepoch')
            from PlcRawIntervals;

            drop table PlcRawIntervals;
            alter table PlcRawIntervals_old rename to PlcRawIntervals;
            """);

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
}
