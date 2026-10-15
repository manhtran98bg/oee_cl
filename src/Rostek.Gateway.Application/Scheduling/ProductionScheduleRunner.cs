using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Scheduling;

public sealed class ProductionScheduleRunner(
    IProductionScheduleRepository schedules,
    IConfigRepository config,
    IOeeLocalRepository oee,
    IProductionCommandService commands,
    OeeProcessingGate gate,
    TimeProvider clock,
    ILogger<ProductionScheduleRunner> logger,
    string gatewayId) : IProductionScheduleRunner
{
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        using var lease = await gate.EnterAsync(cancellationToken);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        await MaterializeDueRunsAsync(now, cancellationToken);
        var pending = await schedules.ListPendingMachinesAsync(cancellationToken);
        foreach (var group in pending.GroupBy(item => item.MachineId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var winner = group.OrderByDescending(item => item.ScheduledAt).ThenBy(item => item.Id).First();
            if (winner.NextAttemptAt > now) continue;
            var ids = group.Select(item => item.Id).ToArray();
            try
            {
                await oee.ExecuteTransactionAsync(async token =>
                {
                    var rows = await schedules.GetMachineRunsAsync(ids, token);
                    var current = rows.Single(item => item.Id == winner.Id);
                    var executedAt = clock.GetUtcNow().ToUnixTimeSeconds();
                    current.Attempts++;
                    current.ExecutedAt = executedAt;
                    var machine = await config.GetMachineAsync(current.MachineId, includeDetails: false, token);
                    if (machine is null || !machine.Enabled)
                    {
                        current.Status = "skipped";
                        current.Message = machine is null ? "Máy đã bị xóa." : "Máy đang bị tắt.";
                    }
                    else
                    {
                        current.MachineCode = machine.Code;
                        var result = await commands.RotateActiveSessionsAsync(gatewayId, machine.Code, executedAt, token);
                        current.Status = result.Transitions.Count == 0 ? "skipped" : "succeeded";
                        current.TransitionsJson = JsonSerializer.Serialize(result.Transitions);
                        current.Message = result.Transitions.Count == 0 ? "Không có lệnh MES đang chạy (không áp dụng TEST_ORDER)."
                            : $"Đã tạo {result.Transitions.Count} session mới.";
                        if (result.Warning is not null) current.Message += " " + result.Warning;
                    }
                    Coalesce(rows, current);
                    await schedules.SaveChangesAsync(token);
                    return true;
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Scheduled session rotation failed. MachineId={MachineId}, ExecutionId={ExecutionId}", group.Key, winner.Id);
                // Reload after rollback: the persistent repository clears tracked, potentially changed contexts.
                await oee.ExecuteTransactionAsync(async token =>
                {
                    var rows = await schedules.GetMachineRunsAsync(ids, token);
                    var current = rows.Single(item => item.Id == winner.Id);
                    current.Attempts++;
                    current.Message = $"Lỗi lần {current.Attempts}/3: {ex.Message}";
                    current.NextAttemptAt = clock.GetUtcNow().ToUnixTimeSeconds() + 5;
                    if (current.Attempts >= 3)
                    {
                        current.Status = "failed";
                        current.ExecutedAt = clock.GetUtcNow().ToUnixTimeSeconds();
                        Coalesce(rows, current);
                    }
                    await schedules.SaveChangesAsync(token);
                    return true;
                }, cancellationToken);
            }
        }
        await CompleteRunsAsync(clock.GetUtcNow().ToUnixTimeSeconds(), cancellationToken);
    }

    private async Task MaterializeDueRunsAsync(long now, CancellationToken cancellationToken)
    {
        await oee.ExecuteTransactionAsync(async token =>
        {
            var due = await schedules.ListDueAsync(now, token);
            if (due.Count == 0) return true;
            var machines = await config.ListScheduleMachinesAsync(token);
            foreach (var schedule in due)
            {
                var scheduledAt = ProductionScheduleClock.LatestDue(schedule, now);
                var run = new ProductionScheduleRun
                {
                    ScheduleId = schedule.Id, ScheduleName = schedule.Name, ScheduledAt = scheduledAt, CreatedAt = now,
                    Message = scheduledAt > schedule.NextRunAt ? "Chạy bù lượt gần nhất; các mốc cũ được bỏ qua." : string.Empty
                };
                var machineIds = schedule.Target == ProductionScheduleTarget.All
                    ? machines.Where(item => item.Enabled).Select(item => item.Id).ToList()
                    : ProductionScheduleService.ReadMachineIds(schedule).Distinct().ToList();
                var rows = machineIds.Select(id => new ProductionScheduleMachineRun
                {
                    RunId = run.Id, MachineId = id, MachineCode = machines.FirstOrDefault(machine => machine.Id == id)?.Code ?? "Máy đã xóa",
                    ScheduledAt = scheduledAt, NextAttemptAt = now
                }).ToList();
                if (rows.Count == 0)
                {
                    run.Status = "skipped";
                    run.CompletedAt = now;
                    run.Message = "Không có máy đang bật trong phạm vi lịch.";
                }
                schedules.AddRun(run, rows);
                schedule.NextRunAt = schedule.Kind == ProductionScheduleKind.Once ? null : ProductionScheduleClock.Next(schedule, now);
                await schedules.SaveChangesAsync(token);
            }
            return true;
        }, cancellationToken);
    }

    private static void Coalesce(IEnumerable<ProductionScheduleMachineRun> rows, ProductionScheduleMachineRun winner)
    {
        foreach (var row in rows.Where(row => row.Id != winner.Id))
        {
            row.Status = winner.Status;
            row.ExecutedAt = winner.ExecutedAt;
            row.TransitionsJson = winner.TransitionsJson;
            row.CoalescedIntoId = winner.Id;
            row.Message = "Gộp với lượt gần nhất trên cùng máy. " + winner.Message;
        }
    }

    private async Task CompleteRunsAsync(long now, CancellationToken cancellationToken)
    {
        await oee.ExecuteTransactionAsync(async token =>
        {
            foreach (var run in await schedules.ListOpenRunsAsync(token))
            {
                var rows = await schedules.ListRunMachinesAsync(run.Id, token);
                if (rows.Any(row => row.Status == "pending")) continue;
                var succeeded = rows.Count(row => row.Status == "succeeded");
                var failed = rows.Count(row => row.Status == "failed");
                var skipped = rows.Count(row => row.Status == "skipped");
                run.Status = succeeded == rows.Count && succeeded > 0 ? "succeeded" :
                    succeeded > 0 ? "partial" : failed > 0 ? "failed" : "skipped";
                run.CompletedAt = now;
                run.Message = $"{succeeded} máy thành công, {skipped} bỏ qua, {failed} thất bại. " + run.Message;
            }
            await schedules.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
    }
}
