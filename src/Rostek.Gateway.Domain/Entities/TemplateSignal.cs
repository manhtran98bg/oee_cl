using Rostek.Gateway.Domain.Enums;

namespace Rostek.Gateway.Domain.Entities;

public sealed class TemplateSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TemplateId { get; set; }
    public MachineTemplate? Template { get; set; }
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
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
