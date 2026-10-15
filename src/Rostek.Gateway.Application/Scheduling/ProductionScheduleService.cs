using System.Globalization;
using System.Text.Json;
using Rostek.Gateway.Application.Common;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Scheduling;

public sealed class ProductionScheduleService(
    IProductionScheduleRepository schedules,
    IConfigRepository config,
    IOeeLocalRepository oee,
    OeeProcessingGate gate,
    TimeProvider clock) : IProductionScheduleService
{
    public async Task<IReadOnlyList<ProductionScheduleListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await schedules.ListAsync(cancellationToken);
        var machines = await config.ListScheduleMachinesAsync(cancellationToken);
        var result = new List<ProductionScheduleListItem>();
        foreach (var item in items)
        {
            var lastRun = (await schedules.ListRunsAsync(item.Id, 1, cancellationToken)).FirstOrDefault();
            result.Add(new(item,
            item.Target == ProductionScheduleTarget.All ? "Tất cả máy đang bật" : string.Join(", ",
                ReadMachineIds(item).Select(id => machines.FirstOrDefault(machine => machine.Id == id)?.Code ?? "Máy đã xóa")),
                lastRun));
        }
        return result;
    }

    public async Task<ProductionScheduleInput?> GetInputAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await schedules.GetAsync(id, cancellationToken);
        return item is null ? null : new ProductionScheduleInput
        {
            Id = item.Id, Name = item.Name, Target = item.Target, MachineIds = ReadMachineIds(item), Kind = item.Kind,
            OnceLocalAt = item.OnceAt is long once ? ProductionScheduleClock.ToLocal(once) : null,
            LocalTime = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(item.LocalTimeMinutes)).ToString("HH:mm"),
            Weekdays = Enumerable.Range(0, 7).Where(day => (item.WeekdaysMask & (1 << day)) != 0).ToList(),
            Enabled = item.Enabled, Revision = item.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<Machine>> ListMachinesAsync(CancellationToken cancellationToken) =>
        await config.ListScheduleMachinesAsync(cancellationToken);

    public async Task<GatewayResult<Guid>> SaveAsync(ProductionScheduleInput input, CancellationToken cancellationToken)
    {
        using var lease = await gate.EnterAsync(cancellationToken);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 150)
            return GatewayResult<Guid>.Fail("Tên lịch là bắt buộc, tối đa 150 ký tự.");
        if (!Enum.IsDefined(input.Target) || !Enum.IsDefined(input.Kind))
            return GatewayResult<Guid>.Fail("Phạm vi máy hoặc kiểu lịch không hợp lệ.");
        var machines = await config.ListScheduleMachinesAsync(cancellationToken);
        var selected = input.MachineIds.Distinct().ToList();
        if (input.Target == ProductionScheduleTarget.Selected &&
            (selected.Count == 0 || selected.Any(id => machines.All(machine => machine.Id != id))))
            return GatewayResult<Guid>.Fail("Chọn ít nhất một máy còn tồn tại trong cấu hình.");
        if (!TimeOnly.TryParseExact(input.LocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) && input.Kind == ProductionScheduleKind.Weekly)
            return GatewayResult<Guid>.Fail("Giờ lặp phải có dạng HH:mm.");
        if (input.Kind == ProductionScheduleKind.Weekly && (input.Weekdays.Count == 0 || input.Weekdays.Any(day => day is < 0 or > 6)))
            return GatewayResult<Guid>.Fail("Chọn ít nhất một ngày trong tuần.");
        long? onceAt = null;
        if (input.Kind == ProductionScheduleKind.Once && input.OnceLocalAt is DateTime local)
        {
            try { onceAt = ProductionScheduleClock.FromLocal(local); }
            catch (ArgumentException) { return GatewayResult<Guid>.Fail("Ngày giờ không hợp lệ."); }
        }
        if (input.Kind == ProductionScheduleKind.Once && (onceAt is null || onceAt <= now))
            return GatewayResult<Guid>.Fail("Lịch một lần phải có ngày giờ trong tương lai (UTC+7).");

        return await oee.ExecuteTransactionAsync(async token =>
        {
            var item = input.Id is Guid id ? await schedules.GetAsync(id, token) : new ProductionSchedule();
            if (item is null) return GatewayResult<Guid>.Fail("Không tìm thấy lịch.");
            if (input.Id is not null && input.Revision != item.UpdatedAt)
                return GatewayResult<Guid>.Fail("Lịch đã thay đổi. Tải lại trang trước khi sửa.");
            if (input.Id is null) schedules.Add(item);
            else await schedules.CancelPendingAsync(item.Id, now, token);
            item.Name = input.Name.Trim();
            item.Target = input.Target;
            item.MachineIdsJson = JsonSerializer.Serialize(input.Target == ProductionScheduleTarget.Selected ? selected : []);
            item.Kind = input.Kind;
            item.OnceAt = input.Kind == ProductionScheduleKind.Once ? onceAt : null;
            item.LocalTimeMinutes = time.Hour * 60 + time.Minute;
            item.WeekdaysMask = input.Weekdays.Aggregate(0, (mask, day) => mask | (1 << day));
            item.Enabled = input.Enabled;
            item.UpdatedAt = Math.Max(now, item.UpdatedAt + 1);
            item.NextRunAt = item.Enabled ? ProductionScheduleClock.Next(item, now) : null;
            await schedules.SaveChangesAsync(token);
            return GatewayResult<Guid>.Ok(item.Id);
        }, cancellationToken);
    }

    public async Task<GatewayResult> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        using var lease = await gate.EnterAsync(cancellationToken);
        return await oee.ExecuteTransactionAsync(async token =>
        {
            var item = await schedules.GetAsync(id, token);
            if (item is null) return GatewayResult.Fail("Không tìm thấy lịch.");
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var next = enabled ? ProductionScheduleClock.Next(item, now) : null;
            if (enabled && next is null) return GatewayResult.Fail("Hãy sửa ngày giờ trong tương lai trước khi bật lại lịch một lần.");
            await schedules.CancelPendingAsync(id, now, token);
            item.Enabled = enabled;
            item.NextRunAt = next;
            item.UpdatedAt = Math.Max(now, item.UpdatedAt + 1);
            await schedules.SaveChangesAsync(token);
            return GatewayResult.Ok();
        }, cancellationToken);
    }

    public async Task<GatewayResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        using var lease = await gate.EnterAsync(cancellationToken);
        return await oee.ExecuteTransactionAsync(async token =>
        {
            var item = await schedules.GetAsync(id, token);
            if (item is null) return GatewayResult.Fail("Không tìm thấy lịch.");
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            await schedules.CancelPendingAsync(id, now, token);
            item.Deleted = true;
            item.Enabled = false;
            item.NextRunAt = null;
            item.UpdatedAt = Math.Max(now, item.UpdatedAt + 1);
            await schedules.SaveChangesAsync(token);
            return GatewayResult.Ok();
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionScheduleHistory>> HistoryAsync(Guid? scheduleId, CancellationToken cancellationToken)
    {
        var history = new List<ProductionScheduleHistory>();
        foreach (var run in await schedules.ListRunsAsync(scheduleId, 100, cancellationToken))
            history.Add(new(run, await schedules.ListRunMachinesAsync(run.Id, cancellationToken)));
        return history;
    }

    internal static List<Guid> ReadMachineIds(ProductionSchedule item) => JsonSerializer.Deserialize<List<Guid>>(item.MachineIdsJson) ?? [];
}
