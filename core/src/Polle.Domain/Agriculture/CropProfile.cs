namespace Polle.Domain.Agriculture;

/// <summary>
/// A way of growing a crop (public.crop_profiles), e.g. Wheat as "Winter Grain Harvest". The solver plans and evaluates
/// profiles, so a planting names one. Read-only for the application.
/// </summary>
public sealed class CropProfile
{
    public long Id { get; init; }
    public long CropId { get; init; }
    public Crop Crop { get; init; } = null!;
    public required string SystemName { get; init; }
    public bool IsPerennial { get; init; }
}
