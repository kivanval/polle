using Polle.Api.Features.Agriculture;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

public sealed class PlantingContractTests
{
    private static PlantingRequest Request(long? profileId) => new(profileId, 2027, new DateOnly(2027, 4, 10));

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-3L)]
    public void PlantingValidationRequiresProfile(long? profileId)
    {
        var errors = AgricultureValidation.Planting(Request(profileId));

        Assert.Contains("cropProfileId", errors.Keys);
    }

    [Fact]
    public void PlantingValidationAcceptsAProfile()
    {
        Assert.Empty(AgricultureValidation.Planting(Request(7734)));
    }

    [Fact]
    public void CropResponseUsesEnglishNameAndKeepsProfileOrder()
    {
        var crop = new Crop
        {
            Id = 7726, Name = "Wheat", NameUk = "Common wheat (uk)", ScientificName = "Triticum aestivum",
            Profiles = [new() { Id = 10, CropId = 7726, SystemName = "Winter Grain Harvest" },
                        new() { Id = 11, CropId = 7726, SystemName = "Spring Grain Harvest", IsPerennial = false }]
        };

        var response = CropResponse.From(crop);

        Assert.Equal("Wheat", response.Name);
        Assert.Equal("Common wheat (uk)", response.NameUk);
        Assert.Equal([10L, 11L], response.Profiles.Select(profile => profile.Id));
        Assert.Equal("Winter Grain Harvest", response.Profiles[0].SystemName);
    }

    [Fact]
    public void PlantingResponseCarriesProfileAndCrop()
    {
        var planting = new FieldPlanting
        {
            FieldId = Guid.NewGuid(), CropProfileId = 10, SeasonYear = 2027, PlantingDate = new DateOnly(2027, 4, 10),
            CropProfile = new CropProfile { Id = 10, CropId = 7726, SystemName = "Winter Grain Harvest" }
        };

        var response = PlantingResponse.From(planting, new DateOnly(2026, 11, 8));

        Assert.Equal(10, response.CropProfileId);
        Assert.Equal(7726, response.CropId);
        Assert.Equal("planned", response.Status);
    }

    [Fact]
    public void PlantingValidationAcceptsWaterAndAFertilizerWithItsRate()
    {
        Assert.Empty(AgricultureValidation.Planting(Request(7734) with { IrrigationMm = 120, FertilizerMaterialId = 2274, FertilizerRateKgHa = 250 }));
    }

    [Fact]
    public void PlantingValidationNeedsAFertilizerAndItsRateTogetherAndSaneAmounts()
    {
        Assert.Contains("fertilizerRateKgHa", AgricultureValidation.Planting(Request(7734) with { FertilizerMaterialId = 2274 }).Keys);
        Assert.Contains("fertilizerMaterialId", AgricultureValidation.Planting(Request(7734) with { FertilizerRateKgHa = 250 }).Keys);
        Assert.Contains("fertilizerRateKgHa", AgricultureValidation.Planting(Request(7734) with { FertilizerMaterialId = 2274, FertilizerRateKgHa = 0 }).Keys);
        Assert.Contains("irrigationMm", AgricultureValidation.Planting(Request(7734) with { IrrigationMm = -5 }).Keys);
    }
}
