using System.ComponentModel.DataAnnotations;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Scheduling;

public sealed class ProductionScheduleGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}

public sealed class ProductionScheduleInput
{
    public Guid? Id { get; set; }
    [Required, StringLength(150)]
    public string Name { get; set; } = string.Empty;
    public ProductionScheduleTarget Target { get; set; } = ProductionScheduleTarget.All;
    public List<Guid> MachineIds { get; set; } = [];
    public ProductionScheduleKind Kind { get; set; }
    public DateTime? OnceLocalAt { get; set; }
    public string LocalTime { get; set; } = "08:00";
    public List<int> Weekdays { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public long? Revision { get; set; }
}

public sealed record ProductionScheduleListItem(ProductionSchedule Schedule, string MachineSummary, ProductionScheduleRun? LastRun);
public sealed record ProductionScheduleHistory(ProductionScheduleRun Run, IReadOnlyList<ProductionScheduleMachineRun> Machines);
public sealed record SessionTransition(string OrderId, string PreviousSessionId, string NewSessionId);
public sealed record ProductionRotationResult(IReadOnlyList<SessionTransition> Transitions, string? Warning = null);

public interface IProductionScheduleService
{
    Task<IReadOnlyList<ProductionScheduleListItem>> ListAsync(CancellationToken cancellationToken);
    Task<ProductionScheduleInput?> GetInputAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken cancellationToken);
    Task<GatewayResult<Guid>> SaveAsync(ProductionScheduleInput input, CancellationToken cancellationToken);
    Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken);
    Task<GatewayResult> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductionScheduleHistory>> HistoryAsync(Guid? scheduleId, CancellationToken cancellationToken);
}

public interface IProductionScheduleRepository
{
    Task<List<ProductionSchedule>> ListAsync(CancellationToken cancellationToken);
    Task<ProductionSchedule?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<List<ProductionSchedule>> ListDueAsync(long now, CancellationToken cancellationToken);
    void Add(ProductionSchedule schedule);
    void AddRun(ProductionScheduleRun run, IReadOnlyList<ProductionScheduleMachineRun> machines);
    Task<List<ProductionScheduleRun>> ListRunsAsync(Guid? scheduleId, int limit, CancellationToken cancellationToken);
    Task<List<ProductionScheduleRun>> ListOpenRunsAsync(CancellationToken cancellationToken);
    Task<List<ProductionScheduleMachineRun>> ListPendingMachinesAsync(CancellationToken cancellationToken);
    Task<List<ProductionScheduleMachineRun>> GetMachineRunsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<List<ProductionScheduleMachineRun>> ListRunMachinesAsync(Guid runId, CancellationToken cancellationToken);
    Task CancelPendingAsync(Guid scheduleId, long now, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IProductionScheduleRunner
{
    Task RunDueAsync(CancellationToken cancellationToken);
}

public static class ProductionScheduleStatus
{
    public static string Label(string status) => status switch
    {
        "pending" => "Đang chờ",
        "succeeded" => "Thành công",
        "partial" => "Thành công một phần",
        "failed" => "Thất bại",
        "skipped" => "Bỏ qua",
        _ => status
    };
}
