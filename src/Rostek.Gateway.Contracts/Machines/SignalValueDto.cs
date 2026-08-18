namespace Rostek.Gateway.Contracts.Machines;

public sealed record SignalValueDto(
    string SignalCode,
    object? Value,
    string DataType,
    MachineReadQuality Quality,
    DateTimeOffset TimestampUtc,
    string? Error);
