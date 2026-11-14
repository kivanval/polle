using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Polle.Api.Common;
using Polle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Fields;

public sealed record FieldTopology(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("area_ha")] double AreaHa,
    [property: JsonPropertyName("initial_depletion_mm")] double? InitialDepletionMm,
    [property: JsonPropertyName("soil")] SoilProperties? Soil,
    [property: JsonPropertyName("historical_variants")] HistoricalVariant[] HistoricalVariants);

/// <summary>
/// A crop the field grew before the requested month. felder matches <c>crop_id</c> against its rotation rules, which are
/// keyed by crop, so the profile id is deliberately not sent (felder would prefer it over the crop id).
/// </summary>
public sealed record HistoricalVariant(
    [property: JsonPropertyName("crop_id")] string CropId,
    [property: JsonPropertyName("plant_date")] DateOnly PlantDate,
    [property: JsonPropertyName("harvest_date")] DateOnly HarvestDate);

/// <summary>
/// The Field Topology API the solver (felder) calls, as in its api/clients/field_topology/openapi.yml. The soil is the
/// deterministic synthetic soil of <see cref="FieldSoil"/> with the farmer's texture and drainage, the same every month,
/// and null for a field that is not on land. Starting N/P/K the farmer entered replace the estimated pools.
/// <c>initial_depletion_mm</c> is the farmer's figure, else follows the drought level
/// (<see cref="FieldSoil.InitialDepletionMm"/>), and is null without either. <c>historical_variants</c> (read by felder, outside the
/// published contract) lists the crops harvested on the field, or on the field a subfield belongs to, before the month.
/// </summary>
public sealed partial class FieldTopologyEndpoints : IEndpoint
{
    public const string ApiKeyHeader = "X-Fields-Api-Key";
    public const string ApiKeySetting = "Fields:ApiKey";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/fields/{field_id}/{year_month}", GetAsync)
            .WithMetadata(new SolverServiceKey(ApiKeyHeader, ApiKeySetting))
            .WithTags("Field Topology").AllowAnonymous().RequireRateLimiting("fields")
            .Produces<FieldTopology>().ProducesValidationProblem().ProducesProblem(404).ProducesProblem(429)
            .WithSummary("A field's name, area, soil (synthetic, with the farmer's texture and drainage) and earlier crops for the solver. A field not on land has no soil; initial depletion follows the farmer's drought level");
    }

    private static async Task<IResult> GetAsync([FromRoute(Name = "field_id")] string fieldId,
        [FromRoute(Name = "year_month")] string yearMonth, AppDbContext db, CancellationToken ct)
    {
        if (!IsYearMonth(yearMonth))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["year_month"] = ["Provide year_month as YYYY-MM."]
            });
        if (!Guid.TryParse(fieldId, out var id)) return TypedResults.NotFound();

        var field = await db.Fields.AsNoTracking().Where(item => item.Id == id)
            .Select(item => new { item.Id, item.Name, item.AreaHectares, item.Boundary, item.ParentFieldId, item.SoilTexture, item.SoilDrainage, item.DroughtLevel,
                item.InitialNKg, item.InitialPKg, item.InitialKKg, item.InitialDepletionMm })
            .SingleOrDefaultAsync(ct);
        if (field is null) return TypedResults.NotFound();
        // The solver requires a positive area, and JSON cannot carry NaN or infinity.
        if (!double.IsFinite(field.AreaHectares) || field.AreaHectares <= 0)
            return TypedResults.Problem(statusCode: 404, title: "Field not found",
                detail: "The field has no valid computed area in hectares.");

        var before = DateOnly.ParseExact($"{yearMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var history = await db.FieldPlantings.AsNoTracking()
            .Where(planting => (planting.FieldId == field.Id || planting.FieldId == field.ParentFieldId)
                && planting.HarvestingDate != null && planting.HarvestingDate < before)
            .OrderBy(planting => planting.PlantingDate)
            .Select(planting => new { planting.CropProfile.CropId, planting.PlantingDate, Harvest = planting.HarvestingDate!.Value })
            .ToListAsync(ct);

        var soil = FieldSoil.For(field.Id, field.Boundary, LandMask.Default, new FarmerSoil(field.SoilTexture, field.SoilDrainage, field.DroughtLevel,
            field.InitialNKg, field.InitialPKg, field.InitialKKg));
        return TypedResults.Ok(new FieldTopology(field.Id.ToString(), field.Name, field.AreaHectares,
            FieldSoil.InitialDepletionMm(soil, field.DroughtLevel, field.InitialDepletionMm), soil,
            [.. history.Select(item => new HistoricalVariant(item.CropId.ToString(CultureInfo.InvariantCulture), item.PlantingDate, item.Harvest))]));
    }

    /// <summary>
    /// A placeholder soil salinity in dS/m until real soil data exists: derived from the field id, so it never changes.
    /// About 70% of fields are non-saline (0.3-1.5), 20% slightly saline (1.5-4) and 10% saline (4-8). Always a value:
    /// felder's topology contract made salinity mandatory and rejects a field without it.
    /// </summary>
    public static double SyntheticSalinity(Guid fieldId)
    {
        var hash = SHA256.HashData(fieldId.ToByteArray());
        double Unit(int offset) => BitConverter.ToUInt32(hash, offset) / 4294967296.0;
        var (band, position) = (Unit(4), Unit(8));
        var value = band < 0.7 ? 0.3 + 1.2 * position : band < 0.9 ? 1.5 + 2.5 * position : 4.0 + 4.0 * position;
        return Math.Round(value, 1);
    }

    internal static bool IsYearMonth(string? value) => value is not null && YearMonth().IsMatch(value);

    // ASCII digits only and \z, not $, so a trailing newline (%0A) does not match.
    [GeneratedRegex(@"^[0-9]{4}-(0[1-9]|1[0-2])\z", RegexOptions.CultureInvariant)]
    private static partial Regex YearMonth();
}
