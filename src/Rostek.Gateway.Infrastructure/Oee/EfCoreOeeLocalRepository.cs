using Microsoft.EntityFrameworkCore;
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
}
