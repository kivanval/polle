using System.Text.Json.Serialization;
using Polle.Api.Features.Weather;

namespace Polle.Api.Features.Solver;

// Ivan's draft transport shape. Nullable members represent unavailable inputs in a preparation artifact.
public sealed record SolverRequest(
    [property: JsonPropertyName("farm_context")] SolverFarmContext FarmContext,
    [property: JsonPropertyName("resource_capacities")] SolverResourceCapacities? ResourceCapacities);

public sealed record SolverFarmContext(
    [property: JsonPropertyName("budget_usd")] decimal? BudgetUsd,
    [property: JsonPropertyName("planning_horizon_years")] int PlanningHorizonYears,
    [property: JsonPropertyName("start_month")] string StartMonth,
    [property: JsonPropertyName("fields")] SolverField[]? Fields);

public sealed record SolverField(
    [property: JsonPropertyName("field_id")] Guid FieldId,
    [property: JsonPropertyName("area_ha")] double AreaHa,
    [property: JsonPropertyName("weather")] SolverMonthlyWeather[]? Weather);

public sealed record SolverMonthlyWeather(
    [property: JsonPropertyName("month")] string Month,
    [property: JsonPropertyName("avg_temp_c")] double AvgTempC,
    [property: JsonPropertyName("min_temp_c")] double MinTempC,
    [property: JsonPropertyName("max_temp_c")] double MaxTempC,
    [property: JsonPropertyName("precipitation_mm")] double PrecipitationMm);

public sealed record SolverResourceCapacities(
    [property: JsonPropertyName("monthly_limits")] SolverMonthlyLimits MonthlyLimits);

public sealed record SolverMonthlyLimits(
    [property: JsonPropertyName("effective_machine_hours")] Dictionary<int, decimal>? EffectiveMachineHours,
    [property: JsonPropertyName("manual_labor_hours")] Dictionary<int, decimal>? ManualLaborHours);

public sealed record SolverMissingInput(string Path, string Reason);

public enum SolverClimateScenarioType
{
    HistoricalTenYearMonthlyBaseline
}

public sealed record SolverClimateSelection(SolverClimateScenarioType ScenarioType, int ReferenceStartYear);

public sealed record SolverClimateFieldProvenance(
    [property: JsonPropertyName("field_id")] Guid FieldId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("point")] ClimateScenarioPoint? Point,
    [property: JsonPropertyName("retrieved_at_utc")] DateTimeOffset? RetrievedAtUtc,
    [property: JsonPropertyName("api_version")] string? ApiVersion,
    [property: JsonPropertyName("cache_hit")] bool? CacheHit,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record SolverClimateProvenance(
    [property: JsonPropertyName("scenario_type")] string ScenarioType,
    [property: JsonPropertyName("reference_start_year")] int ReferenceStartYear,
    [property: JsonPropertyName("reference_end_year")] int ReferenceEndYear,
    [property: JsonPropertyName("target_start_month")] string TargetStartMonth,
    [property: JsonPropertyName("target_end_month")] string TargetEndMonth,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("aggregation")] string Aggregation,
    [property: JsonPropertyName("interpretation")] string Interpretation,
    [property: JsonPropertyName("fields")] SolverClimateFieldProvenance[] Fields);

public sealed record SolverPreparationResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("draft_request")] SolverRequest DraftRequest,
    [property: JsonPropertyName("missing_inputs")] SolverMissingInput[] MissingInputs,
    [property: JsonPropertyName("climate_provenance"),
        JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    SolverClimateProvenance? ClimateProvenance = null);
