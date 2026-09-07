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

    public Task<PlcRawInterval?> GetPreviousAsync(string machineCode, long beforeReadAtUnixTimeSeconds, CancellationToken cancellationToken) =>
        dbContext.PlcRawIntervals
            .AsNoTracking()
            .Where(raw =>
                raw.MachineCode.ToUpper() == machineCode.ToUpper() &&
                raw.ReadAtUnixTimeSeconds < beforeReadAtUnixTimeSeconds)
            .OrderByDescending(raw => raw.ReadAtUnixTimeSeconds)
            .FirstOrDefaultAsync(cancellationToken);
}
