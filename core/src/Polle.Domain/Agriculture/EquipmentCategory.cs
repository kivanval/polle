namespace Polle.Domain.Agriculture;

/// <summary>Read-only machinery type from the data team's public.equipment_categories.</summary>
public sealed class EquipmentCategory
{
    public int Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}
