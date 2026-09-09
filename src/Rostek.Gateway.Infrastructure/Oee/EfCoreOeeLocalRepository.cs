using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Oee;

public sealed class EfCoreOeeLocalRepository(OeeDbContext dbContext) : IOeeLocalRepository
{
    public Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken) =>
        dbContext.ProductionContexts.FirstOrDefaultAsync(
            context => context.Machine.ToUpper() == machine.ToUpper(),
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
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

    public async Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken)
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
            OrderCode = context.OrderCode,
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

    public async Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderCode, CancellationToken cancellationToken)
    {
        var currentMax = await dbContext.ProductionPeriods
            .Where(period => period.Machine.ToUpper() == machine.ToUpper() && period.OrderCode == orderCode)
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
