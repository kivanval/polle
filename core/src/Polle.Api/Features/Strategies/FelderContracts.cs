using System.Text.Json;
using System.Text.Json.Serialization;

namespace Polle.Api.Features.Strategies;

// Wire shapes of the felder solver API (felder/api/server/openapi.yml 2.5.0, main of 2026-11-15: irrigation and
// fertilizer target levels, and each cycle's irrigation_application). Ids are strings there.

public sealed record PlanningHorizon(
    [property: JsonPropertyName("start_year_month")] string StartYearMonth,
    [property: JsonPropertyName("end_year_month")] string EndYearMonth);

public sealed record RiskSettings(
    [property: JsonPropertyName("allow_missing_data")] bool AllowMissingData,
    [property: JsonPropertyName("allow_agronomic_risks")] bool AllowAgronomicRisks,
    [property: JsonPropertyName("allow_soil_depletion")] bool AllowSoilDepletion = true);

public sealed record IrrigationSettings(
    [property: JsonPropertyName("apply")] bool Apply,
    [property: JsonPropertyName("material_id")] string? MaterialId,
    [property: JsonPropertyName("resolution")] string Resolution);

/// <summary>felder picks the fertilizer product itself (its fertilizers table) and reports it per cycle.</summary>
public sealed record FertilizerSettings(
    [property: JsonPropertyName("apply")] bool Apply,
    [property: JsonPropertyName("resolution")] string Resolution);

public sealed record ReplenishmentStrategy(
    [property: JsonPropertyName("irrigation")] IrrigationSettings Irrigation,
    [property: JsonPropertyName("fertilizers")] FertilizerSettings Fertilizers);

public sealed record SolverStrategySettings(
    [property: JsonPropertyName("risk")] RiskSettings Risk,
    [property: JsonPropertyName("replenishment")] ReplenishmentStrategy Replenishment);

public sealed record OptimizeFieldRequest(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("horizon")] PlanningHorizon Horizon,
    [property: JsonPropertyName("strategy")] SolverStrategySettings Strategy);

