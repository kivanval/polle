using System.Globalization;
using Polle.Domain.Agriculture;

namespace Polle.Api.Features.Strategies;

/// <summary>
/// The public.materials id the solver prices irrigation water with; null leaves it unpriced. felder chooses fertilizer
/// products itself, from its fertilizers table.
/// </summary>
public sealed record SolverMaterials(string? Irrigation);

/// <summary>Builds the solver requests for one field and horizon (YYYY-MM months, both inclusive).</summary>
public static class StrategyRequests
{
    /// <summary>The farmer's own plan is scored only with complete figures, so it is never flattered by estimates.</summary>
    public const bool BaselineAllowsMissingData = false;

    public static OptimizeFieldRequest Optimization(Guid fieldId, string startMonth, string endMonth, StrategyDefinition strategy, SolverMaterials materials)
    {
        var options = strategy.Options;
        return new(fieldId.ToString(), new PlanningHorizon(startMonth, endMonth), new SolverStrategySettings(
            new RiskSettings(options.AllowMissingData, options.AllowAgronomicRisks, options.AllowSoilDepletion),
            new ReplenishmentStrategy(
                new IrrigationSettings(strategy.Irrigation, materials.Irrigation, StrategyOptions.Resolution(options.Irrigation)),
                new FertilizerSettings(strategy.Fertilizer, StrategyOptions.Resolution(options.Fertilizer)))));
    }

    /// <summary>
    /// The farmer's plantings sown inside the horizon with the water and fertilizer each one records, and the target levels
    /// a plan copied from a strategy keeps (target_percentage, left out when unknown) (a crop without them
    /// goes out rain-fed and unfertilized, with no fertilizer_application or irrigation_application), agronomic risks and
    /// soil depletion allowed so the plan is scored as entered. Irrigation is sent only with the water material to price it
    /// with. A crop with a harvest date sends it as its clearance date, so felder checks the season against it. Null when
    /// nothing is sown inside the horizon.
    /// </summary>
    public static EvaluateScenarioRequest? Baseline(Guid fieldId, string startMonth, string endMonth, IEnumerable<FieldPlanting> plantings,
        string? irrigationMaterial = null)
    {
        var first = DateOnly.ParseExact($"{startMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = DateOnly.ParseExact($"{endMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
        var schedule = plantings.Where(planting => planting.PlantingDate >= first && planting.PlantingDate <= last)
            .OrderBy(planting => planting.PlantingDate)
            .Select(planting => new ScenarioPlannedCrop(planting.CropProfileId.ToString(CultureInfo.InvariantCulture), planting.PlantingDate,
                planting is { FertilizerMaterialId: { } product, FertilizerRateKgHa: > 0 }
                    ? new FelderFertilizerApplication(product.ToString(CultureInfo.InvariantCulture), planting.FertilizerRateKgHa,
                        StrategyResultMapper.Percent(planting.FertilizerTargetPercent)) : null,
                planting.IrrigationMm is > 0 && !string.IsNullOrWhiteSpace(irrigationMaterial)
                    ? new FelderIrrigationApplication(irrigationMaterial, planting.IrrigationMm.Value, StrategyResultMapper.Percent(planting.IrrigationTargetPercent)) : null,
                planting.HarvestingDate))
            .ToArray();
        return schedule.Length == 0 ? null : new EvaluateScenarioRequest(fieldId.ToString(), new PlanningHorizon(startMonth, endMonth),
            new RiskSettings(BaselineAllowsMissingData, AllowAgronomicRisks: true, AllowSoilDepletion: true), schedule);
    }
}
