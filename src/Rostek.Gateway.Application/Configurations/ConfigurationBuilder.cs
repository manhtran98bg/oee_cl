using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.Configurations;

public sealed class ConfigurationBuilder(IConfigRepository repository) : IConfigurationBuilder
{
    public async Task<RuntimeConfiguration> BuildDraftAsync(CancellationToken cancellationToken)
    {
        var machines = await repository.ListMachinesAsync(new MachineQuery(null, null, null, null, null, 1, int.MaxValue), cancellationToken);
        var effective = new Dictionary<string, EffectiveMachineConfiguration>(StringComparer.OrdinalIgnoreCase);

        foreach (var machine in machines.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (machine.Template is null || machine.Connection is null)
            {
                continue;
            }

            effective[machine.Code] = BuildMachine(machine);
        }

        return new RuntimeConfiguration(0, DateTimeOffset.UtcNow, effective);
    }

    public async Task<RuntimeConfiguration> BuildVersionAsync(long version, CancellationToken cancellationToken)
    {
        var entity = await repository.GetVersionAsync(version, cancellationToken)
            ?? throw new InvalidOperationException($"Configuration version {version} not found.");

        return ConfigurationJson.Deserialize(entity.SnapshotJson);
    }

    private static EffectiveMachineConfiguration BuildMachine(Machine machine)
    {
        var template = machine.Template ?? throw new InvalidOperationException("Machine template is required.");
        var connection = machine.Connection ?? throw new InvalidOperationException("Machine connection is required.");
        var overrides = machine.SignalOverrides.ToDictionary(item => item.TemplateSignalId);
        var signals = template.Signals
            .OrderBy(signal => signal.DisplayOrder)
            .ThenBy(signal => signal.SignalCode)
            .Select(signal => BuildSignal(signal, overrides.GetValueOrDefault(signal.Id)))
            .ToList();

        var endpointUrl = template.Protocol == GatewayProtocol.Net100Http
            ? BuildNet100Endpoint(template)
            : connection.EndpointUrl;

        return new EffectiveMachineConfiguration(
            machine.Id,
            machine.Code,
            machine.Name,
            ProtocolText(template.Protocol),
            machine.Enabled,
            new EffectiveConnectionConfiguration(
                ProtocolText(template.Protocol),
                connection.Host,
                template.Protocol == GatewayProtocol.Net100Http ? template.Net100ServerPort : connection.Port,
                endpointUrl,
                connection.UnitId,
                connection.SecurityMode,
                connection.SecurityPolicy,
                template.Protocol == GatewayProtocol.Net100Http ? template.Net100AuthenticationMode : connection.AuthenticationMode,
                template.Protocol == GatewayProtocol.Net100Http ? template.Net100CredentialReference : connection.CredentialReference,
                connection.ConnectTimeoutMs,
                connection.RequestTimeoutMs,
                connection.RetryCount,
                connection.PollingIntervalMs ?? template.DefaultPollingIntervalMs,
                ConfigurationJson.ParseObjectOptions(connection.OptionsJson)),
            signals)
        {
            TemplateId = template.Id
        };
    }

    private static EffectiveSignalConfiguration BuildSignal(TemplateSignal signal, MachineSignalOverride? signalOverride) =>
        new(
            signal.SignalCode,
            signalOverride?.SourceAddress ?? signal.SourceAddress,
            (signalOverride?.DataType ?? signal.DataType).ToString().ToUpperInvariant(),
            signalOverride?.SamplingIntervalMs ?? signal.SamplingIntervalMs,
            signalOverride?.ScalingFactor ?? signal.ScalingFactor,
            signalOverride?.ScalingOffset ?? signal.ScalingOffset,
            signal.Required,
            signalOverride?.Enabled ?? signal.Enabled,
            ConfigurationJson.ParseStringMap(signalOverride?.ValueMappingJson ?? signal.ValueMappingJson),
            ConfigurationJson.ParseObjectOptions(signalOverride?.OptionsJson ?? signal.OptionsJson));

    private static string? BuildNet100Endpoint(MachineTemplate template) =>
        string.IsNullOrWhiteSpace(template.Net100ServerHost)
            ? null
            : Net100Configuration.BuildBaseUrl(
                template.Net100ServerHost,
                template.Net100ServerPort,
                template.Net100BasePath);

    private static string ProtocolText(GatewayProtocol protocol) => protocol switch
    {
        GatewayProtocol.ModbusTcp => "MODBUS_TCP",
        GatewayProtocol.Net100Http => Net100Configuration.Protocol,
        _ => "OPCUA"
    };
}
