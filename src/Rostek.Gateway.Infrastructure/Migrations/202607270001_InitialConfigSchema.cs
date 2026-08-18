using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202607270001_InitialConfigSchema")]
public partial class InitialConfigSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                UserName = table.Column<string>(type: "TEXT", nullable: true),
                Action = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "TEXT", nullable: true),
                OldValueJson = table.Column<string>(type: "TEXT", nullable: true),
                NewValueJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                CorrelationId = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ConfigurationVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Version = table.Column<long>(type: "INTEGER", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                Checksum = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                AppliedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ConfigurationVersions", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MachineGroups",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MachineGroups", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MachineTemplates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Manufacturer = table.Column<string>(type: "TEXT", nullable: true),
                Model = table.Column<string>(type: "TEXT", nullable: true),
                DefaultPollingIntervalMs = table.Column<int>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MachineTemplates", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Machines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                Description = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Machines", x => x.Id);
                table.ForeignKey("FK_Machines_MachineGroups_GroupId", x => x.GroupId, "MachineGroups", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Machines_MachineTemplates_TemplateId", x => x.TemplateId, "MachineTemplates", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TemplateSignals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                SignalCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                SourceAddress = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                DataType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                AccessMode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                SamplingIntervalMs = table.Column<int>(type: "INTEGER", nullable: true),
                ScalingFactor = table.Column<double>(type: "REAL", nullable: false),
                ScalingOffset = table.Column<double>(type: "REAL", nullable: false),
                Required = table.Column<bool>(type: "INTEGER", nullable: false),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                ValueMappingJson = table.Column<string>(type: "TEXT", nullable: true),
                OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TemplateSignals", x => x.Id);
                table.ForeignKey("FK_TemplateSignals_MachineTemplates_TemplateId", x => x.TemplateId, "MachineTemplates", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MachineConnections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                Host = table.Column<string>(type: "TEXT", nullable: true),
                Port = table.Column<int>(type: "INTEGER", nullable: true),
                EndpointUrl = table.Column<string>(type: "TEXT", nullable: true),
                UnitId = table.Column<int>(type: "INTEGER", nullable: true),
                SecurityMode = table.Column<string>(type: "TEXT", nullable: true),
                SecurityPolicy = table.Column<string>(type: "TEXT", nullable: true),
                AuthenticationMode = table.Column<string>(type: "TEXT", nullable: true),
                CredentialReference = table.Column<string>(type: "TEXT", nullable: true),
                ConnectTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                RequestTimeoutMs = table.Column<int>(type: "INTEGER", nullable: false),
                RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                PollingIntervalMs = table.Column<int>(type: "INTEGER", nullable: true),
                OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MachineConnections", x => x.Id);
                table.ForeignKey("FK_MachineConnections_Machines_MachineId", x => x.MachineId, "Machines", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MachineSignalOverrides",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MachineId = table.Column<Guid>(type: "TEXT", nullable: false),
                TemplateSignalId = table.Column<Guid>(type: "TEXT", nullable: false),
                SourceAddress = table.Column<string>(type: "TEXT", nullable: true),
                DataType = table.Column<string>(type: "TEXT", nullable: true),
                SamplingIntervalMs = table.Column<int>(type: "INTEGER", nullable: true),
                ScalingFactor = table.Column<double>(type: "REAL", nullable: true),
                ScalingOffset = table.Column<double>(type: "REAL", nullable: true),
                Enabled = table.Column<bool>(type: "INTEGER", nullable: true),
                ValueMappingJson = table.Column<string>(type: "TEXT", nullable: true),
                OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MachineSignalOverrides", x => x.Id);
                table.ForeignKey("FK_MachineSignalOverrides_Machines_MachineId", x => x.MachineId, "Machines", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_MachineSignalOverrides_TemplateSignals_TemplateSignalId", x => x.TemplateSignalId, "TemplateSignals", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_AuditLogs_CreatedAtUtc", "AuditLogs", "CreatedAtUtc");
        migrationBuilder.CreateIndex("IX_ConfigurationVersions_Version", "ConfigurationVersions", "Version", unique: true);
        migrationBuilder.CreateIndex("IX_MachineConnections_MachineId", "MachineConnections", "MachineId", unique: true);
        migrationBuilder.CreateIndex("IX_MachineGroups_Code", "MachineGroups", "Code", unique: true);
        migrationBuilder.CreateIndex("IX_Machines_Code", "Machines", "Code", unique: true);
        migrationBuilder.CreateIndex("IX_Machines_GroupId", "Machines", "GroupId");
        migrationBuilder.CreateIndex("IX_Machines_TemplateId", "Machines", "TemplateId");
        migrationBuilder.CreateIndex("IX_MachineSignalOverrides_MachineId", "MachineSignalOverrides", "MachineId");
        migrationBuilder.CreateIndex("IX_MachineSignalOverrides_MachineId_TemplateSignalId", "MachineSignalOverrides", new[] { "MachineId", "TemplateSignalId" }, unique: true);
        migrationBuilder.CreateIndex("IX_MachineSignalOverrides_TemplateSignalId", "MachineSignalOverrides", "TemplateSignalId");
        migrationBuilder.CreateIndex("IX_MachineTemplates_Code", "MachineTemplates", "Code", unique: true);
        migrationBuilder.CreateIndex("IX_TemplateSignals_TemplateId", "TemplateSignals", "TemplateId");
        migrationBuilder.CreateIndex("IX_TemplateSignals_TemplateId_SignalCode", "TemplateSignals", new[] { "TemplateId", "SignalCode" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("ConfigurationVersions");
        migrationBuilder.DropTable("MachineConnections");
        migrationBuilder.DropTable("MachineSignalOverrides");
        migrationBuilder.DropTable("Machines");
        migrationBuilder.DropTable("TemplateSignals");
        migrationBuilder.DropTable("MachineGroups");
        migrationBuilder.DropTable("MachineTemplates");
    }
}
