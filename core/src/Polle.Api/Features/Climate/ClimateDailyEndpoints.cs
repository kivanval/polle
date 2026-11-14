using System.Globalization;
using System.Text.Json.Serialization;
using Polle.Api.Common;
using Polle.Api.Features.Solver;
using Polle.Api.Features.Strategies;
using Polle.Api.Features.Weather;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Climate;

public sealed record DailyClimateRecord(
    [property: JsonPropertyName("date")] DateOnly Date,
    [property: JsonPropertyName("t_min")] double TMin,
    [property: JsonPropertyName("t_max")] double TMax,
    [property: JsonPropertyName("t_mean")] double TMean,
    [property: JsonPropertyName("precip")] double Precip);

public sealed record FieldClimate(
    [property: JsonPropertyName("field_id")] string FieldId,
    [property: JsonPropertyName("timeline")] DailyClimateRecord[] Timeline);

/// <summary>
/// The Climate API the solver (felder) calls, as in its api/clients/climate/openapi.yml. Every requested day gets the
/// field's ten-year daily NASA POWER baseline for that calendar day (see <see cref="DailyClimateBaseline"/>), so future
/// planning horizons have values; it is a planning assumption, not a forecast or the weather observed on that date.
/// </summary>
public sealed class ClimateDailyEndpoints : IEndpoint
{
    public const string ApiKeyHeader = "X-Climate-Api-Key";
    public const string ApiKeySetting = "Climate:ApiKey";
    internal const int MinYear = 1900, MaxYear = 2200;
    /// <summary>The solver asks for its whole strategy horizon, which always ends before this many years from its start.</summary>
    internal const int MaxYearSpan = StrategyJobs.MaxHorizonMonths / 12;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/climate/daily/{field_id}", GetAsync)
            .WithMetadata(new SolverServiceKey(ApiKeyHeader, ApiKeySetting))
            .WithTags("Climate").AllowAnonymous().RequireRateLimiting("climate")
            .Produces<FieldClimate>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Daily min, max and mean temperature and precipitation of a field for each day of a period, from its ten-year daily NASA POWER baseline");
    }

    private static async Task<IResult> GetAsync([FromRoute(Name = "field_id")] string fieldId,
        [FromQuery(Name = "start_date")] string? startDate, [FromQuery(Name = "end_date")] string? endDate,
        AppDbContext db, NasaPowerDailyClimate climate, ILogger<ClimateDailyEndpoints> logger, CancellationToken ct)
    {
        var errors = Validate(startDate, endDate, out var start, out var end);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        if (!Guid.TryParse(fieldId, out var id)) return TypedResults.NotFound();

        var field = await db.Fields.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.Boundary, Point = item.Boundary.InteriorPoint }).SingleOrDefaultAsync(ct);
        if (field is null) return TypedResults.NotFound();
        if (!SolverRequestBuilder.HasValidFieldPoint(field.Boundary, field.Point))
            return TypedResults.Problem(statusCode: 404, title: "Climate data not found",
                detail: "The field boundary has no valid WGS84 representative point.");

        try
        {
            var baseline = await climate.GetAsync(field.Point!.Y, field.Point.X, ct);
            return TypedResults.Ok(new FieldClimate(fieldId, Timeline(baseline, start, end)));
        }
        catch (Exception ex) when (ex is WeatherUpstreamException or WeatherDataUnavailableException)
        {
            logger.LogWarning("Daily climate baseline for field {FieldId} is unavailable: {Reason}", id, ex.Message);
            return TypedResults.Problem(statusCode: 503, title: "Daily climate unavailable", detail: ex.Message);
        }
    }

    internal static Dictionary<string, string[]> Validate(string? startDate, string? endDate,
        out DateOnly start, out DateOnly end)
    {
        var errors = new Dictionary<string, string[]>();
        if (!TryParseDate(startDate, out start)) errors["start_date"] = ["Provide start_date as YYYY-MM-DD."];
        if (!TryParseDate(endDate, out end)) errors["end_date"] = ["Provide end_date as YYYY-MM-DD."];
        if (errors.Count > 0) return errors;
        if (start.Year < MinYear || end.Year > MaxYear) errors["start_date"] = [$"Dates must be between {MinYear} and {MaxYear}."];
        else if (start > end) errors["start_date"] = ["start_date must not be after end_date."];
        else if (end >= start.AddYears(MaxYearSpan)) errors["end_date"] = [$"The period must be under {MaxYearSpan} years."];
        return errors;
    }

    internal static DailyClimateRecord[] Timeline(DailyClimateBaseline baseline, DateOnly start, DateOnly end)
    {
        var timeline = new DailyClimateRecord[end.DayNumber - start.DayNumber + 1];
        for (var index = 0; index < timeline.Length; index++)
        {
            var day = start.AddDays(index);
            var normal = baseline.For(day);
            timeline[index] = new(day, normal.MinTempC, normal.MaxTempC, normal.MeanTempC, normal.PrecipitationMm);
        }
        return timeline;
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
