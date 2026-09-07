using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Domain.Enums;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class EfCoreMesSyncOutboxRepository(GatewayDbContext dbContext) : IMesSyncOutboxRepository
{
    public Task<ProductionContext?> GetProductionContextAsync(string machineCode, CancellationToken cancellationToken) =>
        dbContext.ProductionContexts.FirstOrDefaultAsync(
            context => context.MachineCode.ToUpper() == machineCode.ToUpper(),
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, ProductionContext>> ListActiveProductionContextsAsync(CancellationToken cancellationToken) =>
        await dbContext.ProductionContexts
            .AsNoTracking()
            .Where(context => context.Status != ProductionContextStatus.Stopped)
            .ToDictionaryAsync(context => context.MachineCode, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public async Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken)
    {
        if (dbContext.Entry(context).State == EntityState.Detached)
        {
            var exists = await dbContext.ProductionContexts.AnyAsync(item => item.Id == context.Id, cancellationToken);
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

    public async Task AddOutboxMessageAsync(MesSyncOutboxMessage message, CancellationToken cancellationToken)
    {
        await dbContext.MesSyncOutboxMessages.AddAsync(message, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<MesSyncOutboxMessage>> TakePendingAsync(long nowUnixTimeSeconds, int batchSize, CancellationToken cancellationToken)
    {
        var messages = await dbContext.MesSyncOutboxMessages
            .Where(message =>
                (message.Status == MesSyncOutboxStatus.Pending ||
                 message.Status == MesSyncOutboxStatus.Failed) &&
                message.NextAttemptUnixTimeSeconds <= nowUnixTimeSeconds)
            .OrderBy(message => message.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Status = MesSyncOutboxStatus.InProgress;
            message.UpdatedUnixTimeSeconds = nowUnixTimeSeconds;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return messages;
    }

    public async Task MarkSyncedAsync(long id, long nowUnixTimeSeconds, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = MesSyncOutboxStatus.Synced;
        message.LastError = null;
        message.SyncedUnixTimeSeconds = nowUnixTimeSeconds;
        message.UpdatedUnixTimeSeconds = nowUnixTimeSeconds;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(long id, string error, long nextAttemptUnixTimeSeconds, long nowUnixTimeSeconds, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = MesSyncOutboxStatus.Failed;
        message.RetryCount++;
        message.LastError = Truncate(error, 2000);
        message.NextAttemptUnixTimeSeconds = nextAttemptUnixTimeSeconds;
        message.UpdatedUnixTimeSeconds = nowUnixTimeSeconds;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MesSyncStatusDto> GetStatusAsync(bool enabled, CancellationToken cancellationToken)
    {
        var pendingCount = await dbContext.MesSyncOutboxMessages
            .CountAsync(message => message.Status == MesSyncOutboxStatus.Pending || message.Status == MesSyncOutboxStatus.InProgress, cancellationToken);
        var failedCount = await dbContext.MesSyncOutboxMessages
            .CountAsync(message => message.Status == MesSyncOutboxStatus.Failed, cancellationToken);
        var lastSuccess = await dbContext.MesSyncOutboxMessages
            .Where(message => message.Status == MesSyncOutboxStatus.Synced)
            .OrderByDescending(message => message.Id)
            .Select(message => message.SyncedUnixTimeSeconds)
            .FirstOrDefaultAsync(cancellationToken);
        var lastError = await dbContext.MesSyncOutboxMessages
            .Where(message => message.Status == MesSyncOutboxStatus.Failed && message.LastError != null)
            .OrderByDescending(message => message.Id)
            .Select(message => message.LastError)
            .FirstOrDefaultAsync(cancellationToken);

        return new MesSyncStatusDto(enabled, pendingCount, failedCount, lastSuccess, lastError);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
