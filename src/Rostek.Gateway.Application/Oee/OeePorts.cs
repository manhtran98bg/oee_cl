using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeLocalRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionContext?> GetActiveProductionContextAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionContext>> ListCapturableProductionContextsAsync(string machine, CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task DeleteProductionContextAsync(string sessionId, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderId, CancellationToken cancellationToken);
    Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
}
