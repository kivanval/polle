namespace Polle.Domain.Agriculture;

public sealed class FieldPlanting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FieldId { get; set; }
    public Field Field { get; set; } = null!;
    // public.crop_profiles.id; the crop is the profile's crop.
    public long CropProfileId { get; set; }
    public CropProfile CropProfile { get; set; } = null!;
    public int SeasonYear { get; set; }
    public DateOnly PlantingDate { get; set; }
    // Inclusive final growing day. Null reserves the field indefinitely from PlantingDate.
    public DateOnly? HarvestingDate { get; set; }
    public string? Notes { get; set; }
    // Irrigation the farmer plans over the whole season, mm; null is rain-fed.
    public double? IrrigationMm { get; set; }
    // public.fertilizers product (a public.materials id) and kilograms of it per hectare over the season; both null for none.
    public long? FertilizerMaterialId { get; set; }
    public double? FertilizerRateKgHa { get; set; }
    // The solver's target levels (0-100) a plan copied from a strategy keeps, so evaluating it repeats the strategy:
    // how far each irrigation refills the readily available water, and the share of the nutrient deficit the fertilizer
    // covers. Null leaves the solver's default (a full refill; the rate as given).
    public double? IrrigationTargetPercent { get; set; }
    public double? FertilizerTargetPercent { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
