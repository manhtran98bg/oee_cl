using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.Machines;

public interface IMachineService
{
    Task<MachineListResult> ListAsync(MachineQuery query, CancellationToken cancellationToken);
    Task<MachineEditInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineEditInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> DeleteAsync(Guid id, string? userName, CancellationToken cancellationToken);
    Task<RuntimeConfiguration?> PreviewEffectiveAsync(Guid machineId, CancellationToken cancellationToken);
}

public interface ISignalOverrideService
{
    Task<GatewayResult> SaveAsync(SignalOverrideInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> ResetAsync(Guid machineId, Guid templateSignalId, string? userName, CancellationToken cancellationToken);
}

public sealed class MachineEditInput
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Manufacturer { get; set; }
    public string? Location { get; set; }
    public Guid? GroupId { get; set; }
    public Guid TemplateId { get; set; }
    public bool Enabled { get; set; } = true;
    public string OeeTimeSource { get; set; } = OeeTimeSources.DeviceCounters;
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }
    public OpcUaConnectionInput OpcUa { get; set; } = new();
    public ModbusTcpConnectionInput ModbusTcp { get; set; } = new();
}

public sealed class OpcUaConnectionInput
{
    public string? EndpointUrl { get; set; }
    public string? SecurityMode { get; set; } = "NONE";
    public string? SecurityPolicy { get; set; } = "None";
    public string? AuthenticationMode { get; set; } = "ANONYMOUS";
    public string? CredentialReference { get; set; }
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 3000;
}

public sealed class ModbusTcpConnectionInput
{
    public string? Host { get; set; }
    public int? Port { get; set; } = 502;
    public int? UnitId { get; set; }
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 3000;
    public int RetryCount { get; set; } = 3;
    public int? PollingIntervalMs { get; set; }
    public string? OptionsJson { get; set; }
}

public sealed record MachineListItem(
    Guid Id,
    string Code,
    string Name,
    string? Model,
    string? Serial,
    string? Manufacturer,
    string? Location,
    string? GroupCode,
    string TemplateCode,
    GatewayProtocol Protocol,
    string Endpoint,
    bool Enabled);

public sealed record MachineListResult(IReadOnlyList<MachineListItem> Items, int Page, int PageSize);

public sealed class SignalOverrideInput
{
    public Guid MachineId { get; set; }
    public Guid TemplateSignalId { get; set; }
    public string? SourceAddress { get; set; }
    public SignalDataType? DataType { get; set; }
    public int? SamplingIntervalMs { get; set; }
    public double? ScalingFactor { get; set; }
    public double? ScalingOffset { get; set; }
    public bool? Enabled { get; set; }
    public string? ValueMappingJson { get; set; }
    public string? OptionsJson { get; set; }
}
