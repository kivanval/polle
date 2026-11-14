using System.Text.Json;
using Polle.Api.Features.Fields;
using NetTopologySuite.Geometries;

namespace Polle.Api.Tests;

public sealed class FieldSoilTests
{
    private static readonly GeometryFactory Wgs84 = new(new PrecisionModel(), 4326);
    private static readonly string[] Textures = ["sand", "loamy_sand", "sandy_loam", "loam", "silt_loam", "clay_loam", "clay", "peat"];
    private static readonly string[] Drainage = ["well_drained", "moderate", "poor"];

    /// <summary>A square about <paramref name="size"/> degrees wide centred on the point.</summary>
    private static Polygon Square(double longitude, double latitude, double size = 0.01) => Wgs84.CreatePolygon(
    [
        new(longitude - size / 2, latitude - size / 2), new(longitude + size / 2, latitude - size / 2),
        new(longitude + size / 2, latitude + size / 2), new(longitude - size / 2, latitude + size / 2),
        new(longitude - size / 2, latitude - size / 2),
    ]);

    [Theory]
    [InlineData(30.52, 50.45)]   // Kyiv region
    [InlineData(26.98, 49.47)]   // Khmelnytskyi oblast (the team's test farm)
    [InlineData(-93.6, 41.6)]    // Iowa
    [InlineData(30.70, 46.50)]   // Odesa, on the shore
    public void A_field_on_land_gets_a_complete_valid_soil(double longitude, double latitude)
    {
        var soil = FieldSoil.For(Guid.NewGuid(), Square(longitude, latitude), LandMask.Default);

        Assert.NotNull(soil);
        Assert.Contains(soil.Texture, Textures);
        Assert.Contains(soil.Drainage, Drainage);
        Assert.InRange(soil.Ph, 4.0, 8.2);
        Assert.True(soil.Hydraulics.MaxSoilDepthM > 0);
        Assert.True(soil.Hydraulics.FieldCapacityVol > soil.Hydraulics.WiltingPointVol);
        Assert.True(soil.Hydraulics.WiltingPointVol > 0);
        foreach (var pool in new[] { soil.Nutrients.Nitrogen, soil.Nutrients.Phosphorus, soil.Nutrients.Potassium })
        {
            Assert.InRange(pool.MinKgHa, 0, pool.MaxKgHa);
            Assert.InRange(pool.InitialKgHa, pool.MinKgHa, pool.MaxKgHa);
        }
    }

    [Theory]
    [InlineData(31.5, 43.5)]    // Black Sea, the "field in the sea" Tania found
    [InlineData(-30.0, 40.0)]   // mid-Atlantic
    public void A_field_in_the_sea_has_no_soil(double longitude, double latitude)
    {
        var field = Square(longitude, latitude, 0.1);

        Assert.Null(FieldSoil.For(Guid.NewGuid(), field, LandMask.Default));
        Assert.False(FieldSoil.Available(field, LandMask.Default));
    }

    [Fact]
    public void A_shore_field_with_its_middle_in_the_sea_counts_as_land_when_a_corner_is_on_land()
    {
        // Odesa coast: the middle (30.81, 46.48) is offshore on the 1:50m coastline, the western corners are on land.
        var field = Wgs84.CreatePolygon([new(30.72, 46.47), new(30.90, 46.47), new(30.90, 46.49), new(30.72, 46.49), new(30.72, 46.47)]);

        Assert.False(LandMask.Default.Contains(field.InteriorPoint.X, field.InteriorPoint.Y));
        Assert.True(FieldSoil.Available(field, LandMask.Default));
    }

    [Fact]
    public void A_boundary_that_is_not_wgs84_has_no_soil()
    {
        var field = Square(30.52, 50.45);
        field.SRID = 3857;

        Assert.Null(FieldSoil.For(Guid.NewGuid(), field, LandMask.Default));
        Assert.False(FieldSoil.Available(null, LandMask.Default));
    }

