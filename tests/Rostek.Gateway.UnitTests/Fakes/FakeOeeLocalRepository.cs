using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.UnitTests.Fakes;

public sealed class FakeOeeLocalRepository : IOeeLocalRepository
{
    public List<ProductionContext> Contexts { get; } = [];
    public List<ProductionPeriod> Periods { get; } = [];
    public List<PlcRawInterval> RawIntervals { get; } = [];
    public List<ProductionMetric> ProductionMetrics { get; } = [];
    public List<ProductMetric> ProductMetrics { get; } = [];
    public List<DowntimeEvent> DowntimeEvents { get; } = [];
    public List<MesSyncOutboxMessage> OutboxMessages { get; } = [];

    public Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken) =>
        Task.FromResult(Contexts.FirstOrDefault(context => context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, ProductionContext>>(Contexts
            .ToDictionary(context => context.Machine, StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, ProductionContext>>(Contexts
            .Where(context => context.Status is "active" or "pause")
            .ToDictionary(context => context.Machine, StringComparer.OrdinalIgnoreCase));

    public Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken)
    {
        var context = Contexts.FirstOrDefault(item => item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase));
        if (context is not null)
        {
            return Task.FromResult(context);
        }

        context = new ProductionContext
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
        Contexts.Add(context);
        return Task.FromResult(context);
    }

    public Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken)
    {
        Contexts.RemoveAll(item => item.Machine.Equals(context.Machine, StringComparison.OrdinalIgnoreCase));
        Contexts.Add(context);
        return Task.CompletedTask;
    }

    public Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken) =>
        Task.FromResult(Periods.FirstOrDefault(period => period.PeriodId == periodId));

    public Task<ProductionPeriod> EnsureTestProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
    {
        var period = Periods.FirstOrDefault(item => item.PeriodId == context.ActivePeriodId);
        if (period is not null)
        {
            return Task.FromResult(period);
        }

        period = new ProductionPeriod
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
        Periods.Add(period);
        return Task.FromResult(period);
    }

    public Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        var inserted = rawIntervals
            .Where(raw => !RawIntervals.Any(existing => existing.Machine.Equals(raw.Machine, StringComparison.OrdinalIgnoreCase) && existing.ReadAt == raw.ReadAt))
            .ToList();
        RawIntervals.AddRange(inserted);
        return Task.FromResult<IReadOnlyList<PlcRawInterval>>(inserted);
    }

    public Task<PlcRawInterval?> GetPreviousRawInPeriodAsync(string machine, int plcPeriodIndex, long periodStartAt, long beforeReadAt, CancellationToken cancellationToken) =>
        Task.FromResult(RawIntervals
            .Where(raw =>
                raw.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                raw.PlcPeriodIndex == plcPeriodIndex &&
                raw.ReadAt >= periodStartAt &&
                raw.ReadAt < beforeReadAt)
            .OrderByDescending(raw => raw.ReadAt)
            .FirstOrDefault());

    public Task<PlcRawInterval?> GetFirstRawInRangeAsync(string machine, int plcPeriodIndex, long startAt, long beforeReadAt, CancellationToken cancellationToken) =>
        Task.FromResult(RawIntervals
            .Where(raw =>
                raw.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                raw.PlcPeriodIndex == plcPeriodIndex &&
                raw.ReadAt >= startAt &&
                raw.ReadAt < beforeReadAt)
            .OrderBy(raw => raw.ReadAt)
            .FirstOrDefault());

    public Task<ProductionMetric?> GetProductionMetricAsync(string metricType, string periodId, string productId, string runState, long startAt, CancellationToken cancellationToken) =>
        Task.FromResult(ProductionMetrics.FirstOrDefault(metric =>
            metric.MetricType == metricType &&
            metric.PeriodId == periodId &&
            metric.ProductId == productId &&
            metric.RunState == runState &&
            metric.StartAt == startAt));

    public Task<ProductionMetric?> GetLatestStateMetricAsync(string machine, string periodId, string productId, string runState, long beforeStartAt, CancellationToken cancellationToken) =>
        Task.FromResult(ProductionMetrics
            .Where(metric =>
                metric.MetricType == OeeMetricTypes.State &&
                metric.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                metric.PeriodId == periodId &&
                metric.ProductId == productId &&
                metric.RunState == runState &&
                metric.StartAt < beforeStartAt)
            .OrderByDescending(metric => metric.EndAt)
            .FirstOrDefault());

    public Task UpsertProductionMetricAsync(ProductionMetric metric, CancellationToken cancellationToken)
    {
        ProductionMetrics.RemoveAll(item =>
            item.MetricType == metric.MetricType &&
            item.PeriodId == metric.PeriodId &&
            item.ProductId == metric.ProductId &&
            item.RunState == metric.RunState &&
            item.StartAt == metric.StartAt);
        ProductionMetrics.Add(metric);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProductionMetric>> ListPeriodMetricsAsync(string orderId, string productId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionMetric>>(ProductionMetrics
            .Where(metric => metric.MetricType == OeeMetricTypes.Period && metric.OrderId == orderId && metric.ProductId == productId)
            .ToList());

    public Task UpsertProductMetricAsync(ProductMetric metric, CancellationToken cancellationToken)
    {
        ProductMetrics.RemoveAll(item => item.OrderId == metric.OrderId && item.ProductId == metric.ProductId);
        ProductMetrics.Add(metric);
        return Task.CompletedTask;
    }

    public Task<DowntimeEvent?> GetOpenDowntimeEventAsync(string machine, string periodId, CancellationToken cancellationToken) =>
        Task.FromResult(DowntimeEvents.FirstOrDefault(item => item.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) && item.PeriodId == periodId && item.EndAt == 0));

    public Task UpsertDowntimeEventAsync(DowntimeEvent downtimeEvent, CancellationToken cancellationToken)
    {
        DowntimeEvents.RemoveAll(item => item.Id == downtimeEvent.Id);
        DowntimeEvents.Add(downtimeEvent);
        return Task.CompletedTask;
    }

    public Task EnqueueOutboxAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken)
    {
        OutboxMessages.RemoveAll(item => item.Topic == message.Topic && item.SourceTable == message.SourceTable && item.SourceId == message.SourceId && item.Status != "synced");
        OutboxMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MesSyncOutboxMessage>> TakePendingOutboxAsync(int batchSize, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MesSyncOutboxMessage>>(OutboxMessages.Where(item => item.Status is "pending" or "failed").Take(batchSize).ToList());

    public Task MarkOutboxSyncedAsync(string id, long now, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task MarkOutboxFailedAsync(string id, string error, long now, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<(int PendingCount, int FailedCount, long? LastSuccess, string? LastError)> GetOutboxStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult((0, 0, (long?)null, (string?)null));
}
