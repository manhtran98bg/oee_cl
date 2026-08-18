using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.MachineTemplates;

public sealed class MachineTemplateInput
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GatewayProtocol Protocol { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public int DefaultPollingIntervalMs { get; set; } = 1000;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class TemplateSignalInput
{
    public Guid? Id { get; set; }
    public Guid TemplateId { get; set; }
    public string SignalCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourceAddress { get; set; } = string.Empty;
    public SignalDataType DataType { get; set; }
    public SignalAccessMode AccessMode { get; set; } = SignalAccessMode.Read;
    public int? SamplingIntervalMs { get; set; }
    public double ScalingFactor { get; set; } = 1;
    public double ScalingOffset { get; set; }
    public bool Required { get; set; }
    public bool Enabled { get; set; } = true;
    public string? ValueMappingJson { get; set; }
    public string? OptionsJson { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed record MachineTemplateListItem(Guid Id, string Code, string Name, GatewayProtocol Protocol, int SignalCount, bool Enabled);
public sealed record TemplateSignalListItem(Guid Id, string SignalCode, string DisplayName, string SourceAddress, SignalDataType DataType, bool Required, bool Enabled);
