using Polle.Api.Common;
using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Solver;
using Polle.Api.Features.Weather;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Climate;

/// <param name="MonthlyRainMm">Twelve values, January first: the rain a typical month brings, in millimetres.</param>
public sealed record FieldRainfallResponse(int ReferenceStartYear, int ReferenceEndYear, double[] MonthlyRainMm);

/// <summary>
/// The rain the solver plans with, month by month, for the strategy water balance: the same ten-year daily NASA POWER
/// baseline the solver reads from <see cref="ClimateDailyEndpoints"/>, summed per calendar month.
/// </summary>
public sealed class FieldRainfallEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        OwnedResources.ProtectedGroup(app, "Weather")
            .MapGet("/fields/{fieldId:guid}/climate/monthly-rain", GetAsync)
            .RequireRateLimiting("weather")
            .Produces<FieldRainfallResponse>().ProducesProblem(404).ProducesProblem(503)
            .WithSummary("Typical rain per calendar month at an owned field, from the ten-year daily baseline the solver uses");
    }

    private static async Task<IResult> GetAsync(Guid fieldId, AppDbContext db, NasaPowerDailyClimate climate, HttpContext context,
        ILogger<FieldRainfallEndpoints> logger, CancellationToken ct)
    {
        var field = await OwnedResources.Fields(db, context).AsNoTracking().Where(item => item.Id == fieldId)
            .Select(item => new { item.Boundary, Point = item.Boundary.InteriorPoint }).SingleOrDefaultAsync(ct);
        if (field is null) return TypedResults.NotFound();
        if (!SolverRequestBuilder.HasValidFieldPoint(field.Boundary, field.Point))
            return TypedResults.Problem(statusCode: 404, title: "Climate data not found", detail: "The field boundary has no valid WGS84 representative point.");

        try
        {
            var baseline = await climate.GetAsync(field.Point!.Y, field.Point.X, ct);
            return TypedResults.Ok(new FieldRainfallResponse(baseline.ReferenceStartYear, baseline.ReferenceEndYear, MonthlyRain(baseline)));
        }
        catch (Exception ex) when (ex is WeatherUpstreamException or WeatherDataUnavailableException)
        {
            logger.LogWarning("Rainfall baseline for field {FieldId} is unavailable: {Reason}", fieldId, ex.Message);
            return TypedResults.Problem(statusCode: 503, title: "Rainfall unavailable", detail: ex.Message);
        }
    }

    /// <summary>Daily normals summed over each month of a common (non-leap) year, rounded to tenths of a millimetre.</summary>
    internal static double[] MonthlyRain(DailyClimateBaseline baseline) =>
        Enumerable.Range(1, 12).Select(month => Math.Round(
            Enumerable.Range(1, DateTime.DaysInMonth(2025, month)).Sum(day => baseline.For(new DateOnly(2025, month, day)).PrecipitationMm), 1)).ToArray();
}
