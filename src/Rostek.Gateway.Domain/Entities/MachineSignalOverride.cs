using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class MachineSignalOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }
    public Guid TemplateSignalId { get; set; }
    public TemplateSignal? TemplateSignal { get; set; }
    public string? SourceAddress { get; set; }
    public SignalDataType? DataType { get; set; }
    public int? SamplingIntervalMs { get; set; }
    public double? ScalingFactor { get; set; }
    public double? ScalingOffset { get; set; }
    public bool? Enabled { get; set; }
    public string? ValueMappingJson { get; set; }
    public string? OptionsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
