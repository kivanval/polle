namespace Polle.Domain.Agriculture;

/// <summary>
/// A line of machinery a farm owns (public.farm_fleet, shared with the planning solver): an equipment category,
/// an optional known unit count, and optional historical monthly hour limits per unit.
/// </summary>
public sealed class FarmEquipment
{
    public int Id { get; set; }
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    public int EquipmentCategoryId { get; set; }
    public string? CustomName { get; set; }
    public int? UnitsCount { get; set; }
    /// <summary>
    /// Hour limit for one unit in each month, January first (12 values, 0–744). Null means availability is unspecified;
    /// it implies neither zero nor unlimited capacity. Stored limits do not establish total capacity when unit count is unknown.
    /// </summary>
    public decimal[]? MonthlyHoursPerUnit { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
