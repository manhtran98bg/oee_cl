using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rostek.Gateway.Application.MachineTemplates;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Host.Pages.MachineTemplates;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;
using Xunit;

namespace Rostek.Gateway.IntegrationTests;

public sealed class MachineTemplateSignalEditTests
{
    [Fact]
    public async Task GetSignalInput_returns_signal_for_matching_template()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var template = CreateTemplate();
        var signal = CreateSignal(template.Id);
        db.MachineTemplates.Add(template);
        db.TemplateSignals.Add(signal);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var input = await service.GetSignalInputAsync(template.Id, signal.Id, CancellationToken.None);

        Assert.NotNull(input);
        Assert.Equal(signal.Id, input.Id);
        Assert.Equal(template.Id, input.TemplateId);
        Assert.Equal("SHOT_OK_COUNT", input.SignalCode);
        Assert.Equal("Shot OK Count", input.DisplayName);
        Assert.Equal("HR:40001", input.SourceAddress);
        Assert.Equal(SignalDataType.Int32, input.DataType);
        Assert.Equal(SignalAccessMode.Read, input.AccessMode);
        Assert.Equal(1000, input.SamplingIntervalMs);
        Assert.Equal(2, input.ScalingFactor);
        Assert.Equal(3, input.ScalingOffset);
        Assert.True(input.Required);
        Assert.True(input.Enabled);
        Assert.Equal("{\"a\":1}", input.ValueMappingJson);
        Assert.Equal("{\"b\":2}", input.OptionsJson);
        Assert.Equal(4, input.DisplayOrder);
    }

    [Fact]
    public async Task GetSignalInput_returns_null_when_signal_belongs_to_other_template()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var template = CreateTemplate();
        var otherTemplate = CreateTemplate("JSW-MODBUS-2");
        var signal = CreateSignal(otherTemplate.Id);
        db.MachineTemplates.AddRange(template, otherTemplate);
        db.TemplateSignals.Add(signal);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var input = await service.GetSignalInputAsync(template.Id, signal.Id, CancellationToken.None);

        Assert.Null(input);
    }

    [Fact]
    public async Task SaveSignal_with_id_updates_existing_signal_without_creating_new_row()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var template = CreateTemplate();
        var signal = CreateSignal(template.Id);
        db.MachineTemplates.Add(template);
        db.TemplateSignals.Add(signal);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveSignalAsync(
            new TemplateSignalInput
            {
                Id = signal.Id,
                TemplateId = template.Id,
                SignalCode = "SHOT_NG_COUNT",
                DisplayName = "Shot NG Count",
                SourceAddress = "HR:40003",
                DataType = SignalDataType.Int32,
                AccessMode = SignalAccessMode.Read,
                SamplingIntervalMs = 2000,
                ScalingFactor = 1,
                ScalingOffset = 0,
                Required = false,
                Enabled = true,
                DisplayOrder = 9
            },
            null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(1, await db.TemplateSignals.CountAsync());
        var updated = await db.TemplateSignals.SingleAsync();
        Assert.Equal(signal.Id, updated.Id);
        Assert.Equal("SHOT_NG_COUNT", updated.SignalCode);
        Assert.Equal("Shot NG Count", updated.DisplayName);
        Assert.Equal("HR:40003", updated.SourceAddress);
        Assert.Equal(2000, updated.SamplingIntervalMs);
        Assert.Equal(9, updated.DisplayOrder);
    }

    [Fact]
    public async Task SaveSignal_with_duplicate_code_when_editing_is_blocked()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var template = CreateTemplate();
        var first = CreateSignal(template.Id);
        var second = CreateSignal(template.Id, "SHOT_NG_COUNT");
        db.MachineTemplates.Add(template);
        db.TemplateSignals.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var result = await service.SaveSignalAsync(
            new TemplateSignalInput
            {
                Id = second.Id,
                TemplateId = template.Id,
                SignalCode = first.SignalCode,
                DisplayName = "Duplicate",
                SourceAddress = "HR:40009",
                DataType = SignalDataType.Int32,
                Enabled = true
            },
            null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.ErrorMessage);
    }

    [Fact]
    public async Task Signals_page_model_edit_query_loads_signal_input()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var template = CreateTemplate();
        var signal = CreateSignal(template.Id);
        db.MachineTemplates.Add(template);
        db.TemplateSignals.Add(signal);
        await db.SaveChangesAsync();
        var model = new SignalsModel(CreateService(db));

        await model.OnGetAsync(template.Id, signal.Id, CancellationToken.None);

        Assert.True(model.IsEditing);
        Assert.Equal("Edit Signal: SHOT_OK_COUNT", model.FormTitle);
        Assert.Equal(signal.Id, model.Input.Id);
        Assert.Equal(template.Id, model.Input.TemplateId);
        Assert.Equal("HR:40001", model.Input.SourceAddress);
        Assert.Single(model.Signals);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static GatewayDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>().UseSqlite(connection).Options;
        var db = new GatewayDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static MachineTemplateService CreateService(GatewayDbContext db) =>
        new(
            new EfCoreConfigRepository(db),
            Options.Create(new RuntimeOptions { MinimumPollingIntervalMs = 100 }),
            NullLogger<MachineTemplateService>.Instance);

    private static MachineTemplate CreateTemplate(string code = "JSW-MODBUS") =>
        new()
        {
            Code = code,
            Name = code,
            Protocol = GatewayProtocol.ModbusTcp,
            DefaultPollingIntervalMs = 1000
        };

    private static TemplateSignal CreateSignal(Guid templateId, string code = "SHOT_OK_COUNT") =>
        new()
        {
            TemplateId = templateId,
            SignalCode = code,
            DisplayName = "Shot OK Count",
            SourceAddress = "HR:40001",
            DataType = SignalDataType.Int32,
            AccessMode = SignalAccessMode.Read,
            SamplingIntervalMs = 1000,
            ScalingFactor = 2,
            ScalingOffset = 3,
            Required = true,
            Enabled = true,
            ValueMappingJson = "{\"a\":1}",
            OptionsJson = "{\"b\":2}",
            DisplayOrder = 4
        };
}
