using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IOeeLocalRepository
{
    Task<ProductionContext?> GetProductionContextAsync(string machine, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, ProductionContext>> ListProductionContextsAsync(CancellationToken cancellationToken);
    Task<ProductionContext> EnsureTestProductionContextAsync(string machine, long startAt, CancellationToken cancellationToken);
    Task SaveProductionContextAsync(ProductionContext context, CancellationToken cancellationToken);
    Task<ProductionPeriod?> GetProductionPeriodAsync(string periodId, CancellationToken cancellationToken);
    Task<ProductionPeriod> EnsureProductionPeriodAsync(ProductionContext context, long startAt, CancellationToken cancellationToken);
    Task<int> GetNextPlcPeriodIndexAsync(string machine, string orderCode, CancellationToken cancellationToken);
    Task CloseProductionPeriodAsync(string periodId, long endAt, string status, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlcRawInterval>> InsertMissingRawIntervalsAsync(IReadOnlyCollection<PlcRawInterval> rawIntervals, CancellationToken cancellationToken);
}
