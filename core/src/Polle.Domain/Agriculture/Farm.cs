using NetTopologySuite.Geometries;

namespace Polle.Domain.Agriculture;

public sealed class Farm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    /// <summary>WGS84 point the farm map opens on. Required by the API; null only for farms created before it existed.</summary>
    public Point? HomePoint { get; set; }
    /// <summary>Total money available to the farm, in USD like the solver's economics tables; null until set.</summary>
    public decimal? BudgetUsd { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
