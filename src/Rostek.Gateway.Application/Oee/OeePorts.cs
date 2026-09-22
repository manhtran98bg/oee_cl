using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeLocalRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionContext?> GetActiveProductionContextAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListCapturableProductionContextsAsync(string machine, CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task DeleteProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetLatestRawIntervalAsync(string machine, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> ListRawIntervalsAsync(string machine, long startAt, long endAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionPeriod>> ListProductionPeriodsByMachineOrderAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
    Task UpsertProductionMetricsAsync(IReadOnlyCollection<ProductionMetric> metrics, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionMetric>> ListFinalSessionProductionMetricsAsync(CancellationToken cancellationToken);
    Task<MachineStateEvent?> GetOpenMachineStateEventAsync(string machine, string orderId, string sessionId, CancellationToken cancellationToken);
    Task SaveMachineStateEventsAsync(IReadOnlyCollection<MachineStateEvent> stateEvents, CancellationToken cancellationToken);
    Task UpsertSyncOutboxMessageAsync(SyncOutboxMessage message, CancellationToken cancellationToken);
    Task RemoveStaleRealtimeSnapshotMessagesAsync(IReadOnlyCollection<string> retainedDedupeKeys, CancellationToken cancellationToken);
    Task<IReadOnlyList<SyncOutboxMessage>> TakePendingSyncOutboxMessagesAsync(long now, int batchSize, IReadOnlyCollection<string> topics, CancellationToken cancellationToken);
    Task MarkSyncOutboxMessagesSucceededAsync(IReadOnlyCollection<long> ids, long syncedAt, CancellationToken cancellationToken);
    Task MarkSyncOutboxMessagesFailedAsync(IReadOnlyCollection<long> ids, string error, long nextAttemptAt, CancellationToken cancellationToken);
}
