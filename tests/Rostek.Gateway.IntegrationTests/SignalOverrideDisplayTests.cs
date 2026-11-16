using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class SignalOverrideDisplayTests
{
    [Fact]
    public async Task ListAsync_returns_current_override_address_and_template_fallback()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        await using var db = new GatewayDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var template = new MachineTemplate
        {
            Code = "MODBUS-TEMPLATE",
            Name = "Modbus Template",
            Protocol = GatewayProtocol.ModbusTcp
        };
        var overriddenSignal = new TemplateSignal
        {
            TemplateId = template.Id,
            SignalCode = "SHOT_OK_COUNT",
            DisplayName = "Shot OK Count",
            SourceAddress = "HR:40001",
            DataType = SignalDataType.Int16,
            Enabled = true,
            DisplayOrder = 1
        };
        var templateSignal = new TemplateSignal
        {
            TemplateId = template.Id,
            SignalCode = "MACHINE_STATE",
            DisplayName = "Machine State",
            SourceAddress = "HR:40002",
            DataType = SignalDataType.Int16,
            Enabled = true,
            DisplayOrder = 2
        };
        var machine = new Machine
        {
            Code = "MACHINE-01",
            Name = "Machine 01",
            TemplateId = template.Id
        };

        db.MachineTemplates.Add(template);
        db.TemplateSignals.AddRange(overriddenSignal, templateSignal);
        db.Machines.Add(machine);
        db.MachineSignalOverrides.Add(new MachineSignalOverride
        {
            MachineId = machine.Id,
            TemplateSignalId = overriddenSignal.Id,
            SourceAddress = "HR:41001",
            DataType = SignalDataType.Int32,
            Enabled = false
        });
        await db.SaveChangesAsync();

        var service = new SignalOverrideService(
            new EfCoreConfigRepository(db),
            NullLogger<SignalOverrideService>.Instance);

        var signals = await service.ListAsync(machine.Id, CancellationToken.None);

        Assert.Collection(
            signals,
            signal =>
            {
                Assert.Equal("SHOT_OK_COUNT", signal.SignalCode);
                Assert.Equal("HR:41001", signal.CurrentSourceAddress);
                Assert.Equal(SignalDataType.Int32, signal.DataType);
                Assert.False(signal.Enabled);
            },
            signal =>
            {
                Assert.Equal("MACHINE_STATE", signal.SignalCode);
                Assert.Equal("HR:40002", signal.CurrentSourceAddress);
                Assert.Equal(SignalDataType.Int16, signal.DataType);
                Assert.True(signal.Enabled);
            });
    }
}
