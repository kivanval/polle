using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Polle.Domain.Agriculture;
using NetTopologySuite.Geometries;

namespace Polle.Api.Features.Fields;

/// <summary>The contract's SoilProperties (felder api/clients/field_topology/openapi.yml).</summary>
public sealed record SoilProperties(
    [property: JsonPropertyName("texture")] string Texture,
    [property: JsonPropertyName("drainage")] string Drainage,
    [property: JsonPropertyName("ph")] double Ph,
    [property: JsonPropertyName("salinity_ds_m")] double SalinityDsM,
    [property: JsonPropertyName("hydraulics")] SoilHydraulics Hydraulics,
    [property: JsonPropertyName("nutrients")] SoilNutrients Nutrients);

public sealed record SoilHydraulics(
    [property: JsonPropertyName("max_soil_depth_m")] double MaxSoilDepthM,
    [property: JsonPropertyName("field_capacity_vol")] double FieldCapacityVol,
    [property: JsonPropertyName("wilting_point_vol")] double WiltingPointVol)
{
    /// <summary>Water the root zone holds between field capacity and wilting point: 1000 × (FC − WP) × depth (FAO-56 eq. 82).</summary>
    [JsonPropertyName("total_available_water_mm")]
    public double TotalAvailableWaterMm => Math.Round(1000 * (FieldCapacityVol - WiltingPointVol) * MaxSoilDepthM, 1);
}

/// <summary>What the farmer told us about the field; each null keeps the estimate. Nutrients are starting kg/ha.</summary>
public sealed record FarmerSoil(string? Texture, string? Drainage, string? DroughtLevel,
    double? NitrogenKg = null, double? PhosphorusKg = null, double? PotassiumKg = null)
{
    public static FarmerSoil None { get; } = new(null, null, null);
}

public sealed record SoilNutrients(
    [property: JsonPropertyName("nitrogen")] NutrientPool Nitrogen,
    [property: JsonPropertyName("phosphorus")] NutrientPool Phosphorus,
    [property: JsonPropertyName("potassium")] NutrientPool Potassium);

public sealed record NutrientPool(
    [property: JsonPropertyName("initial_kg_ha")] double InitialKgHa,
    [property: JsonPropertyName("min_kg_ha")] double MinKgHa,
    [property: JsonPropertyName("max_kg_ha")] double MaxKgHa);

/// <summary>
/// Soil of a field for the solver. Polle records no soil measurements, only the farmer's own texture and drainage
/// classes (<see cref="FarmerSoil"/>), so every other value is SYNTHETIC but deterministic:
/// texture, drainage, pH and depth come from the field's ~5 km location cell (neighbouring fields and subfields agree),
/// nutrients and salinity from the field id. Hydraulics follow the texture (FAO-56 Table 19 typical values). A field
/// with no point on land (drawn in the sea) or without a valid WGS84 boundary has no soil, and must not be modelled.
/// </summary>
public static class FieldSoil
{
    public const string MissingMessage = "There is no soil data for this field's location, so it can't be modelled.";

    private const double CellDegrees = 0.05;

    // Texture, share of fields, typical field capacity and wilting point (m3/m3), drainage odds (well, moderate).
    private static readonly (string Texture, double Weight, double Fc, double Wp, double Well, double Moderate)[] Textures =
    [
        ("sand", 0.06, 0.12, 0.045, 0.85, 0.15),
        ("loamy_sand", 0.08, 0.15, 0.065, 0.75, 0.20),
        ("sandy_loam", 0.16, 0.23, 0.11, 0.60, 0.30),
        ("loam", 0.25, 0.25, 0.12, 0.50, 0.40),
        ("silt_loam", 0.20, 0.29, 0.15, 0.40, 0.45),
        ("clay_loam", 0.15, 0.32, 0.19, 0.25, 0.50),
        ("clay", 0.08, 0.36, 0.22, 0.10, 0.45),
        ("peat", 0.02, 0.50, 0.25, 0.05, 0.25),
    ];

    /// <summary>Whether the field can be modelled: a valid WGS84 outline with its interior point or a corner on land.</summary>
    public static bool Available(Polygon? boundary, LandMask land) => LocationOf(boundary, land) is not null;

