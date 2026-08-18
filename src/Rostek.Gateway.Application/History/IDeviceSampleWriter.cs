namespace Rostek.Gateway.Application.History;

public interface IDeviceSampleWriter
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken);

    Task WriteAsync(IReadOnlyCollection<DeviceSample> samples, CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}
