using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Polle.Api.Common;
using Polle.Api.Features.Strategies;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Features.Finance;

/// <summary>Finance API v1 request (deprecated by felder's Finance API 2.0.0, still served).</summary>
public sealed record PricingRequest(
    [property: JsonPropertyName("start_year"), Required] int? StartYear,
    [property: JsonPropertyName("end_year"), Required] int? EndYear,
    [property: JsonPropertyName("material_ids"), Required] string[]? MaterialIds);

/// <summary>Finance API v2 request for material prices or crop profile OpEx: the ids are material or crop profile ids.</summary>
public sealed record FinanceRequestV2(
    [property: JsonPropertyName("start_year"), Required] int? StartYear,
    [property: JsonPropertyName("end_year"), Required] int? EndYear,
    [property: JsonPropertyName("ids"), Required] string[]? Ids);

/// <param name="PriceCents">USD cents: per tonne (or cubic metre of water) for a material, per hectare for crop profile OpEx.</param>
public sealed record AnnualRate(
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("price_cents")] int PriceCents);

public sealed record PricingResponse(
    [property: JsonPropertyName("material_prices")] Dictionary<string, AnnualRate[]> MaterialPrices);

public sealed record CropProfileOpexResponse(
    [property: JsonPropertyName("profile_base_opex")] Dictionary<string, AnnualRate[]> ProfileBaseOpex);

/// <summary>
/// The Finance API the solver (felder) calls, as in its api/clients/finance/openapi.yml (2.0.0). Prices and OpEx are
/// synthetic placeholders. The solver is a backend with no session, so it authenticates with its own key (checked in
/// Program.cs before anything else) instead of a user session, the proxy secret or a CSRF token.
/// </summary>
public sealed class FinanceEndpoints : IEndpoint
{
    public const string ApiKeyHeader = "X-Finance-Api-Key";
    private const int MinYear = 1900, MaxYear = 2200, MaxIds = 5000, MaxBodyBytes = 128 * 1024;
    /// <summary>
    /// Calendar years a request may span. The solver asks for every year its horizon touches, and the longest strategy
    /// horizon that doesn't start in January touches one more calendar year than its length in years.
    /// </summary>
    internal const int MaxYearSpan = StrategyJobs.MaxHorizonMonths / 12 + 1;

    private sealed class FinanceRoute;

    /// <summary>Whether routing resolved this request to a Finance API route. Goes by the route, not the path text.</summary>
    public static bool IsFinanceRoute(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<FinanceRoute>() is not null;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        Finance(app.MapPost("/v1/finance/prices", GetPricesV1Async)).Produces<PricingResponse>()
            .WithSummary("(Deprecated, use v2) Annual price per tonne of each material over a horizon");
        Finance(app.MapPost("/v2/finance/materials/prices", GetPricesV2Async)).Produces<PricingResponse>()
            .WithSummary("Annual price per tonne of each material over a horizon. Years outside the price table get the nearest year's price; unknown materials are left out");
        Finance(app.MapPost("/v2/finance/crop-profiles/opex", GetOpexAsync)).Produces<CropProfileOpexResponse>()
            .WithSummary("Annual base operating costs (machinery, labour) per hectare of each crop profile over a horizon. Years outside the table get the nearest year's figure; unknown profiles are left out");
    }

    private static RouteHandlerBuilder Finance(RouteHandlerBuilder route) => route
        .WithMetadata(new FinanceRoute())
        .WithTags("Finance").AllowAnonymous().RequireRateLimiting("finance")
        // The solver asks for its whole catalog at once, which is over the API's default 16 KB body limit.
        .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
        .ProducesValidationProblem().ProducesProblem(429);

    internal static Dictionary<string, string[]> Validate(PricingRequest request) => Validate(request.StartYear, request.EndYear, request.MaterialIds, "material_ids");

    internal static Dictionary<string, string[]> Validate(FinanceRequestV2 request) => Validate(request.StartYear, request.EndYear, request.Ids, "ids");

    private static Dictionary<string, string[]> Validate(int? startYear, int? endYear, string[]? ids, string idsField)
    {
        var errors = new Dictionary<string, string[]>();
        if (startYear is null) errors["start_year"] = ["Provide the first year of the horizon."];
        if (endYear is null) errors["end_year"] = ["Provide the last year of the horizon."];
        if (ids is null) errors[idsField] = ["Provide a list of ids."];
        else if (ids.Length > MaxIds) errors[idsField] = [$"Ask for at most {MaxIds} ids at a time."];
        if (startYear is { } first && endYear is { } last)
        {
            if (first < MinYear || last > MaxYear) errors["start_year"] = [$"Years must be between {MinYear} and {MaxYear}."];
            else if (first > last) errors["start_year"] = ["The first year must not be after the last year."];
            else if (last - first >= MaxYearSpan) errors["end_year"] = [$"The horizon must span at most {MaxYearSpan} calendar years."];
        }
        return errors;
    }

    private static async Task<IResult> GetPricesV1Async(PricingRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(new PricingResponse(await RatesAsync(db, Table.MaterialPrices, request.StartYear!.Value, request.EndYear!.Value, request.MaterialIds!, ct)));
    }

    private static async Task<IResult> GetPricesV2Async(FinanceRequestV2 request, AppDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(new PricingResponse(await RatesAsync(db, Table.MaterialPrices, request.StartYear!.Value, request.EndYear!.Value, request.Ids!, ct)));
    }

    private static async Task<IResult> GetOpexAsync(FinanceRequestV2 request, AppDbContext db, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        return TypedResults.Ok(new CropProfileOpexResponse(await RatesAsync(db, Table.CropProfileOpex, request.StartYear!.Value, request.EndYear!.Value, request.Ids!, ct)));
    }

    private enum Table { MaterialPrices, CropProfileOpex }

    /// <summary>For each id and each year: that year's row, or the nearest one when the year is outside the table. Ids without rows are left out.</summary>
    private static async Task<Dictionary<string, AnnualRate[]>> RatesAsync(AppDbContext db, Table table, int startYear, int endYear, string[] requested, CancellationToken ct)
    {
        var ids = requested.Select(id => long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (long?)null)
            .OfType<long>().Distinct().ToArray();
        if (ids.Length == 0) return [];

        // Only these two fixed fragments are ever put into the SQL; the values are parameters.
        var (source, key, value) = table == Table.MaterialPrices
            ? ("public.material_prices", "material_id", "price_cents")
            : ("public.crop_profile_opex", "crop_profile_id", "opex_cents");
        var rates = new Dictionary<string, List<AnnualRate>>();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand($"""
                SELECT m.id, y.year, nearest.cents
                FROM unnest($3) AS m(id)
                CROSS JOIN generate_series($1, $2) AS y(year)
                CROSS JOIN LATERAL (
                    SELECT t.{value} AS cents FROM {source} t
                    WHERE t.{key} = m.id
                    ORDER BY abs(t.year - y.year), t.year
                    LIMIT 1
                ) nearest
                ORDER BY m.id, y.year
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = startYear });
            command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = endYear });
            command.Parameters.Add(new NpgsqlParameter<long[]> { TypedValue = ids });
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture);
                if (!rates.TryGetValue(id, out var list)) rates[id] = list = [];
                list.Add(new AnnualRate(reader.GetInt32(1), reader.GetInt32(2)));
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }

        return rates.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