/// <summary>
/// Water a planned crop gets over its whole cycle (felder 2026-11-13). felder caps the simulated irrigation at this
/// allowance and prices it with the material.
/// </summary>
/// <param name="MaterialId">public.materials id of the irrigation water.</param>
/// <param name="AmountMm">Total irrigation over the crop cycle, mm.</param>
/// <param name="TargetPercentage">
/// felder 2.5 (2026-11-15): how far each irrigation refills the readily available water, 0-100; felder takes 100 when it
/// is left out. A strategy's cycle reports the level it chose, and the plan copied from it sends that level back.
/// </param>
public sealed record FelderIrrigationApplication(
    [property: JsonPropertyName("material_id")] string MaterialId,
    [property: JsonPropertyName("amount_mm")] double AmountMm,
    [property: JsonPropertyName("target_percentage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? TargetPercentage = null);

/// <summary>
/// One crop of the plan to evaluate. Since felder's 2026-11-13 evaluation update each crop carries its own fertilizer
/// product and rate and its own irrigation allowance, replacing the 0-1 levels and the request-wide materials. Both are
/// left out (null, not sent) for a rain-fed, unfertilized crop.
/// </summary>
public sealed record ScenarioPlannedCrop(
    [property: JsonPropertyName("crop_profile_id")] string CropProfileId,
    [property: JsonPropertyName("plant_date")] DateOnly PlantDate,
    [property: JsonPropertyName("fertilizer_application"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FelderFertilizerApplication? FertilizerApplication = null,
    [property: JsonPropertyName("irrigation_application"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    FelderIrrigationApplication? IrrigationApplication = null,
    // felder 2026-11-14: the day the farmer clears the field; felder flags a risk when its simulated season doesn't end
    // there. Left out for a crop still growing.
    [property: JsonPropertyName("clearance_date"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateOnly? ClearanceDate = null);

public sealed record EvaluateScenarioRequest(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("horizon")] PlanningHorizon Horizon,
    [property: JsonPropertyName("risk")] RiskSettings Risk,
    [property: JsonPropertyName("schedule")] ScenarioPlannedCrop[] Schedule);

public sealed record FelderTaskResponse([property: JsonPropertyName("task_id")] string TaskId);

/// <summary>
/// One crop of a felder schedule. Since felder's day-precision update (2026-11-13) a cycle's dates are whole days
/// (plant_date, clearance_date, harvest_dates; sowing usually on the 1st, 11th or 21st). Results stored before that carry
/// months only (planting_year_month and the rest), so both shapes are read: <see cref="PlantingMonth"/> and the other
/// month properties take whichever is there.
/// </summary>
public sealed record CropProfileCycle(
    [property: JsonPropertyName("crop_profile_id")] string CropProfileId,
    [property: JsonPropertyName("planting_year_month")] string? PlantingYearMonth,
    [property: JsonPropertyName("clearance_year_month")] string? ClearanceYearMonth,
    [property: JsonPropertyName("harvest_year_months")] string[]? HarvestYearMonths,
    // Absent from felder 2.4 (2026-11-12), which sends Financials instead; read as zero there.
    [property: JsonPropertyName("expected_revenue_cents")] long ExpectedRevenueCents,
    [property: JsonPropertyName("expenses_cents")] long ExpensesCents,
    [property: JsonPropertyName("yield_multiplier")] double YieldMultiplier,
    [property: JsonPropertyName("irrigation_applied_mm")] double IrrigationAppliedMm,
    [property: JsonPropertyName("nitrogen_applied_kg_ha")] double NitrogenAppliedKgHa,
    [property: JsonPropertyName("nitrogen_fixed_kg_ha")] double NitrogenFixedKgHa,
    [property: JsonPropertyName("nitrogen_removed_kg_ha")] double NitrogenRemovedKgHa,
    [property: JsonPropertyName("risk_notes")] string[]? RiskNotes,
    // Felder replaced risk_notes with notes on 2026-11-09: objects {message, category, importance}, or tagged strings.
    [property: JsonPropertyName("notes")] JsonElement[]? Notes = null,
    // Phosphorus and potassium arrived later than nitrogen; results stored before them read as zero.
    [property: JsonPropertyName("phosphorus_applied_kg_ha")] double PhosphorusAppliedKgHa = 0,
    [property: JsonPropertyName("phosphorus_removed_kg_ha")] double PhosphorusRemovedKgHa = 0,
    [property: JsonPropertyName("potassium_applied_kg_ha")] double PotassiumAppliedKgHa = 0,
    [property: JsonPropertyName("potassium_removed_kg_ha")] double PotassiumRemovedKgHa = 0,
    // felder 2.3 (2026-11-10) replaced the cycle totals above with month-by-month figures.
    [property: JsonPropertyName("monthly_dynamics")] CropProfileCycleMonthlyDynamics? MonthlyDynamics = null,
    // felder 2.3 (2026-11-11): public.materials id of the fertilizer product the cycle uses; null when unfertilized.
    [property: JsonPropertyName("fertilizer_material_id")] string? FertilizerMaterialId = null,
    // felder 2.4 (2026-11-11) replaced fertilizer_material_id with the product and its rate; null when unfertilized.
    [property: JsonPropertyName("fertilizer_application")] FelderFertilizerApplication? FertilizerApplication = null,
    // felder 2.4 (2026-11-12): revenue and every cost of the cycle over the whole field, OpEx included.
    [property: JsonPropertyName("financials")] CropProfileCycleFinancials? Financials = null,
    // felder 2026-11-13 (day precision): YYYY-MM-DD dates replacing the three month fields above.
    [property: JsonPropertyName("plant_date")] string? PlantDate = null,
    [property: JsonPropertyName("clearance_date")] string? ClearanceDate = null,
    [property: JsonPropertyName("harvest_dates")] string[]? HarvestDates = null,
    // felder 2.5 (2026-11-15): the cycle's water allowance and the refill level it was planned with; null when rain-fed.
    [property: JsonPropertyName("irrigation_application")] FelderIrrigationApplication? IrrigationApplication = null)
{
    /// <summary>The sowing day, YYYY-MM-DD; null for a result stored with months only or a date that doesn't parse.</summary>
    [JsonIgnore] public string? PlantingDay => FelderDates.Day(PlantDate);
    [JsonIgnore] public string? ClearanceDay => FelderDates.Day(ClearanceDate);
    [JsonIgnore] public string[] HarvestDays => (HarvestDates ?? []).Select(FelderDates.Day).OfType<string>().ToArray();

    /// <summary>YYYY-MM of sowing, from the date or the older month field; empty when neither is there.</summary>
    [JsonIgnore] public string PlantingMonth => FelderDates.Month(PlantDate) ?? PlantingYearMonth ?? "";
    [JsonIgnore] public string ClearanceMonth => FelderDates.Month(ClearanceDate) ?? ClearanceYearMonth ?? "";
    [JsonIgnore] public string[] HarvestMonths => HarvestDates is { Length: > 0 } dates
        ? dates.Select(FelderDates.Month).OfType<string>().Distinct().ToArray() : HarvestYearMonths ?? [];
}

/// <summary>felder's calendar dates (ISO YYYY-MM-DD), read leniently: anything else counts as absent.</summary>
public static class FelderDates
{
    public static DateOnly? Parse(string? value) =>
        value is { Length: >= 10 } && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;

    public static string? Day(string? value) => Parse(value)?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public static string? Month(string? value) => Parse(value)?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A cycle's money over the whole field, US cents.</summary>
/// <param name="BaseOpexCents">Machinery, fuel and labour: the profile's OpEx per hectare times the field's area.</param>
public sealed record CropProfileCycleFinancials(
    [property: JsonPropertyName("gross_revenue_cents")] long GrossRevenueCents,
    [property: JsonPropertyName("base_opex_cents")] long BaseOpexCents,
    [property: JsonPropertyName("planting_cost_cents")] long PlantingCostCents,
    [property: JsonPropertyName("water_cost_cents")] long WaterCostCents,
    [property: JsonPropertyName("fertilizer_cost_cents")] long FertilizerCostCents)
{
    [JsonIgnore] public long CostsCents => BaseOpexCents + PlantingCostCents + WaterCostCents + FertilizerCostCents;
}

/// <param name="RateKgHa">Kilograms of the product itself (not of N, P or K) per hectare over the cycle.</param>
/// <param name="TargetPercentage">felder 2.5 (2026-11-15): the share of the crop's nutrient deficit the fertilizer covers, 0-100; null when not given.</param>
public sealed record FelderFertilizerApplication(
    [property: JsonPropertyName("material_id")] string? MaterialId,
    [property: JsonPropertyName("rate_kg_ha")] double? RateKgHa,
    [property: JsonPropertyName("target_percentage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? TargetPercentage = null);

/// <summary>Per-month figures of one cycle, keyed by YYYY-MM.</summary>
/// <param name="IrrigationAppliedMm">The first 2.3 name of <paramref name="WaterAppliedMm"/>, kept for results stored with it.</param>
public sealed record CropProfileCycleMonthlyDynamics(
    [property: JsonPropertyName("irrigation_applied_mm")] Dictionary<string, double>? IrrigationAppliedMm,
    [property: JsonPropertyName("water_removed_mm")] Dictionary<string, double>? WaterRemovedMm,
    [property: JsonPropertyName("nitrogen_applied_kg_ha")] Dictionary<string, double>? NitrogenAppliedKgHa,
    [property: JsonPropertyName("nitrogen_fixed_kg_ha")] Dictionary<string, double>? NitrogenFixedKgHa,
    [property: JsonPropertyName("nitrogen_removed_kg_ha")] Dictionary<string, double>? NitrogenRemovedKgHa,
    [property: JsonPropertyName("phosphorus_applied_kg_ha")] Dictionary<string, double>? PhosphorusAppliedKgHa,
    [property: JsonPropertyName("phosphorus_removed_kg_ha")] Dictionary<string, double>? PhosphorusRemovedKgHa,
    [property: JsonPropertyName("potassium_applied_kg_ha")] Dictionary<string, double>? PotassiumAppliedKgHa,
    [property: JsonPropertyName("potassium_removed_kg_ha")] Dictionary<string, double>? PotassiumRemovedKgHa,
    [property: JsonPropertyName("water_applied_mm")] Dictionary<string, double>? WaterAppliedMm = null)
{
    /// <summary>Irrigation per month under either name.</summary>
    [JsonIgnore] public Dictionary<string, double>? Irrigation => WaterAppliedMm ?? IrrigationAppliedMm;
}

/// <summary>
/// felder's OptimizeFieldResponse, and since 2026-11-13 also its EvaluateScenarioResponse, which has the same shape: the
/// evaluation now returns every planned crop as a full cycle (money, yield, monthly water and N-P-K, the solver's own
/// notes), including a crop its simulation stopped on, whose notes say why. An evaluation's status is always FEASIBLE.
/// </summary>
public sealed record OptimizeFieldResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expected_profit_cents")] long? ExpectedProfitCents,
    [property: JsonPropertyName("schedule")] CropProfileCycle[] Schedule);
