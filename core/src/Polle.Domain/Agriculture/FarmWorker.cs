namespace Polle.Domain.Agriculture;

/// <summary>A person who works on a farm, with their pay terms. Their own week of shifts lives in <see cref="FarmWorkerShift"/>.</summary>
public sealed class FarmWorker
{
    public int Id { get; set; }
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    public required string FullName { get; set; }
    /// <summary>Free-text job title, e.g. tractor driver or agronomist.</summary>
    public string? Position { get; set; }
    /// <summary>Optional labour type (public.labor_categories) the worker belongs to.</summary>
    public int? LaborCategoryId { get; set; }
    public string? Phone { get; set; }
    /// <summary>One of <see cref="PayTypes"/>: paid per worked hour or a fixed monthly salary.</summary>
    public required string PayType { get; set; }
    /// <summary>USD per hour for hourly pay, USD per month for a salary.</summary>
    public decimal PayRateUsd { get; set; }
    public DateOnly? HiredOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public static readonly string[] PayTypes = ["hourly", "monthly"];
}