    /// <summary>
    /// The field's soil, or null when it has none (see <see cref="Available"/>). A texture or drainage the farmer gave
    /// replaces the estimate; the hydraulics then follow the farmer's texture.
    /// </summary>
    public static SoilProperties? For(Guid fieldId, Polygon? boundary, LandMask land, FarmerSoil? farmer = null)
    {
        if (LocationOf(boundary, land) is not { } point) return null;
        farmer ??= FarmerSoil.None;

        var site = Random(string.Create(CultureInfo.InvariantCulture,
            $"cell:{(long)Math.Floor(point.Y / CellDegrees)}:{(long)Math.Floor(point.X / CellDegrees)}"));
        var estimated = Pick(site.Next());
        var texture = Textures.FirstOrDefault(candidate => candidate.Texture == farmer.Texture) is { Texture: not null } given ? given : estimated;
        var (well, moderate) = (texture.Well, texture.Moderate);
        var drainageDraw = site.Next();
        var drainage = farmer.Drainage ?? (drainageDraw < well ? "well_drained" : drainageDraw < well + moderate ? "moderate" : "poor");
        var peat = texture.Texture == "peat";
        var ph = peat ? Between(site.Next(), 4.0, 5.5) : Between(site.Next(), 5.2, 8.2);
        var depth = peat ? Between(site.Next(), 0.5, 1.0) : Between(site.Next(), 0.6, 1.8);
        // Within ±8% of the texture's typical value, keeping field capacity above the wilting point.
        var fc = texture.Fc * Between(site.Next(), 0.92, 1.08);
        var wp = Math.Min(texture.Wp * Between(site.Next(), 0.92, 1.08), fc - 0.03);

        var field = Random($"field:{fieldId:N}");
        // Draw all three even when the farmer gave some, so a given value never shifts the estimate of another nutrient.
        var nutrients = new SoilNutrients(
            Given(Pool(field.Next(), 20, 250, 40, 160), farmer.NitrogenKg),
            Given(Pool(field.Next(), 10, 150, 15, 70), farmer.PhosphorusKg),
            Given(Pool(field.Next(), 60, 600, 100, 350), farmer.PotassiumKg));

        return new SoilProperties(texture.Texture, drainage, Math.Round(ph, 1), FieldTopologyEndpoints.SyntheticSalinity(fieldId),
            new SoilHydraulics(Math.Round(depth, 2), Math.Round(fc, 3), Math.Round(wp, 3)), nutrients);
    }

    /// <summary>Share of the soil's available water already used up when the plan starts, by the farmer's drought level.</summary>
    public static double? DepletionShare(string? droughtLevel) => droughtLevel switch
    {
        FieldConditions.DroughtLow => 0.0,
        FieldConditions.DroughtModerate => 0.35,
        FieldConditions.DroughtProne => 0.65,
        _ => null,
    };

    /// <summary>
    /// Starting root-zone depletion for the solver, in mm (0 = field capacity): the farmer's own figure when given, capped
    /// at the soil's total available water (a root zone cannot be drier than its wilting point), otherwise the drought
    /// level's share of that water. Null when neither is given, which felder records as missing data.
    /// </summary>
    public static double? InitialDepletionMm(SoilProperties? soil, string? droughtLevel, double? givenMm = null)
    {
        if (soil is null) return null;
        var available = soil.Hydraulics.TotalAvailableWaterMm;
        if (givenMm is { } given) return Math.Round(Math.Min(given, available), 1);
        return DepletionShare(droughtLevel) is { } share ? Math.Round(share * available, 1) : null;
    }

    private static Point? LocationOf(Polygon? boundary, LandMask land)
    {
        if (boundary is not { IsEmpty: false, SRID: 4326 } || !boundary.IsValid) return null;
        var interior = boundary.InteriorPoint;
        if (!InRange(interior)) return null;
        if (land.Contains(interior.X, interior.Y)) return interior;
        // The 1:50m coastline is coarse: a shore field counts as on land when one of its corners is.
        return boundary.ExteriorRing.Coordinates.Any(corner => land.Contains(corner.X, corner.Y)) ? interior : null;
    }

    private static bool InRange(Point point) =>
        double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X is >= -180 and <= 180 && point.Y is > -90 and < 90;

    private static (string Texture, double Weight, double Fc, double Wp, double Well, double Moderate) Pick(double draw)
    {
        var total = 0.0;
        foreach (var texture in Textures)
        {
            total += texture.Weight;
            if (draw < total) return texture;
        }
        return Textures[^1];
    }

    private static NutrientPool Pool(double draw, double min, double max, double low, double high) =>
        new(Math.Round(Between(draw, low, high)), min, max);

    private static NutrientPool Given(NutrientPool estimate, double? givenKg) =>
        givenKg is { } given
            ? estimate with { InitialKgHa = Math.Round(given, 1), MinKgHa = Math.Min(estimate.MinKgHa, given), MaxKgHa = Math.Max(estimate.MaxKgHa, given) }
            : estimate;

    private static double Between(double unit, double low, double high) => low + (high - low) * unit;

    /// <summary>A reproducible stream of numbers in [0, 1) from a seed text (SHA-256, eight draws per seed).</summary>
    private static Draws Random(string seed) => new(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    private sealed class Draws(byte[] hash)
    {
        private int offset;

        public double Next()
        {
            if (offset + 4 > hash.Length) throw new InvalidOperationException("Ask a soil seed for at most eight values.");
            var value = BitConverter.ToUInt32(hash, offset) / 4294967296.0;
            offset += 4;
            return value;
        }
    }
}
