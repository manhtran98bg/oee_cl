using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Persistence;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Migration_creates_config_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);

        await db.Database.EnsureCreatedAsync();

        db.MachineGroups.Add(new MachineGroup { Code = "MOLDING-A", Name = "Molding A" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.MachineGroups.CountAsync());
    }

    [Fact]
    public async Task Unique_template_signal_code_is_enforced_per_template()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        var template = new MachineTemplate { Code = "SUMITOMO-OPCUA", Name = "Sumitomo", Protocol = GatewayProtocol.OpcUa };
        template.Signals.Add(new TemplateSignal { SignalCode = "machine_state", DisplayName = "State", SourceAddress = "ns=2;s=State", DataType = SignalDataType.Int16 });
        template.Signals.Add(new TemplateSignal { SignalCode = "machine_state", DisplayName = "State 2", SourceAddress = "ns=2;s=State2", DataType = SignalDataType.Int16 });
        db.MachineTemplates.Add(template);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static GatewayDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        return new GatewayDbContext(options);
    }
}
