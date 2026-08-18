namespace Rostek.Gateway.Application.History;

public interface IDeviceHistorySampler
{
    Task<int> SampleAndWriteAsync(string gatewayId, CancellationToken cancellationToken);
}
