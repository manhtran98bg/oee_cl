using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeLocalRepository
{
    // In-memory adapters execute directly; persistent adapters must provide atomicity and deferred cache updates.
    Task<T> ExecuteTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => action(cancellationToken);
    void AfterCommit(Action action) => action();

    Task<ProductionContext?> GetProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionContext?> GetActiveProductionContextAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListCapturableProductionContextsAsync(string machine, CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task DeleteProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionQuantitySourceEvent?> GetProductionQuantitySourceEventAsync(string eventCode, CancellationToken cancellationToken);
    Task SaveProductionQuantitySourceEventAsync(ProductionQuantitySourceEvent sourceEvent, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken);
    Task<PlcRawInterval?> GetLatestRawIntervalAsync(string machine, CancellationToken cancellationToken);
    async Task<IReadOnlyDictionary<string, PlcRawInterval>> GetLatestRawIntervalsAsync(
        IReadOnlyCollection<string> machines,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PlcRawInterval>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in machines.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var raw = await GetLatestRawIntervalAsync(machine, cancellationToken);
            if (raw is not null)
            {
                result[machine] = raw;
            }
        }

        return result;
    }
    Task<IReadOnlyList<PlcRawInterval>> ListRawIntervalsAsync(string machine, long startAt, long endAt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionPeriod>> ListProductionPeriodsByMachineOrderAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
    Task UpsertProductionMetricsAsync(IReadOnlyCollection<ProductionMetric> metrics, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionMetric>> ListFinalSessionProductionMetricsAsync(CancellationToken cancellationToken);
    Task RemoveOpenProductionMetricsAsync(string machine, string orderId, string sessionId, CancellationToken cancellationToken);
    Task<MachineStateEvent?> GetOpenMachineStateEventAsync(string machine, string orderId, string sessionId, CancellationToken cancellationToken);
    async Task<IReadOnlyDictionary<string, MachineStateEvent>> GetOpenMachineStateEventsAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, MachineStateEvent>(StringComparer.OrdinalIgnoreCase);
        foreach (var sessionId in sessionIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var contexts = await ListProductionContextsAsync(cancellationToken);
            var context = contexts.FirstOrDefault(item => item.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
            if (context is null)
            {
                continue;
            }

            var stateEvent = await GetOpenMachineStateEventAsync(context.Machine, context.OrderId, sessionId, cancellationToken);
            if (stateEvent is not null)
            {
                result[sessionId] = stateEvent;
            }
        }

        return result;
    }
    Task SaveMachineStateEventsAsync(IReadOnlyCollection<MachineStateEvent> stateEvents, CancellationToken cancellationToken);
    Task UpsertSyncOutboxMessageAsync(SyncOutboxMessage message, CancellationToken cancellationToken);
    async Task UpsertSyncOutboxMessagesAsync(IReadOnlyCollection<SyncOutboxMessage> messages, CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await UpsertSyncOutboxMessageAsync(message, cancellationToken);
        }
    }
    Task RemoveStaleRealtimeSnapshotMessagesAsync(IReadOnlyCollection<string> retainedDedupeKeys, CancellationToken cancellationToken);
    Task RemovePendingQuantityOutboxMessagesAsync(string machine, string orderId, string sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SyncOutboxMessage>> TakePendingSyncOutboxMessagesAsync(long now, int batchSize, IReadOnlyCollection<string> topics, CancellationToken cancellationToken);
    Task MarkSyncOutboxMessagesSucceededAsync(IReadOnlyCollection<SyncOutboxMessage> dispatchedMessages, long syncedAt, CancellationToken cancellationToken);
    Task MarkSyncOutboxMessagesFailedAsync(IReadOnlyCollection<SyncOutboxMessage> dispatchedMessages, string error, long nextAttemptAt, CancellationToken cancellationToken);
}
