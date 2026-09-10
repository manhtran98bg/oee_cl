using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.UnitTests.Support;

public sealed class InMemoryOeeLocalRepository : IOeeLocalRepository
{
    public List<ProductionContext> Contexts { get; } = [];
    public List<ProductionPeriod> Periods { get; } = [];
    public List<PlcRawInterval> RawIntervals { get; } = [];

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
            .Where(context => context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) && context.Status is "active" or "pause")
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

    public Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        var inserted = rawIntervals
            .Where(raw => !RawIntervals.Any(existing => existing.Machine.Equals(raw.Machine, StringComparison.OrdinalIgnoreCase) && existing.ReadAt == raw.ReadAt))
            .ToList();
        RawIntervals.AddRange(inserted);
        return Task.FromResult<IReadOnlyList<PlcRawInterval>>(inserted);
    }
}
