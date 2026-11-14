namespace Polle.Domain.Agriculture;

/// <summary>
/// Money actually spent by a farm, to the cent: one purchase, bill or payment in a budget category. Planned amounts
/// stay in <see cref="FarmBudgetAllocation"/>; expenses are the actuals compared against them.
/// </summary>
public sealed class FarmExpense
{
    public int Id { get; set; }
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    /// <summary>The day spent; for a whole-year cost (<see cref="CoversYear"/>) January 1 of that year.</summary>
    public DateOnly SpentOn { get; set; }
    /// <summary>True for a cost that covers the whole year (a yearly lease, insurance, a subscription) rather than one day.</summary>
    public bool CoversYear { get; set; }
    /// <summary>One of <see cref="FarmBudgetAllocation.Categories"/>.</summary>
    public required string Category { get; set; }
    public required string Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    /// <summary>Price of one unit in USD, up to 4 decimals for fuel and bulk inputs.</summary>
    public decimal? UnitPriceUsd { get; set; }
    /// <summary>Total in USD with cents. Equals Quantity × UnitPriceUsd rounded to cents when both are given.</summary>
    public decimal AmountUsd { get; set; }
    public string? Supplier { get; set; }
    public string? DocumentNumber { get; set; }
    /// <summary>True once the money has left the farm; false for an unpaid bill.</summary>
    public bool IsPaid { get; set; } = true;
    public Guid? FieldId { get; set; }
    public int? WorkerId { get; set; }
    public int? EquipmentId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
