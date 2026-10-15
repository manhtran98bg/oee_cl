using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Scheduling;

public static class ProductionScheduleClock
{
    public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static DateTime ToLocal(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(VietnamOffset).DateTime;
    public static long FromLocal(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), VietnamOffset).ToUnixTimeSeconds();
    public static string Format(long? unixSeconds) => unixSeconds is long value ? ToLocal(value).ToString("dd/MM/yyyy HH:mm:ss") : "—";

    public static long? Next(ProductionSchedule schedule, long after)
    {
        if (schedule.Kind == ProductionScheduleKind.Once)
            return schedule.OnceAt > after ? schedule.OnceAt : null;

        var today = ToLocal(after).Date;
        for (var day = 0; day <= 7; day++)
        {
            var date = today.AddDays(day);
            if ((schedule.WeekdaysMask & (1 << (int)date.DayOfWeek)) == 0) continue;
            var candidate = FromLocal(date.AddMinutes(schedule.LocalTimeMinutes));
            if (candidate > after) return candidate;
        }
        return null;
    }

    public static long LatestDue(ProductionSchedule schedule, long now)
    {
        if (schedule.Kind == ProductionScheduleKind.Once) return schedule.NextRunAt!.Value;
        var today = ToLocal(now).Date;
        for (var day = 0; day <= 7; day++)
        {
            var date = today.AddDays(-day);
            if ((schedule.WeekdaysMask & (1 << (int)date.DayOfWeek)) == 0) continue;
            var candidate = FromLocal(date.AddMinutes(schedule.LocalTimeMinutes));
            if (candidate <= now && candidate >= schedule.NextRunAt) return candidate;
        }
        return schedule.NextRunAt!.Value;
    }
}
