using Polle.Api.Features.Agriculture;

namespace Polle.Api.Tests;

public sealed class FieldConditionsTests
{
    private static Dictionary<string, string[]> Errors(string? drought, string? drainage, string? texture)
    {
        var errors = new Dictionary<string, string[]>();
        AgricultureValidation.FieldConditions(new FieldRequest("North", null, DroughtLevel: drought, SoilDrainage: drainage, SoilTexture: texture), errors);
        return errors;
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", " ", null)]
    [InlineData("drought_prone", "well_drained", "silt_loam")]
    [InlineData("low", "poor", "peat")]
    public void Accepts_the_solvers_classes_or_nothing(string? drought, string? drainage, string? texture) =>
        Assert.Empty(Errors(drought, drainage, texture));

    [Fact]
    public void Rejects_anything_else_under_its_own_key()
    {
        var errors = Errors("very dry", "Well drained", "loamy");

        Assert.Equal(["droughtLevel", "soilDrainage", "soilTexture"], errors.Keys.Order());
    }

    private static Dictionary<string, string[]> StartErrors(double? n, double? p, double? k, double? depletion)
    {
        var errors = new Dictionary<string, string[]>();
        AgricultureValidation.FieldConditions(new FieldRequest("North", null, InitialNKg: n, InitialPKg: p, InitialKKg: k, InitialDepletionMm: depletion), errors);
        return errors;
    }

    [Fact]
    public void Accepts_starting_nutrients_and_water_deficit_in_range_or_blank()
    {
        Assert.Empty(StartErrors(null, null, null, null));
        Assert.Empty(StartErrors(0, 45.5, 2000, 1000));
    }

    [Fact]
    public void Rejects_negative_too_large_or_non_finite_starting_values()
    {
        var errors = StartErrors(-1, 2000.1, double.NaN, 1000.5);
        Assert.Equal(["initialDepletionMm", "initialKKg", "initialNKg", "initialPKg"], errors.Keys.Order());
    }
}
