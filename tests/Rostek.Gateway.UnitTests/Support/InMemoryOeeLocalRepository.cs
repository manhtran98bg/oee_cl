using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.UnitTests.Support;

public sealed class InMemoryOeeLocalRepository : IOeeLocalRepository
{
    public List<ProductionContext> Contexts { get; } = [];
    public List<ProductionPeriod> Periods { get; } = [];
    public List<PlcRawInterval> RawIntervals { get; } = [];

    public Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken) =>
        Task.FromResult(Contexts.FirstOrDefault(context => context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, ProductionContext>>(Contexts
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
            Status = "active",
            OrderCode = OeeTestProductionContext.OrderCode,
            ServerOrderId = OeeTestProductionContext.ServerOrderId,
            ActivePeriodId = $"{machine}-{OeeTestProductionContext.PeriodId}",
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
        Contexts.RemoveAll(item => item.Machine.Equals(context.Machine, StringComparison.OrdinalIgnoreCase));
        Contexts.Add(context);
        return Task.CompletedTask;
    }

    public Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken) =>
        Task.FromResult(Periods.FirstOrDefault(period => period.PeriodId == periodId));

    public Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
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
            OrderCode = context.OrderCode,
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

    public Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderCode, CancellationToken cancellationToken)
    {
        var currentMax = Periods
            .Where(period => period.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) && period.OrderCode == orderCode)
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

    public Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        var inserted = rawIntervals
            .Where(raw => !RawIntervals.Any(existing => existing.Machine.Equals(raw.Machine, StringComparison.OrdinalIgnoreCase) && existing.ReadAt == raw.ReadAt))
            .ToList();
        RawIntervals.AddRange(inserted);
        return Task.FromResult<IReadOnlyList<PlcRawInterval>>(inserted);
    }
}
