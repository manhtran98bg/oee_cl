using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Oee;

public sealed class EfCoreOeeRawIntervalRepository(GatewayDbContext dbContext) : IOeeRawIntervalRepository
{
    public async Task<IReadOnlyList<PlcRawInterval>> InsertMissingAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken)
    {
        var inserted = new List<PlcRawInterval>();
        foreach (var raw in rawIntervals)
        {
            var exists = await dbContext.PlcRawIntervals.AnyAsync(
                item => item.MachineCode.ToUpper() == raw.MachineCode.ToUpper() &&
                        item.ProductionOrderCode.ToUpper() == raw.ProductionOrderCode.ToUpper() &&
                        item.SessionId.ToUpper() == raw.SessionId.ToUpper() &&
                        item.ReadAtUnixTimeSeconds == raw.ReadAtUnixTimeSeconds,
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

    public Task<PlcRawInterval?> GetPreviousInContextAsync(
        string machineCode,
        string productionOrderCode,
        string sessionId,
        long contextStartedUnixTimeSeconds,
        long beforeReadAtUnixTimeSeconds,
        CancellationToken cancellationToken) =>
        dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(raw =>
                raw.MachineCode.ToUpper() == machineCode.ToUpper() &&
                raw.ProductionOrderCode.ToUpper() == productionOrderCode.ToUpper() &&
                raw.SessionId.ToUpper() == sessionId.ToUpper() &&
                raw.ReadAtUnixTimeSeconds >= contextStartedUnixTimeSeconds &&
                raw.ReadAtUnixTimeSeconds < beforeReadAtUnixTimeSeconds)
            .OrderByDescending(raw => raw.ReadAtUnixTimeSeconds)
            .FirstOrDefaultAsync(cancellationToken);
}
