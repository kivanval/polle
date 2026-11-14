namespace Polle.Domain.Agriculture;

/// <summary>A recurring farm-level time window, not a worker count or calculated capacity.</summary>
public sealed class FarmOperatorShift
{
    public int Id { get; set; }
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    /// <summary>Labour type (public.labor_categories) the window belongs to; null is the farm default for every type without its own week.</summary>
    public int? LaborCategoryId { get; set; }
    public int Month { get; set; }
    public int DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
