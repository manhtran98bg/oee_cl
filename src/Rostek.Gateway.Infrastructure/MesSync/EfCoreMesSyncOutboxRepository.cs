using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.MesSync;

public sealed class EfCoreMesSyncOutboxRepository(GatewayDbContext dbContext) : IMesSyncOutboxRepository
{
    public async Task<IReadOnlyList<Domain.Entities.MesSyncOutboxMessage>> TakePendingAsync(int batchSize, CancellationToken cancellationToken)
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

    public async Task MarkSyncedAsync(string id, long nowUnixTimeSeconds, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = "synced";
        message.LastError = string.Empty;
        message.SyncedAt = nowUnixTimeSeconds;
        message.UpdatedAt = nowUnixTimeSeconds;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(string id, string error, long nowUnixTimeSeconds, CancellationToken cancellationToken)
    {
        var message = await dbContext.MesSyncOutboxMessages.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (message is null)
        {
            return;
        }

        message.Status = "failed";
        message.RetryCount++;
        message.LastError = error.Length <= 2000 ? error : error[..2000];
        message.UpdatedAt = nowUnixTimeSeconds;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<MesSyncStatusDto> GetStatusAsync(bool enabled, CancellationToken cancellationToken)
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

        return new MesSyncStatusDto(enabled, pendingCount, failedCount, lastSuccess, lastError);
    }
}
