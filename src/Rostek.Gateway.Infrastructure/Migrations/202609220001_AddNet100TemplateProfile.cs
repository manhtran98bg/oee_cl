using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609220001_AddNet100TemplateProfile")]
public sealed class AddNet100TemplateProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Net100ServerHost",
            table: "MachineTemplates",
            type: "TEXT",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Net100ServerPort",
            table: "MachineTemplates",
            type: "INTEGER",
            nullable: false,
            defaultValue: 80);

        migrationBuilder.AddColumn<string>(
            name: "Net100BasePath",
            table: "MachineTemplates",
            type: "TEXT",
            maxLength: 255,
            nullable: false,
            defaultValue: "/net100");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Net100ServerHost", table: "MachineTemplates");
        migrationBuilder.DropColumn(name: "Net100ServerPort", table: "MachineTemplates");
        migrationBuilder.DropColumn(name: "Net100BasePath", table: "MachineTemplates");
    }
}
