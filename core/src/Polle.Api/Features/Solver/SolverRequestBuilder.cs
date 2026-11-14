using System.Globalization;
using System.Security.Claims;
using Polle.Api.Features.Weather;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Polle.Api.Features.Solver;

// A future provider must return observations for the requested field and every requested month.
public interface IMonthlyWeatherProvider
{
    Task<SolverMonthlyWeather[]?> GetMonthlyAsync(Guid fieldId, Polygon boundary,
        DateOnly startMonth, int monthCount, CancellationToken ct);
}

internal sealed class UnavailableMonthlyWeatherProvider : IMonthlyWeatherProvider
{
    public Task<SolverMonthlyWeather[]?> GetMonthlyAsync(Guid fieldId, Polygon boundary,
        DateOnly startMonth, int monthCount, CancellationToken ct) => Task.FromResult<SolverMonthlyWeather[]?>(null);
}

public sealed class SolverRequestBuilder(AppDbContext db, IMonthlyWeatherProvider weather,
    ClimateScenarioService climateScenarios, TimeProvider clock)
{
    public async Task<SolverPreparationResult?> PrepareAsync(ClaimsPrincipal user, Guid farmId,
        string startMonth, int planningHorizonYears, CancellationToken ct,
        SolverClimateSelection? climateSelection = null)
    {
        if (farmId == Guid.Empty) throw new ArgumentException("A farm ID is required.", nameof(farmId));
        if (startMonth?.Length != 7 || !DateOnly.TryParseExact(startMonth + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var firstMonth))
            throw new ArgumentException("Start month must be YYYY-MM.", nameof(startMonth));
        // A bounded read prevents a malformed tool call from requesting an unbounded data range.
        if (planningHorizonYears is < 1 or > 20 || firstMonth.Year + planningHorizonYears > 9999)
            throw new ArgumentOutOfRangeException(nameof(planningHorizonYears), "Planning horizon must be 1–20 years within the calendar range.");

        if (climateSelection is not null)
        {
            if (climateSelection.ScenarioType != SolverClimateScenarioType.HistoricalTenYearMonthlyBaseline)
                throw new ArgumentException("Unsupported climate scenario type.", nameof(climateSelection));
            if (planningHorizonYears != 1)
                throw new ArgumentException("A historical climate baseline currently supports exactly one planning year.", nameof(planningHorizonYears));
            if (climateSelection.ReferenceStartYear < 1981 ||
                climateSelection.ReferenceStartYear > clock.GetUtcNow().Year - ClimateScenarioService.ReferenceYearCount)
                throw new ArgumentException("The climate reference period must contain ten completed years from 1981 onward.", nameof(climateSelection));
            var referenceEnd = new DateOnly(climateSelection.ReferenceStartYear + ClimateScenarioService.ReferenceYearCount - 1, 12, 1);
            if (firstMonth <= referenceEnd)
                throw new ArgumentException("The planning period must begin after the historical reference period.", nameof(startMonth));
        }

        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id : throw new UnauthorizedAccessException("A valid session is required.");
        var farm = await db.Farms.AsNoTracking()
            .Where(item => item.Id == farmId && item.OwnerUserId == userId)
            .Select(item => new { item.BudgetUsd }).SingleOrDefaultAsync(ct);
        if (farm is null) return null;

        var missing = new List<SolverMissingInput>();
        if (farm.BudgetUsd is null)
            missing.Add(new("farm_context.budget_usd", "The farm has no recorded USD budget."));

        // Subfields lie inside their parent and are management divisions only; sending both would count the land twice.
        var ownedFields = db.Fields.AsNoTracking()
            .Where(item => item.FarmId == farmId && item.Farm.OwnerUserId == userId && item.ParentFieldId == null)
            .OrderBy(item => item.Id);
        var fields = climateSelection is null
            ? await ownedFields.Select(item => new SolverFieldSource(item.Id, item.AreaHectares,
                item.Boundary, null)).ToArrayAsync(ct)
            : await ownedFields.Select(item => new SolverFieldSource(item.Id, item.AreaHectares,
                item.Boundary, item.Boundary.InteriorPoint)).ToArrayAsync(ct);
        if (fields.Length == 0)
            missing.Add(new("farm_context.fields", "The farm has no recorded fields."));

        var draftFields = new List<SolverField>(fields.Length);
        var climateFields = new List<SolverClimateFieldProvenance>(fields.Length);
        foreach (var field in fields)
        {
            if (!double.IsFinite(field.AreaHectares) || field.AreaHectares <= 0)
            {
                missing.Add(new($"farm_context.fields[{field.Id}].area_ha", "The field has no valid computed area in hectares."));
                if (climateSelection is not null)
                    climateFields.Add(new(field.Id, "incomplete", null, null, null, null,
                        "The field has no valid computed area in hectares."));
                continue;
            }

            SolverMonthlyWeather[]? observations = null;
            if (climateSelection is null)
            {
                observations = await weather.GetMonthlyAsync(field.Id, field.Boundary, firstMonth,
                    planningHorizonYears * 12, ct);
                if (!HasCompleteWeather(observations, firstMonth, planningHorizonYears * 12))
                {
                    observations = null;
                    missing.Add(new($"farm_context.fields[{field.Id}].weather",
                        $"Verified field-level monthly temperature and precipitation are unavailable for {startMonth} and the {planningHorizonYears}-year horizon."));
                }
            }
            else if (!HasValidFieldPoint(field.Boundary, field.Point))
            {
                const string reason = "The field boundary has no valid WGS84 representative point.";
                missing.Add(new($"farm_context.fields[{field.Id}].weather", reason));
                climateFields.Add(new(field.Id, "incomplete", null, null, null, null, reason));
            }
            else
            {
                var point = new ClimateScenarioPoint(field.Point!.Y, field.Point.X);
                try
                {
                    var scenario = await climateScenarios.GetAsync(point.Latitude, point.Longitude,
                        firstMonth, climateSelection.ReferenceStartYear, ct);
                    observations = scenario.Months.Select(month => new SolverMonthlyWeather(month.Month,
                        month.AvgTempC, month.MinTempC, month.MaxTempC, month.PrecipitationMm)).ToArray();
                    if (!HasCompleteWeather(observations, firstMonth, 12))
                        throw new WeatherDataUnavailableException("The climate scenario did not provide all twelve valid planning months.");
                    climateFields.Add(new(field.Id, "complete", point, scenario.RetrievedAtUtc,
                        scenario.ApiVersion, scenario.CacheHit, null));
                }
                catch (WeatherDataUnavailableException ex)
                {
                    observations = null;
                    missing.Add(new($"farm_context.fields[{field.Id}].weather",
                        $"Historical NASA POWER climate data are incomplete: {ex.Message}"));
                    climateFields.Add(new(field.Id, "incomplete", point, null, null, null, ex.Message));
                }
            }
            draftFields.Add(new(field.Id, field.AreaHectares, observations));
        }

        var fleet = await db.FarmEquipment.AsNoTracking()
            .Where(item => item.FarmId == farmId && item.Farm.OwnerUserId == userId)
            .Select(item => new { item.Id, item.EquipmentCategoryId, item.UnitsCount, item.MonthlyHoursPerUnit })
            .ToArrayAsync(ct);
        var shiftTypes = await db.FarmOperatorShifts.AsNoTracking()
            .Where(item => item.FarmId == farmId && item.Farm.OwnerUserId == userId)
            .Select(item => item.LaborCategoryId).Distinct().ToListAsync(ct);
        var shiftsExist = shiftTypes.Count > 0;
        var unknownFleet = fleet.Where(item => item.UnitsCount is null || item.MonthlyHoursPerUnit is null)
            .Select(item => item.Id).ToArray();
        var fleetDetail = fleet.Length == 0 ? "No farm fleet lines are recorded."
            : unknownFleet.Length > 0 ? $"Fleet lines {string.Join(", ", unknownFleet)} lack a unit count or monthly hours per unit."
            : "Fleet unit counts and monthly hours exist, but effective capacity still depends on operators.";
        missing.Add(new("resource_capacities.monthly_limits.effective_machine_hours",
            $"{fleetDetail} Qualified operator counts, category qualifications, actual availability and the solver's month-by-month limit shape are not defined."));
        missing.Add(new("resource_capacities.monthly_limits.manual_labor_hours",
            shiftsExist
                ? $"Recurring working-time windows are recorded ({(shiftTypes.Contains(null) ? "a farm default week" : "no farm default week")}, {shiftTypes.Count(id => id is not null)} labour type(s) with their own week), but they are time windows, not employee counts, and the solver request has no field for shift start or end times. Worker counts, qualifications, actual attendance and month-by-month limit semantics are missing."
                : "No operator shifts are recorded. Worker counts, labor category IDs, qualifications, actual attendance and month-by-month limit semantics are missing."));

        var request = new SolverRequest(
            new SolverFarmContext(farm.BudgetUsd, planningHorizonYears, startMonth,
                draftFields.Count == 0 ? null : [.. draftFields]),
            null);
        SolverClimateProvenance? climateProvenance = null;
        if (climateSelection is not null)
            climateProvenance = new(ClimateScenarioService.ScenarioType,
                climateSelection.ReferenceStartYear,
                climateSelection.ReferenceStartYear + ClimateScenarioService.ReferenceYearCount - 1,
                startMonth, firstMonth.AddMonths(11).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                ClimateScenarioService.Source, ClimateScenarioService.Aggregation,
                ClimateScenarioService.Interpretation, [.. climateFields]);
        return new(missing.Count == 0 ? "ready" : "incomplete", request, [.. missing], climateProvenance);
    }

    internal static bool HasValidFieldPoint(Polygon? boundary, Point? point) =>
        boundary is { IsEmpty: false, IsValid: true, SRID: 4326 } &&
        point is { IsEmpty: false, SRID: 4326 } &&
        double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        point.X is >= -180 and <= 180 && point.Y is > -90 and < 90;

    private sealed record SolverFieldSource(Guid Id, double AreaHectares, Polygon Boundary, Point? Point);

    private static bool HasCompleteWeather(SolverMonthlyWeather[]? observations, DateOnly firstMonth, int monthCount)
    {
        if (observations?.Length != monthCount) return false;
        for (var index = 0; index < monthCount; index++)
        {
            var item = observations[index];
            if (item.Month != firstMonth.AddMonths(index).ToString("yyyy-MM", CultureInfo.InvariantCulture) ||
                !double.IsFinite(item.AvgTempC) || !double.IsFinite(item.MinTempC) ||
                !double.IsFinite(item.MaxTempC) || !double.IsFinite(item.PrecipitationMm) ||
                item.MinTempC > item.AvgTempC || item.AvgTempC > item.MaxTempC || item.PrecipitationMm < 0)
                return false;
        }
        return true;
    }
}
