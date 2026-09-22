using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Rostek.Gateway.Infrastructure.Persistence;

#nullable disable

namespace Rostek.Gateway.Infrastructure.Migrations;

[DbContextAttribute(typeof(GatewayDbContext))]
[Migration("202609220002_AddNet100BasicAuthentication")]
public sealed class AddNet100BasicAuthentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Net100AuthenticationMode",
            table: "MachineTemplates",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "NONE");

        migrationBuilder.AddColumn<string>(
            name: "Net100CredentialReference",
            table: "MachineTemplates",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Net100AuthenticationMode", table: "MachineTemplates");
        migrationBuilder.DropColumn(name: "Net100CredentialReference", table: "MachineTemplates");
    }
}
