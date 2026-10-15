namespace Rostek.Gateway.Domain.Entities;

public enum ProductionScheduleTarget { All, Selected }
public enum ProductionScheduleKind { Once, Weekly }

public sealed class ProductionSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ProductionScheduleTarget Target { get; set; }
    public string MachineIdsJson { get; set; } = "[]";
    public ProductionScheduleKind Kind { get; set; }
    public long? OnceAt { get; set; }
    public int LocalTimeMinutes { get; set; }
    public int WeekdaysMask { get; set; } = 127;
    public bool Enabled { get; set; } = true;
    public bool Deleted { get; set; }
    public long? NextRunAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class ProductionScheduleRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }
    public string ScheduleName { get; set; } = string.Empty;
    public long ScheduledAt { get; set; }
    public long CreatedAt { get; set; }
    public long? CompletedAt { get; set; }
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = string.Empty;
}

public sealed class ProductionScheduleMachineRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public long ScheduledAt { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public long NextAttemptAt { get; set; }
    public long? ExecutedAt { get; set; }
    public string TransitionsJson { get; set; } = "[]";
    public string Message { get; set; } = string.Empty;
    public Guid? CoalescedIntoId { get; set; }
}
