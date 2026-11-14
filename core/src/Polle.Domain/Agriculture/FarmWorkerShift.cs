namespace Polle.Domain.Agriculture;

/// <summary>One recurring same-day shift of one worker: a weekday of a month, repeated every year.</summary>
public sealed class FarmWorkerShift
{
    public int Id { get; set; }
    public int WorkerId { get; set; }
    public FarmWorker Worker { get; set; } = null!;
    public int Month { get; set; }
    public int DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
