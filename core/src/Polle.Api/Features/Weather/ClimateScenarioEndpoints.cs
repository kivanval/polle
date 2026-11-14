using System.Globalization;
using Polle.Api.Common;
using Polle.Api.Features.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Weather;

public sealed record ClimateScenarioProvenance(string Source, string Dataset, string[] Parameters,
    ClimateScenarioPoint Point, DateTimeOffset RetrievedAtUtc, string? ApiVersion, bool CacheHit,
    string Aggregation, string Interpretation, string SpatialLimitation, string RevisionLimitation);

public sealed record FieldClimateScenarioResponse(string Status, Guid FieldId, string ScenarioType,
    string TargetStartMonth, string TargetEndMonth, int ReferenceStartYear, int ReferenceEndYear,
    string TemperatureUnit, string PrecipitationUnit, ClimateScenarioMonth[]? Months,
    ClimateScenarioSourceMonth[]? HistoricalSourceMonths, ClimateScenarioProvenance? Provenance,
    string? Reason);

public sealed class ClimateScenarioEndpoints : IEndpoint
{
    private const string SpatialLimitation = "NASA POWER meteorology is gridded reanalysis at about 0.5 by 0.625 degrees. The representative field point is not a field-level measurement.";
    private const string RevisionLimitation = "NASA POWER can revise archived data. Preserve the historicalSourceMonths in this response to reproduce these calculations after the cache expires.";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        OwnedResources.ProtectedGroup(app, "Weather")
            .MapGet("/fields/{fieldId:guid}/weather/climate-scenario", GetAsync)
            .RequireRateLimiting("weather")
            .Produces<FieldClimateScenarioResponse>()
            .WithSummary("Prepare a ten-year historical monthly planning baseline for an owned field");
    }

    private static async Task<IResult> GetAsync(Guid fieldId, string? targetStartMonth,
        int? referenceStartYear, AppDbContext db, ClimateScenarioService scenarios,
        TimeProvider clock, HttpContext context, CancellationToken ct)
    {
        if (!TryMonth(targetStartMonth, out var targetStart) ||
            referenceStartYear is null or < 1981 ||
            referenceStartYear > clock.GetUtcNow().Year - ClimateScenarioService.ReferenceYearCount ||
            targetStart.Year == 9999 && targetStart.Month > 1 ||
            targetStart <= new DateOnly(referenceStartYear.Value + ClimateScenarioService.ReferenceYearCount - 1, 12, 1))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["Use a targetStartMonth in YYYY-MM after the ten complete reference years, and a referenceStartYear from 1981 through the latest eligible ten-year period. The target must fit 12 calendar months."]
            });

        var targetEnd = targetStart.AddMonths(11).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var referenceEndYear = referenceStartYear.Value + ClimateScenarioService.ReferenceYearCount - 1;

        // Ownership is part of the database query, before any coordinates reach NASA POWER.
        var field = await OwnedResources.Fields(db, context).AsNoTracking()
            .Where(item => item.Id == fieldId)
            .Select(item => new { Point = item.Boundary.InteriorPoint, Boundary = item.Boundary })
            .SingleOrDefaultAsync(ct);
        if (field is null) return TypedResults.NotFound();
        if (field.Boundary is null || field.Boundary.IsEmpty || !field.Boundary.IsValid ||
            field.Boundary.SRID != 4326 || field.Point is null || field.Point.IsEmpty ||
            field.Point.SRID != 4326 || !double.IsFinite(field.Point.X) || !double.IsFinite(field.Point.Y) ||
            field.Point.X is < -180 or > 180 || field.Point.Y is <= -90 or >= 90)
            return TypedResults.Ok(Incomplete("The field boundary has no valid WGS84 representative point."));

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(27));
        try
        {
            var result = await scenarios.GetAsync(field.Point.Y, field.Point.X, targetStart,
                referenceStartYear.Value, budget.Token);
            var provenance = new ClimateScenarioProvenance(ClimateScenarioService.Source,
                "NASA POWER gridded meteorology", ["T2M", "T2M_MIN", "T2M_MAX", "PRECTOTCORR", "PRECTOTCORR_SUM"],
                new(field.Point.Y, field.Point.X), result.RetrievedAtUtc, result.ApiVersion,
                result.CacheHit, ClimateScenarioService.Aggregation, ClimateScenarioService.Interpretation,
                SpatialLimitation, RevisionLimitation);
            return TypedResults.Ok(new FieldClimateScenarioResponse("complete", fieldId, ClimateScenarioService.ScenarioType,
                targetStartMonth!, targetEnd, referenceStartYear.Value, referenceEndYear,
                "C", "mm/month", result.Months, result.HistoricalSourceMonths, provenance, null));
        }
        catch (WeatherDataUnavailableException ex)
        {
            return TypedResults.Ok(Incomplete(ex.Message));
        }
        catch (WeatherUpstreamException ex)
        {
            return TypedResults.Problem(statusCode: 503, title: "Historical weather unavailable", detail: ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Problem(statusCode: 504, title: "Historical weather timed out",
                detail: "NASA POWER did not respond within the weather request budget.");
        }

        FieldClimateScenarioResponse Incomplete(string reason) => new("incomplete", fieldId, ClimateScenarioService.ScenarioType,
            targetStartMonth!, targetEnd, referenceStartYear.Value, referenceEndYear,
            "C", "mm/month", null, null, null, reason);
    }

    private static bool TryMonth(string? value, out DateOnly month)
    {
        month = default;
        return value is { Length: 7 } && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
    }
}
