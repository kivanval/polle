namespace Polle.Domain.Agriculture;

/// <summary>
/// Planned spending of one category within a farm's budget. Planning data only: the solver keeps using
/// <see cref="Farm.BudgetUsd"/> as the total, and allocations may temporarily exceed it.
/// </summary>
public sealed class FarmBudgetAllocation
{
    public int Id { get; set; }
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    public required string Category { get; set; }
    public decimal AmountUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Stored category keys, in display order.</summary>
    public static readonly string[] Categories = ["seeds", "fertilizers", "crop_protection", "fuel", "machinery", "labor", "irrigation", "other"];
}
