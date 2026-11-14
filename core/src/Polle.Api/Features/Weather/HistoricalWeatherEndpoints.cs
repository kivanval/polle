using System.Globalization;
using Polle.Api.Common;
using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Solver;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Weather;

public sealed record FieldHistoricalWeatherResponse(string Status, Guid FieldId, string Source,
    string StartMonth, string EndMonth, string TemperatureUnit, string PrecipitationUnit,
    SolverMonthlyWeather[]? Months, DateTimeOffset? RetrievedAtUtc, bool CacheHit,
    string? ApiVersion, string? Reason);

public sealed class HistoricalWeatherEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        OwnedResources.ProtectedGroup(app, "Weather")
            .MapGet("/fields/{fieldId:guid}/weather/historical", GetAsync)
            .RequireRateLimiting("weather")
            .Produces<FieldHistoricalWeatherResponse>()
            .WithSummary("Read historical NASA POWER monthly weather for an owned field");
    }

    private static async Task<IResult> GetAsync(Guid fieldId, string? startMonth, string? endMonth,
        AppDbContext db, NasaPowerHistoricalWeather weather, TimeProvider clock,
        HttpContext context, CancellationToken ct)
    {
        if (!TryMonth(startMonth, out var first) || !TryMonth(endMonth, out var last) ||
            first.Year < 1981 || last.Year >= clock.GetUtcNow().Year || last < first ||
            (last.Year - first.Year) * 12 + last.Month - first.Month >= 120)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["Use startMonth and endMonth as YYYY-MM for 1–120 completed historical months from 1981 through the previous year."]
            });

        // Npgsql translates InteriorPoint to PostGIS ST_PointOnSurface. The ownership predicate
        // is part of the database query, before any coordinates can reach the weather provider.
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
            var result = await weather.GetAsync(field.Point.Y, field.Point.X, first, last, budget.Token);
            return TypedResults.Ok(new FieldHistoricalWeatherResponse("complete", fieldId, "NASA POWER",
                startMonth!, endMonth!, "C", "mm/month", result.Months, result.RetrievedAtUtc,
                result.CacheHit, result.ApiVersion, null));
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

        FieldHistoricalWeatherResponse Incomplete(string reason) =>
            new("incomplete", fieldId, "NASA POWER", startMonth!, endMonth!, "C", "mm/month",
                null, null, false, null, reason);
    }

    private static bool TryMonth(string? value, out DateOnly month)
    {
        month = default;
        return value is { Length: 7 } && DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out month);
    }
}
