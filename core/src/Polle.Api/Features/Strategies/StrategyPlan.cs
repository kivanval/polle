using System.Globalization;
using Polle.Domain.Agriculture;

namespace Polle.Api.Features.Strategies;

/// <param name="IrrigationMm">The strategy's irrigation for the crop over its season; null when it gets none.</param>
/// <param name="FertilizerMaterialId">The strategy's fertilizer product with <paramref name="FertilizerRateKgHa"/>; both null for none.</param>
public sealed record NewPlanting(long CropProfileId, int SeasonYear, DateOnly Sown, DateOnly Harvested,
    double? IrrigationMm = null, long? FertilizerMaterialId = null, double? FertilizerRateKgHa = null);

/// <summary>Rules for turning a strategy into the field's own crop plan.</summary>
public static class StrategyPlan
{
    /// <summary>
    /// Each crop from its sowing day to its clearance day as the solver gave them. A result stored before felder sent days
    /// has months only: those crops run from the first day of the sowing month to the last day of the clearance month.
    /// When the next crop is sown on or before the day the previous one is cleared, the previous one ends the day before,
    /// since crops on a field never overlap. The season is the year the crop is cleared. Each crop keeps the strategy's
    /// irrigation and fertilizer, so the plan is scored with them afterwards.
    /// </summary>
    public static IReadOnlyList<NewPlanting> Plantings(IEnumerable<StrategyCycle> cycles)
    {
        var ordered = cycles.Select(cycle => (Cycle: cycle,
                Sown: FelderDates.Parse(cycle.PlantingDate) ?? First(cycle.PlantingMonth),
                Harvested: FelderDates.Parse(cycle.ClearanceDate) ?? Last(cycle.ClearanceMonth)))
            .OrderBy(cycle => cycle.Sown).ToList();
        var result = new List<NewPlanting>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var (cycle, sown, harvested) = ordered[index];
            if (index + 1 < ordered.Count && ordered[index + 1].Sown <= harvested) harvested = ordered[index + 1].Sown.AddDays(-1);
            if (harvested < sown) harvested = sown;
            var product = long.TryParse(cycle.Fertilizer?.MaterialId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (long?)null;
            var rate = product is null ? null : cycle.Fertilizer?.RateKgHa is > 0 and <= 10000 ? cycle.Fertilizer.RateKgHa : null;
            result.Add(new NewPlanting(cycle.CropProfileId, harvested.Year, sown, harvested,
                cycle.IrrigationMm is > 0 ? Math.Min(Math.Round(cycle.IrrigationMm, 1), 5000) : null, rate is null ? null : product, rate));
        }
        return result;
    }

    /// <summary>The field's crops the strategy replaces: those sown on or after the strategy's first month.</summary>
    public static IReadOnlyList<FieldPlanting> Replaced(IEnumerable<FieldPlanting> existing, DateOnly start) =>
        existing.Where(planting => planting.PlantingDate >= start).ToList();

    /// <summary>A crop sown before the start that is still on the field when the strategy's first crop goes in, if any.</summary>
    public static FieldPlanting? Blocking(IEnumerable<FieldPlanting> existing, DateOnly start, DateOnly firstSown) =>
        existing.FirstOrDefault(planting => planting.PlantingDate < start && (planting.HarvestingDate is null || planting.HarvestingDate >= firstSown));

    public static DateOnly First(string month) => DateOnly.ParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly Last(string month) => First(month).AddMonths(1).AddDays(-1);
}