    [Fact]
    public void The_soil_is_deterministic_and_fields_in_one_place_share_the_site()
    {
        var id = Guid.NewGuid();
        var first = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default)!;
        var again = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default)!;
        var neighbour = FieldSoil.For(Guid.NewGuid(), Square(30.522, 50.452), LandMask.Default)!;

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(again));
        Assert.Equal((first.Texture, first.Drainage, first.Ph, first.Hydraulics), (neighbour.Texture, neighbour.Drainage, neighbour.Ph, neighbour.Hydraulics));
    }

    [Fact]
    public void The_farmers_texture_and_drainage_replace_the_estimate_and_the_hydraulics_follow_the_texture()
    {
        var id = Guid.NewGuid();
        var estimated = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default)!;
        var texture = estimated.Texture == "clay" ? "sand" : "clay";

        var farmer = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default, new FarmerSoil(texture, "poor", null))!;

        Assert.Equal((texture, "poor"), (farmer.Texture, farmer.Drainage));
        Assert.NotEqual(estimated.Hydraulics.FieldCapacityVol, farmer.Hydraulics.FieldCapacityVol);
        Assert.Equal((estimated.Ph, estimated.SalinityDsM, estimated.Nutrients), (farmer.Ph, farmer.SalinityDsM, farmer.Nutrients));
    }

    [Theory]
    [InlineData("low", 0.0)]
    [InlineData("moderate", 63.0)]
    [InlineData("drought_prone", 117.0)]
    [InlineData(null, null)]
    public void The_drought_level_sets_the_starting_depletion_as_a_share_of_the_available_water(string? level, double? expected)
    {
        // 1000 × (0.27 − 0.12) × 1.2 m = 180 mm available.
        var soil = new SoilProperties("loam", "moderate", 6.5, 0.8, new SoilHydraulics(1.2, 0.27, 0.12),
            new SoilNutrients(new(120, 20, 250), new(40, 10, 150), new(200, 60, 600)));

        Assert.Equal(180, soil.Hydraulics.TotalAvailableWaterMm);
        Assert.Equal(expected, FieldSoil.InitialDepletionMm(soil, level));
    }

    [Theory]
    [InlineData(25.04, "drought_prone", 25.0)]
    [InlineData(400.0, null, 180.0)]
    public void The_farmers_water_deficit_wins_over_the_drought_level_and_is_capped_at_the_available_water(double given, string? level, double expected)
    {
        var soil = new SoilProperties("loam", "moderate", 6.5, 0.8, new SoilHydraulics(1.2, 0.27, 0.12),
            new SoilNutrients(new(120, 20, 250), new(40, 10, 150), new(200, 60, 600)));
        Assert.Equal(expected, FieldSoil.InitialDepletionMm(soil, level, given));
        Assert.Null(FieldSoil.InitialDepletionMm(null, level, given));
    }

    [Fact]
    public void The_farmers_soil_test_replaces_only_the_nutrients_given()
    {
        var id = Guid.Parse("6d8c8f0e-4a5b-4f4e-9c1a-2f7b1e3d9a10");
        var estimate = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default, FarmerSoil.None)!;
        var tested = FieldSoil.For(id, Square(30.521, 50.451), LandMask.Default, new FarmerSoil(null, null, null, NitrogenKg: 900, PotassiumKg: 5))!;

        Assert.Equal(900, tested.Nutrients.Nitrogen.InitialKgHa);
        Assert.True(tested.Nutrients.Nitrogen.MaxKgHa >= 900);
        Assert.Equal(5, tested.Nutrients.Potassium.InitialKgHa);
        Assert.True(tested.Nutrients.Potassium.MinKgHa <= 5);
        Assert.Equal(estimate.Nutrients.Phosphorus, tested.Nutrients.Phosphorus);
    }

    [Fact]
    public void Every_field_gets_a_salinity_because_felder_requires_one()
    {
        Assert.All(Enumerable.Range(0, 200).Select(_ => FieldTopologyEndpoints.SyntheticSalinity(Guid.NewGuid())), value => Assert.InRange(value, 0.3, 8.0));
    }

    [Fact]
    public void The_land_mask_tells_land_from_sea()
    {
        Assert.True(LandMask.Default.Contains(-0.13, 51.51));   // London
        Assert.True(LandMask.Default.Contains(30.52, 50.45));   // Kyiv
        Assert.False(LandMask.Default.Contains(-30.0, 40.0));   // Atlantic
        Assert.False(LandMask.Default.Contains(double.NaN, 0));
    }

    [Fact]
    public void Topology_uses_the_contract_names_with_soil_and_history()
    {
        var soil = new SoilProperties("loam", "moderate", 6.5, 0.8, new SoilHydraulics(1.2, 0.27, 0.12),
            new SoilNutrients(new(120, 20, 250), new(40, 10, 150), new(200, 60, 600)));
        var body = new FieldTopology("f1", "North Valley", 120.5, FieldSoil.InitialDepletionMm(soil, "moderate"), soil,
            [new HistoricalVariant("7721", new(2025, 4, 1), new(2025, 9, 15))]);

        Assert.Equal("""
            {"id":"f1","name":"North Valley","area_ha":120.5,"initial_depletion_mm":63,"soil":{"texture":"loam","drainage":"moderate","ph":6.5,"salinity_ds_m":0.8,"hydraulics":{"max_soil_depth_m":1.2,"field_capacity_vol":0.27,"wilting_point_vol":0.12,"total_available_water_mm":180},"nutrients":{"nitrogen":{"initial_kg_ha":120,"min_kg_ha":20,"max_kg_ha":250},"phosphorus":{"initial_kg_ha":40,"min_kg_ha":10,"max_kg_ha":150},"potassium":{"initial_kg_ha":200,"min_kg_ha":60,"max_kg_ha":600}}},"historical_variants":[{"crop_id":"7721","plant_date":"2025-04-01","harvest_date":"2025-09-15"}]}
            """, JsonSerializer.Serialize(body));
    }
}
