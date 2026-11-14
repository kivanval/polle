using NetTopologySuite.Geometries;

namespace Polle.Domain.Agriculture;

public sealed class Field
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FarmId { get; set; }
    public Farm Farm { get; set; } = null!;
    /// <summary>
    /// Set for a subfield: a named part of a top-level field of the same farm, drawn inside its boundary. Only one level
    /// is allowed. The parent stays the planning unit, so totals and the solver count top-level fields only.
    /// </summary>
    public Guid? ParentFieldId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required Polygon Boundary { get; set; }
    public double AreaHectares { get; private set; }
    /// <summary>The farmer's own reading of how dry the field runs (<see cref="FieldConditions.DroughtLevels"/>); null when not given.</summary>
    public string? DroughtLevel { get; set; }
    /// <summary>The farmer's soil drainage class (<see cref="FieldConditions.Drainages"/>); null keeps the estimated soil's.</summary>
    public string? SoilDrainage { get; set; }
    /// <summary>The farmer's soil texture (<see cref="FieldConditions.Textures"/>); null keeps the estimated soil's.</summary>
    public string? SoilTexture { get; set; }
    /// <summary>Plant-available nitrogen in the soil when the plan starts, kg/ha (a soil test); null keeps the estimate.</summary>
    public double? InitialNKg { get; set; }
    /// <summary>Plant-available phosphorus when the plan starts, kg/ha; null keeps the estimate.</summary>
    public double? InitialPKg { get; set; }
    /// <summary>Plant-available potassium when the plan starts, kg/ha; null keeps the estimate.</summary>
    public double? InitialKKg { get; set; }
    /// <summary>
    /// Water missing from the root zone when the plan starts, mm below field capacity (0 = fully wet). Null derives it
    /// from <see cref="DroughtLevel"/>.
    /// </summary>
    public double? InitialDepletionMm { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
