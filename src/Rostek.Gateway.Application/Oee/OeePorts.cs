using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeLocalRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureTestProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetPreviousRawInPeriodAsync(string machine, int plcPeriodIndex, long periodStartAt, long beforeReadAt, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetFirstRawInRangeAsync(string machine, int plcPeriodIndex, long startAt, long beforeReadAt, CancellationToken cancellationToken);
    Task<ProductionMetric?> GetProductionMetricAsync(string metricType, string periodId, string productId, string runState, long startAt, CancellationToken cancellationToken);
    Task<ProductionMetric?> GetLatestStateMetricAsync(string machine, string periodId, string productId, string runState, long beforeStartAt, CancellationToken cancellationToken);
    Task UpsertProductionMetricAsync(ProductionMetric metric, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionMetric>> ListPeriodMetricsAsync(string orderId, string productId, CancellationToken cancellationToken);
    Task UpsertProductMetricAsync(ProductMetric metric, CancellationToken cancellationToken);
    Task<DowntimeEvent?> GetOpenDowntimeEventAsync(string machine, string periodId, CancellationToken cancellationToken);
    Task UpsertDowntimeEventAsync(DowntimeEvent downtimeEvent, CancellationToken cancellationToken);
    Task EnqueueOutboxAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken);
    Task<(int PendingCount, int FailedCount, long? LastSuccess, string? LastError)> GetOutboxStatusAsync(CancellationToken cancellationToken);
}
