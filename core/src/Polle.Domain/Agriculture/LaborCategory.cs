namespace Polle.Domain.Agriculture;

/// <summary>Read-only labour type from the data team's public.labor_categories, edited in Expert data.</summary>
public sealed class LaborCategory
{
    public int Id { get; init; }
    public required string Name { get; init; }
}
