using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Oee;

public sealed class EfCoreOeeRawIntervalRepository(GatewayDbContext dbContext) : IOeeLocalRepository
{
    public Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken) =>
        dbContext.ProductionContexts.FirstOrDefaultAsync(
            context => context.Machine.ToUpper() == machine.ToUpper(),
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
            .ToDictionaryAsync(context => context.Machine, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public async Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
            .Where(context => context.Status == "active" || context.Status == "pause")
            .ToDictionaryAsync(context => context.Machine, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public async Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken)
    {
        var existing = await GetProductionContextAsync(machine, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var context = new ProductionContext
        {
            Machine = machine,
            Mode = OeeTestProductionContext.Mode,
            OrderId = OeeTestProductionContext.OrderId,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ActivePeriodId = $"{machine}-{OeeTestProductionContext.PeriodId}",
            CurrentPlcPeriodIndex = OeeTestProductionContext.PlcPeriodIndex,
            ProductsJson = OeeTestProductionContext.ProductsJson,
            TagsJson = OeeTestProductionContext.TagsJson,
            ExtraJson = OeeTestProductionContext.ExtraJson,
            Status = "active",
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
            var exists = await dbContext.ProductionContexts.AnyAsync(item => item.Machine.ToUpper() == context.Machine.ToUpper(), cancellationToken);
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

    public Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken) =>
        dbContext.ProductionPeriods.AsNoTracking().FirstOrDefaultAsync(period => period.PeriodId == periodId, cancellationToken);

    public async Task<ProductionPeriod> EnsureTestProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProductionPeriods.FirstOrDefaultAsync(period => period.PeriodId == context.ActivePeriodId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var period = new ProductionPeriod
        {
            PeriodId = context.ActivePeriodId,
            Machine = context.Machine,
            PlcPeriodIndex = context.CurrentPlcPeriodIndex,
            Mode = context.Mode,
            OrderId = context.OrderId,
            ServerOrderId = context.ServerOrderId,
            ProductsJson = context.ProductsJson,
            TagsJson = context.TagsJson,
            ExtraJson = context.ExtraJson,
            StartAt = startAt,
            Status = "active",
            CreatedAt = startAt,
            UpdatedAt = startAt
        };
        await dbContext.ProductionPeriods.AddAsync(period, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return period;
    }

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

    public Task<PlcRawInterval?> GetPreviousRawInPeriodAsync(
        string machine,
        int plcPeriodIndex,
        long periodStartAt,
        long beforeReadAt,
        CancellationToken cancellationToken) =>
        dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(raw =>
                raw.Machine.ToUpper() == machine.ToUpper() &&
                raw.PlcPeriodIndex == plcPeriodIndex &&
                raw.ReadAt >= periodStartAt &&
                raw.ReadAt < beforeReadAt)
            .OrderByDescending(raw => raw.ReadAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PlcRawInterval?> GetFirstRawInRangeAsync(
        string machine,
        int plcPeriodIndex,
        long startAt,
        long beforeReadAt,
        CancellationToken cancellationToken) =>
        dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(raw =>
                raw.Machine.ToUpper() == machine.ToUpper() &&
                raw.PlcPeriodIndex == plcPeriodIndex &&
                raw.ReadAt >= startAt &&
                raw.ReadAt < beforeReadAt)
            .OrderBy(raw => raw.ReadAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ProductionMetric?> GetProductionMetricAsync(
        string metricType,
        string periodId,
        string productId,
        string runState,
        long startAt,
        CancellationToken cancellationToken) =>
        dbContext.ProductionMetrics.FirstOrDefaultAsync(
            metric =>
                metric.MetricType == metricType &&
                metric.PeriodId == periodId &&
                metric.ProductId == productId &&
                metric.RunState == runState &&
                metric.StartAt == startAt,
            cancellationToken);

    public Task<ProductionMetric?> GetLatestStateMetricAsync(
        string machine,
        string periodId,
        string productId,
        string runState,
        long beforeStartAt,
        CancellationToken cancellationToken) =>
        dbContext.ProductionMetrics
            .Where(metric =>
                metric.MetricType == OeeMetricTypes.State &&
                metric.Machine.ToUpper() == machine.ToUpper() &&
                metric.PeriodId == periodId &&
                metric.ProductId == productId &&
                metric.RunState == runState &&
                metric.StartAt < beforeStartAt)
            .OrderByDescending(metric => metric.EndAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpsertProductionMetricAsync(ProductionMetric metric, CancellationToken cancellationToken)
    {
        var existing = await GetProductionMetricAsync(metric.MetricType, metric.PeriodId, metric.ProductId, metric.RunState, metric.StartAt, cancellationToken);
        if (existing is null)
        {
            await dbContext.ProductionMetrics.AddAsync(metric, cancellationToken);
        }
        else
        {
            existing.EndAt = metric.EndAt;
            existing.TotalQty = metric.TotalQty;
            existing.NgQty = metric.NgQty;
            existing.PlanQty = metric.PlanQty;
            existing.ProdTimeSec = metric.ProdTimeSec;
            existing.RunTimeSec = metric.RunTimeSec;
            existing.StopTimeSec = metric.StopTimeSec;
            existing.ErrorTimeSec = metric.ErrorTimeSec;
            existing.Availability = metric.Availability;
            existing.Performance = metric.Performance;
            existing.Quality = metric.Quality;
            existing.ActualCycleSec = metric.ActualCycleSec;
            existing.Oee = metric.Oee;
            existing.UpdatedAt = metric.UpdatedAt;
            metric.Id = existing.Id;
            metric.CreatedAt = existing.CreatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionMetric>> ListPeriodMetricsAsync(string orderId, string productId, CancellationToken cancellationToken) =>
        await dbContext.ProductionMetrics
            .AsNoTracking()
            .Where(metric => metric.MetricType == OeeMetricTypes.Period && metric.OrderId == orderId && metric.ProductId == productId)
            .ToListAsync(cancellationToken);

    public async Task UpsertProductMetricAsync(ProductMetric metric, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ProductMetrics.FirstOrDefaultAsync(item => item.OrderId == metric.OrderId && item.ProductId == metric.ProductId, cancellationToken);
        if (existing is null)
        {
            await dbContext.ProductMetrics.AddAsync(metric, cancellationToken);
        }
        else
        {
            existing.Machine = metric.Machine;
            existing.ServerOrderId = metric.ServerOrderId;
            existing.StartAt = metric.StartAt;
            existing.EndAt = metric.EndAt;
            existing.TotalQty = metric.TotalQty;
            existing.NgQty = metric.NgQty;
            existing.CountCheckQty = metric.CountCheckQty;
            existing.PlanQty = metric.PlanQty;
            existing.ProdTimeSec = metric.ProdTimeSec;
            existing.RunTimeSec = metric.RunTimeSec;
            existing.StopTimeSec = metric.StopTimeSec;
            existing.ErrorTimeSec = metric.ErrorTimeSec;
            existing.Availability = metric.Availability;
            existing.Performance = metric.Performance;
            existing.Quality = metric.Quality;
            existing.ActualCycleSec = metric.ActualCycleSec;
            existing.Oee = metric.Oee;
            existing.TargetQty = metric.TargetQty;
            existing.Status = metric.Status;
            existing.UpdatedAt = metric.UpdatedAt;
            metric.CreatedAt = existing.CreatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<DowntimeEvent?> GetOpenDowntimeEventAsync(string machine, string periodId, CancellationToken cancellationToken) =>
        dbContext.DowntimeEvents
            .Where(item => item.Machine.ToUpper() == machine.ToUpper() && item.PeriodId == periodId && item.EndAt == 0)
            .OrderByDescending(item => item.StartAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpsertDowntimeEventAsync(DowntimeEvent downtimeEvent, CancellationToken cancellationToken)
    {
        var existing = await dbContext.DowntimeEvents.FirstOrDefaultAsync(item => item.Id == downtimeEvent.Id, cancellationToken);
        if (existing is null)
        {
            await dbContext.DowntimeEvents.AddAsync(downtimeEvent, cancellationToken);
        }
        else
        {
            existing.EndAt = downtimeEvent.EndAt;
            existing.DurationSec = downtimeEvent.DurationSec;
            existing.Category = downtimeEvent.Category;
            existing.Error = downtimeEvent.Error;
            existing.Description = downtimeEvent.Description;
            existing.UpdatedAt = downtimeEvent.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueOutboxAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken)
    {
        var existing = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(
            item =>
                item.Topic == message.Topic &&
                item.SourceTable == message.SourceTable &&
                item.SourceId == message.SourceId &&
                item.Status != "synced",
            cancellationToken);
        if (existing is null)
        {
            await dbContext.MesSyncOutboxMessages.AddAsync(message, cancellationToken);
        }
        else
        {
            existing.PayloadJson = message.PayloadJson;
            existing.Status = "pending";
            existing.LastError = string.Empty;
            existing.UpdatedAt = message.UpdatedAt;
            message.Id = existing.Id;
            message.CreatedAt = existing.CreatedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MesSyncOutboxMessage>> TakePendingOutboxAsync(int batchSize, CancellationToken cancellationToken)
    {
        var messages = await dbContext.MesSyncOutboxMessages
            .Where(message => message.Status == "pending" || message.Status == "failed")
            .OrderBy(message => message.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = "sending";
            message.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return messages;
    }

    public async Task MarkOutboxSyncedAsync(string id, long now, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = "synced";
        message.LastError = string.Empty;
        message.SyncedAt = now;
        message.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkOutboxFailedAsync(string id, string error, long now, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = "failed";
        message.RetryCount++;
        message.LastError = Truncate(error, 2000);
        message.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(int PendingCount, int FailedCount, long? LastSuccess, string? LastError)> GetOutboxStatusAsync(CancellationToken cancellationToken)
    {
        var pendingCount = await dbContext.MesSyncOutboxMessages
            .CountAsync(message => message.Status == "pending" || message.Status == "sending", cancellationToken);
        var failedCount = await dbContext.MesSyncOutboxMessages
            .CountAsync(message => message.Status == "failed", cancellationToken);
        var lastSuccess = await dbContext.MesSyncOutboxMessages
            .Where(message => message.Status == "synced")
            .OrderByDescending(message => message.SyncedAt)
            .Select(message => (long?)message.SyncedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var lastError = await dbContext.MesSyncOutboxMessages
            .Where(message => message.Status == "failed" && message.LastError != string.Empty)
            .OrderByDescending(message => message.UpdatedAt)
            .Select(message => message.LastError)
            .FirstOrDefaultAsync(cancellationToken);

        return (pendingCount, failedCount, lastSuccess, lastError);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
