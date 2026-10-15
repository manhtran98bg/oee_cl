using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Application.Scheduling;
using Rostek.Gateway.Domain.Entities;
using Rostek.Gateway.Infrastructure.Persistence;

namespace Rostek.Gateway.Infrastructure.Scheduling;

public sealed class EfCoreProductionScheduleRepository(OeeDbContext db) : IProductionScheduleRepository
{
    public Task<List<ProductionSchedule>> ListAsync(CancellationToken cancellationToken) =>
        db.ProductionSchedules.AsNoTracking().Where(item => !item.Deleted).OrderBy(item => item.Name).ToListAsync(cancellationToken);

    public Task<ProductionSchedule?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.ProductionSchedules.FirstOrDefaultAsync(item => item.Id == id && !item.Deleted, cancellationToken);

    public Task<List<ProductionSchedule>> ListDueAsync(long now, CancellationToken cancellationToken) =>
        db.ProductionSchedules.Where(item => !item.Deleted && item.Enabled && item.NextRunAt <= now)
            .OrderBy(item => item.NextRunAt).ToListAsync(cancellationToken);

    public void Add(ProductionSchedule schedule) => db.ProductionSchedules.Add(schedule);

    public void AddRun(ProductionScheduleRun run, IReadOnlyList<ProductionScheduleMachineRun> machines)
    {
        db.ProductionScheduleRuns.Add(run);
        db.ProductionScheduleMachineRuns.AddRange(machines);
    }

    public Task<List<ProductionScheduleRun>> ListRunsAsync(Guid? scheduleId, int limit, CancellationToken cancellationToken) =>
        db.ProductionScheduleRuns.AsNoTracking().Where(run => scheduleId == null || run.ScheduleId == scheduleId)
            .OrderByDescending(run => run.CreatedAt).ThenByDescending(run => run.ScheduledAt).Take(limit).ToListAsync(cancellationToken);

    public Task<List<ProductionScheduleRun>> ListOpenRunsAsync(CancellationToken cancellationToken) =>
        db.ProductionScheduleRuns.Where(run => run.Status == "pending").ToListAsync(cancellationToken);

    public Task<List<ProductionScheduleMachineRun>> ListPendingMachinesAsync(CancellationToken cancellationToken) =>
        db.ProductionScheduleMachineRuns.AsNoTracking().Where(run => run.Status == "pending").ToListAsync(cancellationToken);

    public Task<List<ProductionScheduleMachineRun>> GetMachineRunsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.ProductionScheduleMachineRuns.Where(run => ids.Contains(run.Id)).ToListAsync(cancellationToken);

    public Task<List<ProductionScheduleMachineRun>> ListRunMachinesAsync(Guid runId, CancellationToken cancellationToken) =>
        db.ProductionScheduleMachineRuns.AsNoTracking().Where(run => run.RunId == runId).OrderBy(run => run.MachineCode).ToListAsync(cancellationToken);

    public async Task CancelPendingAsync(Guid scheduleId, long now, CancellationToken cancellationToken)
    {
        var runs = await db.ProductionScheduleRuns.Where(run => run.ScheduleId == scheduleId && run.Status == "pending").ToListAsync(cancellationToken);
        var ids = runs.Select(run => run.Id).ToArray();
        var machines = await db.ProductionScheduleMachineRuns.Where(run => ids.Contains(run.RunId) && run.Status == "pending").ToListAsync(cancellationToken);
        foreach (var machine in machines)
        {
            machine.Status = "skipped";
            machine.ExecutedAt = now;
            machine.Message = "Lịch đã được sửa, tắt hoặc xóa.";
        }
        foreach (var run in runs)
        {
            var results = await db.ProductionScheduleMachineRuns.Where(item => item.RunId == run.Id).ToListAsync(cancellationToken);
            run.Status = results.Any(item => item.Status == "succeeded") ? "partial" : "skipped";
            run.CompletedAt = now;
            run.Message = "Lịch đã được sửa, tắt hoặc xóa; các máy chưa thực hiện được bỏ qua.";
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
