using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContext(typeof(OeeDbContext))]
[Migration("20261015000100_AddProductionSchedules")]
public sealed class AddProductionSchedules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE production_schedule (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                Target TEXT NOT NULL,
                MachineIdsJson TEXT NOT NULL,
                Kind TEXT NOT NULL,
                OnceAt INTEGER NULL,
                LocalTimeMinutes INTEGER NOT NULL,
                WeekdaysMask INTEGER NOT NULL,
                Enabled INTEGER NOT NULL,
                Deleted INTEGER NOT NULL,
                NextRunAt INTEGER NULL,
                UpdatedAt INTEGER NOT NULL
            );
            CREATE INDEX IX_production_schedule_Enabled_Deleted_NextRunAt ON production_schedule(Enabled, Deleted, NextRunAt);
            CREATE TABLE production_schedule_run (
                Id TEXT NOT NULL PRIMARY KEY,
                ScheduleId TEXT NOT NULL REFERENCES production_schedule(Id) ON DELETE RESTRICT,
                ScheduleName TEXT NOT NULL,
                ScheduledAt INTEGER NOT NULL,
                CreatedAt INTEGER NOT NULL,
                CompletedAt INTEGER NULL,
                Status TEXT NOT NULL,
                Message TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_production_schedule_run_ScheduleId_ScheduledAt ON production_schedule_run(ScheduleId, ScheduledAt);
            CREATE INDEX IX_production_schedule_run_Status ON production_schedule_run(Status);
            CREATE TABLE production_schedule_machine_run (
                Id TEXT NOT NULL PRIMARY KEY,
                RunId TEXT NOT NULL REFERENCES production_schedule_run(Id) ON DELETE RESTRICT,
                MachineId TEXT NOT NULL,
                MachineCode TEXT NOT NULL,
                ScheduledAt INTEGER NOT NULL,
                Status TEXT NOT NULL,
                Attempts INTEGER NOT NULL,
                NextAttemptAt INTEGER NOT NULL,
                ExecutedAt INTEGER NULL,
                TransitionsJson TEXT NOT NULL,
                Message TEXT NOT NULL,
                CoalescedIntoId TEXT NULL
            );
            CREATE UNIQUE INDEX IX_production_schedule_machine_run_RunId_MachineId ON production_schedule_machine_run(RunId, MachineId);
            CREATE INDEX IX_production_schedule_machine_run_Status_NextAttemptAt ON production_schedule_machine_run(Status, NextAttemptAt);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE production_schedule_machine_run; DROP TABLE production_schedule_run; DROP TABLE production_schedule;");
    }
}
