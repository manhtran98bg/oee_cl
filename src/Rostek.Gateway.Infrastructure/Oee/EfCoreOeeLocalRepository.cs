using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Oee;

public sealed class EfCoreOeeLocalRepository(OeeDbContext dbContext) : IOeeLocalRepository
{
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
            .Where(item => item.Machine.ToUpper() == machine.ToUpper())
            .OrderByDescending(item => item.ReadAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PlcRawInterval>> ListRawIntervalsAsync(
        string machine,
        long startAt,
        long endAt,
        CancellationToken cancellationToken) =>
        await dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(item =>
                item.Machine.ToUpper() == machine.ToUpper() &&
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
        var inserted = new List<PlcRawInterval>();
        foreach (var raw in rawIntervals)
        {
            var exists = await dbContext.PlcRawIntervals.AnyAsync(
                item => item.Machine.ToUpper() == raw.Machine.ToUpper() && item.ReadAt == raw.ReadAt,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            await dbContext.PlcRawIntervals.AddAsync(raw, cancellationToken);
            inserted.Add(raw);
        }

        if (inserted.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return inserted;
    }

    public async Task UpsertProductionMetricsAsync(IReadOnlyCollection<ProductionMetric> metrics, CancellationToken cancellationToken)
    {
        foreach (var metric in metrics)
        {
            var existing = await dbContext.ProductionMetrics.FirstOrDefaultAsync(
                item => item.MetricId == metric.MetricId,
                cancellationToken);
            if (existing is null)
            {
                await dbContext.ProductionMetrics.AddAsync(metric, cancellationToken);
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

    public async Task SaveMachineStateEventsAsync(IReadOnlyCollection<MachineStateEvent> stateEvents, CancellationToken cancellationToken)
    {
        foreach (var stateEvent in stateEvents)
        {
            if (dbContext.Entry(stateEvent).State != EntityState.Detached)
            {
                continue;
            }

            var exists = await dbContext.MachineStateEvents.AnyAsync(item => item.EventId == stateEvent.EventId, cancellationToken);
            if (exists)
            {
                dbContext.MachineStateEvents.Update(stateEvent);
            }
            else
            {
                await dbContext.MachineStateEvents.AddAsync(stateEvent, cancellationToken);
            }
        }

        if (stateEvents.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpsertSyncOutboxMessageAsync(SyncOutboxMessage message, CancellationToken cancellationToken)
    {
        var existing = await dbContext.SyncOutboxMessages.FirstOrDefaultAsync(
            item => item.DedupeKey == message.DedupeKey,
            cancellationToken);
        if (existing is null)
        {
            await dbContext.SyncOutboxMessages.AddAsync(message, cancellationToken);
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

    public async Task MarkSyncOutboxMessagesSucceededAsync(IReadOnlyCollection<long> ids, long syncedAt, CancellationToken cancellationToken)
    {
        var messages = await dbContext.SyncOutboxMessages
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        foreach (var message in messages)
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

    public async Task MarkSyncOutboxMessagesFailedAsync(IReadOnlyCollection<long> ids, string error, long nextAttemptAt, CancellationToken cancellationToken)
    {
        var messages = await dbContext.SyncOutboxMessages
            .Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var message in messages)
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
}
