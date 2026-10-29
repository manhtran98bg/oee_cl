using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Oee;

public sealed class EfCoreOeeLocalRepository(OeeDbContext dbContext) : IOeeLocalRepository
{
    private List<Action>? _afterCommit;

    public async Task<T> ExecuteTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        if (_afterCommit is not null)
        {
            return await action(cancellationToken);
        }

        T result;
        var callbacks = new List<Action>();
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            _afterCommit = callbacks;
            try
            {
                result = await action(cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                _afterCommit = null;
            }
        }

        foreach (var callback in callbacks)
        {
            callback();
        }

        return result;
    }

    public void AfterCommit(Action action)
    {
        if (_afterCommit is null) action();
        else _afterCommit.Add(action);
    }

    public Task<ProductionContext?> GetProductionContextAsync(string sessionId, CancellationToken cancellationToken) =>
        dbContext.ProductionContexts.FirstOrDefaultAsync(
            context => context.SessionId == sessionId,
            cancellationToken);

    public Task<ProductionContext?> GetActiveProductionContextAsync(string machine, string orderId, CancellationToken cancellationToken) =>
        dbContext.ProductionContexts
            .Where(context =>
                context.Machine.ToUpper() == machine.ToUpper() &&
                context.OrderId.ToUpper() == orderId.ToUpper() &&
                (context.Status == "active" || context.Status == "pause"))
            .OrderByDescending(context => context.ActivePeriodStartAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
            .Where(context => context.Status == "active" || context.Status == "pause")
            .OrderBy(context => context.Machine)
            .ThenBy(context => context.OrderId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductionContext>> ListCapturableProductionContextsAsync(string machine, CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
            .Where(context =>
                context.Machine.ToUpper() == machine.ToUpper() &&
                (context.Status == "active" || context.Status == "pause"))
            .OrderBy(context => context.OrderId)
            .ThenBy(context => context.SessionId)
            .ToListAsync(cancellationToken);

    public async Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken)
    {
        var existing = await GetActiveProductionContextAsync(machine, OeeTestProductionContext.OrderId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var context = new ProductionContext
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
        await dbContext.ProductionContexts.AddAsync(context, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return context;
    }

    public async Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(context).State == EntityState.Detached)
        {
            var exists = await dbContext.ProductionContexts.AnyAsync(item => item.SessionId == context.SessionId, cancellationToken);
            if (exists)
            {
                dbContext.ProductionContexts.Update(context);
            }
            else
            {
                await dbContext.ProductionContexts.AddAsync(context, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteProductionContextAsync(string sessionId, CancellationToken cancellationToken)
    {
        var context = await dbContext.ProductionContexts.FirstOrDefaultAsync(item => item.SessionId == sessionId, cancellationToken);
        if (context is null)
        {
            return;
        }

        dbContext.ProductionContexts.Remove(context);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ProductionQuantitySourceEvent?> GetProductionQuantitySourceEventAsync(
        string eventCode,
        CancellationToken cancellationToken) =>
        dbContext.ProductionQuantitySourceEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.EventCode == eventCode, cancellationToken);

    public async Task SaveProductionQuantitySourceEventAsync(
        ProductionQuantitySourceEvent sourceEvent,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProductionQuantitySourceEvents.AnyAsync(
                item => item.EventCode == sourceEvent.EventCode,
                cancellationToken))
        {
            return;
        }

        await dbContext.ProductionQuantitySourceEvents.AddAsync(sourceEvent, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken) =>
        dbContext.ProductionPeriods.AsNoTracking().FirstOrDefaultAsync(period => period.PeriodId == periodId, cancellationToken);

    public async Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProductionPeriods.FirstOrDefaultAsync(period => period.PeriodId == context.SessionId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var period = new ProductionPeriod
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
        await dbContext.ProductionPeriods.AddAsync(period, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return period;
    }

    public async Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderId, CancellationToken cancellationToken)
    {
        var currentMax = await dbContext.ProductionPeriods
            .Where(period => period.Machine.ToUpper() == machine.ToUpper() && period.OrderId.ToUpper() == orderId.ToUpper())
            .Select(period => (int?)period.PlcPeriodIndex)
            .MaxAsync(cancellationToken);
        return (currentMax ?? 0) + 1;
    }

    public async Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken)
    {
        var period = await dbContext.ProductionPeriods.FirstOrDefaultAsync(item => item.PeriodId == periodId, cancellationToken);
        if (period is null)
        {
            return;
        }

        period.EndAt = endAt;
        period.Status = status;
        period.UpdatedAt = endAt;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PlcRawInterval?> GetLatestRawIntervalAsync(string machine, CancellationToken cancellationToken) =>
        dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(item => item.Machine == machine)
            .OrderByDescending(item => item.ReadAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, PlcRawInterval>> GetLatestRawIntervalsAsync(
        IReadOnlyCollection<string> machines,
        CancellationToken cancellationToken)
    {
        var machineCodes = machines.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (machineCodes.Length == 0)
        {
            return new Dictionary<string, PlcRawInterval>(StringComparer.OrdinalIgnoreCase);
        }

        var latestReadTimes = dbContext.PlcRawIntervals
            .Where(item => machineCodes.Contains(item.Machine))
            .GroupBy(item => item.Machine)
            .Select(group => new { Machine = group.Key, ReadAt = group.Max(item => item.ReadAt) });
        var rows = await dbContext.PlcRawIntervals
            .AsNoTracking()
            .Join(
                latestReadTimes,
                raw => new { raw.Machine, raw.ReadAt },
                latest => new { latest.Machine, latest.ReadAt },
                (raw, _) => raw)
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(item => item.Machine, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<PlcRawInterval>> ListRawIntervalsAsync(
        string machine,
        long startAt,
        long endAt,
        CancellationToken cancellationToken) =>
        await dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(item =>
                item.Machine == machine &&
                item.ReadAt >= startAt &&
                item.ReadAt <= endAt)
            .OrderBy(item => item.ReadAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductionPeriod>> ListProductionPeriodsByMachineOrderAsync(
        string machine,
        string orderId,
        CancellationToken cancellationToken) =>
        await dbContext.ProductionPeriods
            .AsNoTracking()
            .Where(item =>
                item.Machine.ToUpper() == machine.ToUpper() &&
                item.OrderId.ToUpper() == orderId.ToUpper())
            .OrderBy(item => item.StartAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        if (rawIntervals.Count == 0)
        {
            return [];
        }

        var candidates = rawIntervals
            .GroupBy(raw => (raw.Machine, raw.ReadAt))
            .Select(group => group.First())
            .ToArray();
        var machines = candidates.Select(raw => raw.Machine).Distinct().ToArray();
        var minReadAt = candidates.Min(raw => raw.ReadAt);
        var maxReadAt = candidates.Max(raw => raw.ReadAt);
        var existing = await dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(raw => machines.Contains(raw.Machine) && raw.ReadAt >= minReadAt && raw.ReadAt <= maxReadAt)
            .Select(raw => new { raw.Machine, raw.ReadAt })
            .ToListAsync(cancellationToken);
        var existingKeys = existing.Select(raw => (raw.Machine, raw.ReadAt)).ToHashSet();
        var inserted = candidates.Where(raw => !existingKeys.Contains((raw.Machine, raw.ReadAt))).ToList();

        if (inserted.Count > 0)
        {
            await dbContext.PlcRawIntervals.AddRangeAsync(inserted, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return inserted;
    }

    public async Task UpsertProductionMetricsAsync(IReadOnlyCollection<ProductionMetric> metrics, CancellationToken cancellationToken)
    {
        if (metrics.Count == 0)
        {
            return;
        }

        var metricIds = metrics.Select(metric => metric.MetricId).Distinct().ToArray();
        var existingMetrics = await dbContext.ProductionMetrics
            .Where(item => metricIds.Contains(item.MetricId))
            .ToDictionaryAsync(item => item.MetricId, cancellationToken);
        foreach (var metric in metrics)
        {
            if (!existingMetrics.TryGetValue(metric.MetricId, out var existing))
            {
                await dbContext.ProductionMetrics.AddAsync(metric, cancellationToken);
                continue;
            }

            if (existing.IsFinal && !metric.IsFinal)
            {
                continue;
            }

            existing.BucketType = metric.BucketType;
            existing.BucketStart = metric.BucketStart;
            existing.BucketEnd = metric.BucketEnd;
            existing.IsFinal = metric.IsFinal;
            existing.GatewayId = metric.GatewayId;
            existing.Machine = metric.Machine;
            existing.OrderId = metric.OrderId;
            existing.SessionId = metric.SessionId;
            existing.ProductCode = metric.ProductCode;
            existing.MoldCode = metric.MoldCode;
            existing.MachineState = metric.MachineState;
            existing.ActualQty = metric.ActualQty;
            existing.TotalQty = metric.TotalQty;
            existing.PlannedQty = metric.PlannedQty;
            existing.TargetQty = metric.TargetQty;
            existing.RunTime = metric.RunTime;
            existing.StopTime = metric.StopTime;
            existing.ErrorTime = metric.ErrorTime;
            existing.ProductionTime = metric.ProductionTime;
            existing.Availability = metric.Availability;
            existing.Performance = metric.Performance;
            existing.Quality = metric.Quality;
            existing.Oee = metric.Oee;
            existing.ExtraJson = metric.ExtraJson;
            existing.UpdatedAt = metric.UpdatedAt;
        }

        if (metrics.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProductionMetric>> ListFinalSessionProductionMetricsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionMetrics
            .AsNoTracking()
            .Where(metric =>
                metric.BucketType == "session" &&
                metric.IsFinal &&
                metric.SessionId != null)
            .OrderBy(metric => metric.Machine)
            .ThenBy(metric => metric.OrderId)
            .ThenBy(metric => metric.SessionId)
            .ToListAsync(cancellationToken);

    public async Task RemoveOpenProductionMetricsAsync(
        string machine,
        string orderId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var metrics = await dbContext.ProductionMetrics
            .Where(metric =>
                metric.Machine == machine &&
                metric.OrderId == orderId &&
                !metric.IsFinal &&
                (metric.SessionId == sessionId || metric.BucketType == "order"))
            .ToListAsync(cancellationToken);
        if (metrics.Count == 0)
        {
            return;
        }

        dbContext.ProductionMetrics.RemoveRange(metrics);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<MachineStateEvent?> GetOpenMachineStateEventAsync(
        string machine,
        string orderId,
        string sessionId,
        CancellationToken cancellationToken) =>
        dbContext.MachineStateEvents
            .Where(item =>
                item.Machine.ToUpper() == machine.ToUpper() &&
                item.OrderId.ToUpper() == orderId.ToUpper() &&
                item.SessionId == sessionId &&
                item.IsOpen)
            .OrderByDescending(item => item.StartAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, MachineStateEvent>> GetOpenMachineStateEventsAsync(
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken)
    {
        var ids = sessionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<string, MachineStateEvent>(StringComparer.OrdinalIgnoreCase);
        }

        var events = await dbContext.MachineStateEvents
            .AsNoTracking()
            .Where(item => item.IsOpen && ids.Contains(item.SessionId))
            .OrderByDescending(item => item.StartAt)
            .ToListAsync(cancellationToken);
        return events
            .GroupBy(item => item.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveMachineStateEventsAsync(IReadOnlyCollection<MachineStateEvent> stateEvents, CancellationToken cancellationToken)
    {
        var distinctEvents = stateEvents
            .GroupBy(stateEvent => stateEvent.EventId, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();

        foreach (var stateEvent in distinctEvents)
        {
            var tracked = dbContext.MachineStateEvents.Local.FirstOrDefault(
                item => string.Equals(item.EventId, stateEvent.EventId, StringComparison.Ordinal));
            if (tracked is not null)
            {
                if (!ReferenceEquals(tracked, stateEvent))
                {
                    dbContext.Entry(tracked).CurrentValues.SetValues(stateEvent);
                }

                continue;
            }

            var existing = await dbContext.MachineStateEvents.FirstOrDefaultAsync(
                item => item.EventId == stateEvent.EventId,
                cancellationToken);
            if (existing is not null)
            {
                dbContext.Entry(existing).CurrentValues.SetValues(stateEvent);
            }
            else
            {
                await dbContext.MachineStateEvents.AddAsync(stateEvent, cancellationToken);
            }
        }

        if (distinctEvents.Length > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpsertSyncOutboxMessageAsync(SyncOutboxMessage message, CancellationToken cancellationToken)
    {
        await UpsertSyncOutboxMessagesAsync([message], cancellationToken);
    }

    public async Task UpsertSyncOutboxMessagesAsync(
        IReadOnlyCollection<SyncOutboxMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return;
        }

        var distinctMessages = messages
            .GroupBy(message => message.DedupeKey, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToArray();
        var dedupeKeys = distinctMessages.Select(message => message.DedupeKey).ToArray();
        var existingMessages = await dbContext.SyncOutboxMessages
            .Where(item => dedupeKeys.Contains(item.DedupeKey))
            .ToDictionaryAsync(item => item.DedupeKey, cancellationToken);
        foreach (var message in distinctMessages)
        {
            if (!existingMessages.TryGetValue(message.DedupeKey, out var existing))
            {
                await dbContext.SyncOutboxMessages.AddAsync(message, cancellationToken);
                continue;
            }

            existing.Topic = message.Topic;
            existing.EndpointPath = message.EndpointPath;
            existing.PayloadJson = message.PayloadJson;
            existing.Status = SyncOutboxStatuses.Pending;
            existing.NextAttemptAt = message.NextAttemptAt;
            existing.LastError = null;
            existing.UpdatedAt = message.UpdatedAt;
            existing.SyncedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveStaleRealtimeSnapshotMessagesAsync(
        IReadOnlyCollection<string> retainedDedupeKeys,
        CancellationToken cancellationToken)
    {
        var retained = retainedDedupeKeys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var query = dbContext.SyncOutboxMessages.Where(message =>
            message.Topic == SyncOutboxTopics.RealtimeSnapshot &&
            (message.Status == SyncOutboxStatuses.Pending || message.Status == SyncOutboxStatuses.Failed));
        if (retained.Length > 0)
        {
            query = query.Where(message => !retained.Contains(message.DedupeKey));
        }

        var stale = await query.ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        dbContext.SyncOutboxMessages.RemoveRange(stale);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemovePendingQuantityOutboxMessagesAsync(
        string machine,
        string orderId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var candidates = await dbContext.SyncOutboxMessages
            .Where(message =>
                (message.Status == SyncOutboxStatuses.Pending || message.Status == SyncOutboxStatuses.Failed) &&
                (message.Topic == SyncOutboxTopics.RealtimeSnapshot || message.Topic == SyncOutboxTopics.ProductionMetric))
            .ToListAsync(cancellationToken);
        var stale = candidates.Where(message =>
            message.Topic == SyncOutboxTopics.RealtimeSnapshot
                ? message.DedupeKey.Equals(
                    $"realtime_snapshot:{machine}:{orderId}:{sessionId}",
                    StringComparison.OrdinalIgnoreCase)
                : PayloadMatchesOrder(message.PayloadJson, machine, orderId)).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        dbContext.SyncOutboxMessages.RemoveRange(stale);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SyncOutboxMessage>> TakePendingSyncOutboxMessagesAsync(
        long now,
        int batchSize,
        IReadOnlyCollection<string> topics,
        CancellationToken cancellationToken)
    {
        var normalizedTopics = topics.Select(topic => topic.Trim()).Where(topic => topic.Length > 0).ToArray();
        var query = dbContext.SyncOutboxMessages.AsNoTracking()
            .Where(item =>
                item.NextAttemptAt <= now &&
                (item.Status == SyncOutboxStatuses.Pending || item.Status == SyncOutboxStatuses.Failed));

        if (normalizedTopics.Length > 0)
        {
            query = query.Where(item => normalizedTopics.Contains(item.Topic));
        }

        return await query
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(cancellationToken);
    }

    private static bool PayloadMatchesOrder(string payloadJson, string machine, string orderId)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return root.TryGetProperty("machine_code", out var machineElement) &&
                root.TryGetProperty("order_id", out var orderElement) &&
                string.Equals(machineElement.GetString(), machine, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(orderElement.GetString(), orderId, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task MarkSyncOutboxMessagesSucceededAsync(
        IReadOnlyCollection<SyncOutboxMessage> dispatchedMessages,
        long syncedAt,
        CancellationToken cancellationToken)
    {
        var dispatchedById = dispatchedMessages.ToDictionary(message => message.Id);
        var ids = dispatchedById.Keys.ToArray();
        var messages = await dbContext.SyncOutboxMessages
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        foreach (var message in messages.Where(message => IsSameOutboxVersion(message, dispatchedById[message.Id])))
        {
            message.Status = SyncOutboxStatuses.Synced;
            message.LastError = null;
            message.SyncedAt = syncedAt;
            message.UpdatedAt = syncedAt;
        }

        if (messages.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkSyncOutboxMessagesFailedAsync(
        IReadOnlyCollection<SyncOutboxMessage> dispatchedMessages,
        string error,
        long nextAttemptAt,
        CancellationToken cancellationToken)
    {
        var dispatchedById = dispatchedMessages.ToDictionary(message => message.Id);
        var ids = dispatchedById.Keys.ToArray();
        var messages = await dbContext.SyncOutboxMessages
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var message in messages.Where(message => IsSameOutboxVersion(message, dispatchedById[message.Id])))
        {
            message.Status = SyncOutboxStatuses.Failed;
            message.AttemptCount += 1;
            message.NextAttemptAt = nextAttemptAt;
            message.LastError = error.Length > 1000 ? error[..1000] : error;
            message.UpdatedAt = now;
        }

        if (messages.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsSameOutboxVersion(SyncOutboxMessage current, SyncOutboxMessage dispatched) =>
        current.UpdatedAt == dispatched.UpdatedAt &&
        current.Status == dispatched.Status &&
        string.Equals(current.PayloadJson, dispatched.PayloadJson, StringComparison.Ordinal);
}
