using System.Text.Json;
using Polle.Api.Common;

namespace Polle.Api.Features.Drought;

public sealed record DroughtMonthResponse(string Month, string Kind);

public sealed record DroughtOverviewResponse(DateTimeOffset GeneratedAtUtc, int[] FitYears, string ProjectedFrom,
    double[][] ImageBounds, JsonElement Layers, DroughtIndexInfo Index, string Method, DroughtClass[] Classes,
    DroughtLevel? NoDrought, Dictionary<string, string> Kinds, DroughtSource[] Sources, DroughtMonthResponse[] Months);

public sealed record DroughtPointResponse(string Status, double Latitude, double Longitude, double? CellLatitude,
    double? CellLongitude, TrendCell[] Months, string? Reason);

public sealed class DroughtEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Global gridded data with no user or farm content, so the public Climate Intelligence page can read it
        // without an account. Anonymous reads are rate limited per client IP.
        var group = app.MapGroup("/drought").WithTags("Drought").AllowAnonymous().RequireRateLimiting("public-data")
            .ProducesProblem(404).ProducesProblem(429).ProducesProblem(503).ProducesValidationProblem();
        group.MapGet("", GetOverviewAsync)
            .Produces<DroughtOverviewResponse>()
            .WithSummary("List the trend model's months (fitted, projected) with layers, classes and sources");
        group.MapGet("/{month}/layers/{layer}.png", GetImageAsync)
            .Produces(200, contentType: "image/png")
            .WithSummary("Web Mercator overlay of one grid layer (drought, temperature, rain) for one month");
        group.MapGet("/point", GetPointAsync)
            .Produces<DroughtPointResponse>()
            .WithSummary("Every month of the trend model for the 0.5° grid cell containing a WGS84 point");
    }

    private static async Task<IResult> GetOverviewAsync(ClimateTrendStore store, CancellationToken ct)
    {
        try
        {
            var model = await store.GetModelAsync(ct);
            var manifest = model.Manifest;
            var (first, last) = (manifest.FitYears[0], manifest.FitYears[^1]);
            var months = Enumerable.Range(0, model.MonthCount).Select(model.MonthName)
                .Select(month => new DroughtMonthResponse(month, string.CompareOrdinal(month, manifest.ProjectedFrom) < 0 ? "fitted" : "projected"));
            return TypedResults.Ok(new DroughtOverviewResponse(manifest.GeneratedAtUtc, manifest.FitYears, manifest.ProjectedFrom,
                manifest.ImageBounds, manifest.Layers, manifest.Index, manifest.Model.Method, manifest.Classes, manifest.NoDrought,
                new()
                {
                    ["fitted"] = $"Trend line through {first}–{last}, not the weather that actually happened",
                    ["projected"] = $"The {first}–{last} trend carried forward: an expected climate, not a forecast",
                },
                manifest.Sources, [.. months]));
        }
        catch (DroughtDataUnavailableException ex) { return Unavailable(ex); }
    }

    private static async Task<IResult> GetImageAsync(string month, string layer, ClimateTrendStore store, HttpContext context, CancellationToken ct)
    {
        try
        {
            var model = await store.GetModelAsync(ct);
            if (model.MonthIndex(month) is not { } index) return InvalidMonth(model);
            var png = await store.GetImageAsync(index, layer, ct);
            if (png is null) return TypedResults.NotFound();
            // The model changes only with a deploy; the manifest timestamp versions the URL.
            context.Response.Headers.CacheControl = "private, max-age=86400";
            return TypedResults.File(png, "image/png");
        }
        catch (DroughtDataUnavailableException ex) { return Unavailable(ex); }
    }

    private static async Task<IResult> GetPointAsync(double? latitude, double? longitude, ClimateTrendStore store, CancellationToken ct)
    {
        if (latitude is not { } lat || longitude is not { } lon || !double.IsFinite(lat) || !double.IsFinite(lon) ||
            lat is <= -90 or >= 90 || lon is < -180 or > 180)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["point"] = ["Use latitude between -90 and 90 and longitude between -180 and 180."]
            });
        try
        {
            var model = await store.GetModelAsync(ct);
            if (model.CellRow(lat, lon) is not { } row)
                return TypedResults.Ok(new DroughtPointResponse("unavailable", lat, lon, null, null, [],
                    "The point is over the sea or outside the gauge-based land grid."));
            var cell = model.Cells[row];
            return TypedResults.Ok(new DroughtPointResponse("complete", lat, lon, 89.75 - 0.5 * (cell / 720), -179.75 + 0.5 * (cell % 720),
                [.. Enumerable.Range(0, model.MonthCount).Select(month => model.Cell(month, row))], null));
        }
        catch (DroughtDataUnavailableException ex) { return Unavailable(ex); }
    }

    private static IResult InvalidMonth(TrendModel model) => TypedResults.ValidationProblem(new Dictionary<string, string[]>
    {
        ["month"] = [$"Use a month as YYYY-MM between {model.MonthName(0)} and {model.MonthName(model.MonthCount - 1)}."]
    });

    private static IResult Unavailable(DroughtDataUnavailableException ex) =>
        TypedResults.Problem(statusCode: 503, title: "Climate layers unavailable", detail: ex.Message);
}
