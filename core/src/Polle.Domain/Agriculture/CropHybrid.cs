namespace Polle.Domain.Agriculture;

/// <summary>
/// A commercial hybrid or variety of a catalog crop (public.crop_hybrids), with its maturity group and how it copes with
/// drought and heat. The assistant reads it to suggest seed for a crop or a field. The solver never sees it: plans and
/// scores stay per crop profile, so a hybrid is advice for the farmer, not a planning input.
/// </summary>
public sealed class CropHybrid
{
    public const string High = "high", Medium = "medium", Low = "low";

    public long Id { get; init; }
    public long CropId { get; init; }
    public Crop Crop { get; init; } = null!;
    /// <summary>Set when the hybrid fits one way of growing the crop only (for example winter wheat), else null.</summary>
    public long? CropProfileId { get; init; }
    public required string Name { get; init; }
    /// <summary>FAO number, maturity group (MG 00) or a plain class such as Early or Mid-season.</summary>
    public required string MaturityGroup { get; init; }
    /// <summary>high, medium or low.</summary>
    public required string DroughtTolerance { get; init; }
    /// <summary>high, medium or low.</summary>
    public required string HeatTolerance { get; init; }
    public required string KeyTraits { get; init; }
    public required string RecommendedConditions { get; init; }
}
