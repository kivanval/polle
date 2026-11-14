using Polle.Api.Features.Agriculture;

namespace Polle.Api.Tests;

public sealed class FertilizerCatalogTests
{
    private static FertilizerCatalog.Row Row(long id, string? name, double? n = 10, double? p = 20, double? k = 30) => new(id, name, n, p, k);

    [Fact]
    public void ListsProductsByNameWithTheirLabelContent()
    {
        var list = FertilizerCatalog.ToResponses([Row(7, "urea", 46, 0, 0), Row(3, "Ammonium nitrate", 34.4, 0, 0), Row(9, "NPK 16-16-16", 16, 16, 16)]);

        Assert.Equal(["Ammonium nitrate", "NPK 16-16-16", "urea"], list.Select(item => item.Name));
        Assert.Equal(new FertilizerResponse(7, "urea", 46, 0, 0), list[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AProductWithoutANameIsStillListedUnderItsId(string? name)
    {
        var item = Assert.Single(FertilizerCatalog.ToResponses([Row(2215, name)]));

        Assert.Equal("Fertilizer 2215", item.Name);
    }

    [Fact]
    public void AMissingOrImpossibleNutrientFigureCountsAsNone()
    {
        var item = Assert.Single(FertilizerCatalog.ToResponses([Row(5, "Mix", null, double.NaN, 140)]));

        Assert.Equal((0d, 0d, 0d), (item.NitrogenPercent, item.PhosphatePercent, item.PotashPercent));
    }

    [Fact]
    public void ARepeatedProductIsListedOnce()
    {
        var list = FertilizerCatalog.ToResponses([Row(5, "Mix"), Row(5, "Mix")]);

        Assert.Single(list);
    }
}
