using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.UnitTests.Support;

public sealed class InMemoryOeeLocalRepository : IOeeLocalRepository
{
    public List<ProductionContext> Contexts { get; } = [];
    public List<ProductionPeriod> Periods { get; } = [];
    public List<PlcRawInterval> RawIntervals { get; } = [];
    public List<MachineStateEvent> MachineStateEvents { get; } = [];
    public List<SyncOutboxMessage> SyncOutboxMessages { get; } = [];
    public List<ProductionMetric> ProductionMetrics { get; } = [];

    public Task<ProductionContext?> GetProductionContextAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Contexts.FirstOrDefault(context => context.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase)));

    public Task<ProductionContext?> GetActiveProductionContextAsync(string machine, string orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Contexts
            .Where(context =>
                context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                context.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) &&
                context.Status is "active" or "pause")
            .OrderByDescending(context => context.ActivePeriodStartAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionContext>>(Contexts
            .Where(context => context.Status is "active" or "pause")
            .OrderBy(context => context.Machine, StringComparer.OrdinalIgnoreCase)
            .ThenBy(context => context.OrderId, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public Task<IReadOnlyList<ProductionContext>> ListCapturableProductionContextsAsync(string machine, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionContext>>(Contexts
            .Where(context =>
                context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                context.Status is "active" or "pause")
            .OrderBy(context => context.OrderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(context => context.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken)
    {
        var context = Contexts.FirstOrDefault(item =>
            item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
            item.OrderId == OeeTestProductionContext.OrderId &&
            item.Status is "active" or "pause");
        if (context is not null)
        {
            return Task.FromResult(context);
        }

        context = new ProductionContext
        {
            SessionId = $"{machine}-{OeeTestProductionContext.PeriodId}",
            Machine = machine,
            Status = "active",
            OrderId = OeeTestProductionContext.OrderId,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ActivePeriodStartAt = startAt,
            CurrentPlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            ProductsJson = OeeTestProductionContext.ProductsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson,
            UpdatedAt = startAt
        };
        Contexts.Add(context);
        return Task.FromResult(context);
    }

    public Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken)
    {
        Contexts.RemoveAll(item => item.SessionId.Equals(context.SessionId, StringComparison.OrdinalIgnoreCase));
        Contexts.Add(context);
        return Task.CompletedTask;
    }

    public Task DeleteProductionContextAsync(string sessionId, CancellationToken cancellationToken)
    {
        Contexts.RemoveAll(item => item.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    public Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken) =>
        Task.FromResult(Periods.FirstOrDefault(period => period.PeriodId == periodId));

    public Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
    {
        var period = Periods.FirstOrDefault(item => item.PeriodId == context.SessionId);
        if (period is not null)
        {
            return Task.FromResult(period);
        }

        period = new ProductionPeriod
        {
            PeriodId = context.SessionId,
            Machine = context.Machine,
            PlcPeriodIndex = context.CurrentPlcPeriodIndex,
            OrderId = context.OrderId,
            ServerOrderId = context.ServerOrderId,
            ProductsJson = context.ProductsJson,
            ExtraJson = context.ExtraJson,
            StartAt = startAt,
            Status = context.Status,
            CreatedAt = startAt,
            UpdatedAt = startAt
        };
        Periods.Add(period);
        return Task.FromResult(period);
    }

    public Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderId, CancellationToken cancellationToken)
    {
        var currentMax = Periods
            .Where(period => period.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) && period.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase))
            .Select(period => (int?)period.PlcPeriodIndex)
            .DefaultIfEmpty()
            .Max();
        return Task.FromResult((currentMax ?? 0) + 1);
    }

    public Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken)
    {
        var period = Periods.FirstOrDefault(item => item.PeriodId == periodId);
        if (period is not null)
        {
            period.EndAt = endAt;
            period.Status = status;
            period.UpdatedAt = endAt;
        }

        return Task.CompletedTask;
    }

    public Task<PlcRawInterval?> GetLatestRawIntervalAsync(string machine, CancellationToken cancellationToken) =>
        Task.FromResult(RawIntervals
            .Where(item => item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.ReadAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<PlcRawInterval>> ListRawIntervalsAsync(string machine, long startAt, long endAt, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlcRawInterval>>(RawIntervals
            .Where(item =>
                item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                item.ReadAt >= startAt &&
                item.ReadAt <= endAt)
            .OrderBy(item => item.ReadAt)
            .ToList());

    public Task<IReadOnlyList<ProductionPeriod>> ListProductionPeriodsByMachineOrderAsync(string machine, string orderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionPeriod>>(Periods
            .Where(item =>
                item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                item.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StartAt)
            .ToList());

    public Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        var inserted = rawIntervals
            .Where(raw => !RawIntervals.Any(existing => existing.Machine.Equals(raw.Machine, StringComparison.OrdinalIgnoreCase) && existing.ReadAt == raw.ReadAt))
            .ToList();
        RawIntervals.AddRange(inserted);
        return Task.FromResult<IReadOnlyList<PlcRawInterval>>(inserted);
    }

    public Task UpsertProductionMetricsAsync(IReadOnlyCollection<ProductionMetric> metrics, CancellationToken cancellationToken)
    {
        foreach (var metric in metrics)
        {
            ProductionMetrics.RemoveAll(item => item.MetricId == metric.MetricId);
            ProductionMetrics.Add(metric);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProductionMetric>> ListFinalSessionProductionMetricsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionMetric>>(ProductionMetrics
            .Where(metric =>
                metric.BucketType.Equals("session", StringComparison.OrdinalIgnoreCase) &&
                metric.IsFinal &&
                !string.IsNullOrWhiteSpace(metric.SessionId))
            .OrderBy(metric => metric.Machine, StringComparer.OrdinalIgnoreCase)
            .ThenBy(metric => metric.OrderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(metric => metric.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToList());

    public Task<MachineStateEvent?> GetOpenMachineStateEventAsync(
        string machine,
        string orderId,
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(MachineStateEvents
            .Where(item =>
                item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                item.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) &&
                item.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase) &&
                item.IsOpen)
            .OrderByDescending(item => item.StartAt)
            .FirstOrDefault());

    public Task SaveMachineStateEventsAsync(IReadOnlyCollection<MachineStateEvent> stateEvents, CancellationToken cancellationToken)
    {
        foreach (var stateEvent in stateEvents)
        {
            MachineStateEvents.RemoveAll(item => item.EventId == stateEvent.EventId);
            MachineStateEvents.Add(stateEvent);
        }

        return Task.CompletedTask;
    }

    public Task UpsertSyncOutboxMessageAsync(SyncOutboxMessage message, CancellationToken cancellationToken)
    {
        var existing = SyncOutboxMessages.FirstOrDefault(item => item.DedupeKey == message.DedupeKey);
        if (existing is null)
        {
            message.Id = message.Id == 0 ? SyncOutboxMessages.Count + 1 : message.Id;
            SyncOutboxMessages.Add(message);
        }
        else
        {
            existing.Topic = message.Topic;
            existing.EndpointPath = message.EndpointPath;
            existing.PayloadJson = message.PayloadJson;
            existing.Status = SyncOutboxStatuses.Pending;
            existing.NextAttemptAt = message.NextAttemptAt;
            existing.LastError = null;
            existing.UpdatedAt = message.UpdatedAt;
            existing.SyncedAt = null;
        }

        return Task.CompletedTask;
    }

    public Task RemoveStaleRealtimeSnapshotMessagesAsync(
        IReadOnlyCollection<string> retainedDedupeKeys,
        CancellationToken cancellationToken)
    {
        var retained = retainedDedupeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        SyncOutboxMessages.RemoveAll(message =>
            message.Topic == SyncOutboxTopics.RealtimeSnapshot &&
            message.Status is SyncOutboxStatuses.Pending or SyncOutboxStatuses.Failed &&
            !retained.Contains(message.DedupeKey));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncOutboxMessage>> TakePendingSyncOutboxMessagesAsync(
        long now,
        int batchSize,
        IReadOnlyCollection<string> topics,
        CancellationToken cancellationToken)
    {
        var topicSet = topics.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messages = SyncOutboxMessages
            .Where(item =>
                item.NextAttemptAt <= now &&
                item.Status is SyncOutboxStatuses.Pending or SyncOutboxStatuses.Failed &&
                (topicSet.Count == 0 || topicSet.Contains(item.Topic)))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(Math.Max(1, batchSize))
            .ToList();
        return Task.FromResult<IReadOnlyList<SyncOutboxMessage>>(messages);
    }

    public Task MarkSyncOutboxMessagesSucceededAsync(IReadOnlyCollection<long> ids, long syncedAt, CancellationToken cancellationToken)
    {
        foreach (var message in SyncOutboxMessages.Where(item => ids.Contains(item.Id)))
        {
            message.Status = SyncOutboxStatuses.Synced;
            message.LastError = null;
            message.SyncedAt = syncedAt;
            message.UpdatedAt = syncedAt;
        }

        return Task.CompletedTask;
    }

    public Task MarkSyncOutboxMessagesFailedAsync(IReadOnlyCollection<long> ids, string error, long nextAttemptAt, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var message in SyncOutboxMessages.Where(item => ids.Contains(item.Id)))
        {
            message.Status = SyncOutboxStatuses.Failed;
            message.AttemptCount += 1;
            message.LastError = error;
            message.NextAttemptAt = nextAttemptAt;
            message.UpdatedAt = now;
        }

        return Task.CompletedTask;
    }
}
