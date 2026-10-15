using Rostek.Gateway.Application.Scheduling;
using Rostek.Gateway.Domain.Entities;
using Xunit;

namespace Rostek.Gateway.UnitTests;

public sealed class ProductionScheduleClockTests
{
    [Fact]
    public void Local_time_uses_Vietnam_offset_independent_of_host_timezone()
    {
        var local = new DateTime(2026, 10, 15, 8, 0, 0);
        var unix = ProductionScheduleClock.FromLocal(local);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), unix);
        Assert.Equal(local, ProductionScheduleClock.ToLocal(unix));
    }

    [Fact]
    public void Daily_schedule_at_exact_boundary_advances_to_next_day()
    {
        var schedule = new ProductionSchedule { Kind = ProductionScheduleKind.Weekly, LocalTimeMinutes = 8 * 60 };
        var now = ProductionScheduleClock.FromLocal(new(2026, 10, 15, 8, 0, 0));
        Assert.Equal(now + 86400, ProductionScheduleClock.Next(schedule, now));
    }

    [Fact]
    public void Weekly_schedule_wraps_week_and_latest_due_skips_old_occurrences()
    {
        var schedule = new ProductionSchedule
        {
            Kind = ProductionScheduleKind.Weekly, LocalTimeMinutes = 8 * 60,
            WeekdaysMask = 1 << (int)DayOfWeek.Monday,
            NextRunAt = ProductionScheduleClock.FromLocal(new(2026, 9, 28, 8, 0, 0))
        };
        var now = ProductionScheduleClock.FromLocal(new(2026, 10, 15, 12, 0, 0));
        Assert.Equal(ProductionScheduleClock.FromLocal(new(2026, 10, 12, 8, 0, 0)), ProductionScheduleClock.LatestDue(schedule, now));
        Assert.Equal(ProductionScheduleClock.FromLocal(new(2026, 10, 19, 8, 0, 0)), ProductionScheduleClock.Next(schedule, now));
    }

    [Fact]
    public void Once_schedule_is_not_rearmed_after_completion()
    {
        var schedule = new ProductionSchedule { Kind = ProductionScheduleKind.Once, OnceAt = 1000, NextRunAt = 1000 };
        Assert.Equal(1000, ProductionScheduleClock.Next(schedule, 999));
        Assert.Null(ProductionScheduleClock.Next(schedule, 1000));
        Assert.Equal(1000, ProductionScheduleClock.LatestDue(schedule, 2000));
    }
}
