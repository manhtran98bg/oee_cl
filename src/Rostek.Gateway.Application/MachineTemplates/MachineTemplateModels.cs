using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Application.MachineTemplates;

public interface IMachineTemplateService
{
    Task<IReadOnlyList<MachineTemplateListItem>> ListAsync(CancellationToken cancellationToken);
    Task<MachineTemplateInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TemplateSignalListItem>> ListSignalsAsync(Guid templateId, CancellationToken cancellationToken);
    Task<TemplateSignalInput?> GetSignalInputAsync(Guid templateId, Guid signalId, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(MachineTemplateInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> CloneAsync(Guid id, string newCode, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveSignalAsync(TemplateSignalInput input, string? userName, CancellationToken cancellationToken);
    Task<GatewayResult> DeleteSignalAsync(Guid id, string? userName, CancellationToken cancellationToken);
}

public sealed class MachineTemplateInput
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public GatewayProtocol Protocol { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public int DefaultPollingIntervalMs { get; set; } = 1000;
    public string? Net100ServerHost { get; set; }
    public int Net100ServerPort { get; set; } = 80;
    public string Net100BasePath { get; set; } = "/net100";
    public string Net100AuthenticationMode { get; set; } = "NONE";
    public string? Net100CredentialReference { get; set; }
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
