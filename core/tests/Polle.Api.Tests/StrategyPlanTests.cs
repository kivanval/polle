using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

public sealed class StrategyPlanTests
{
    private static StrategyCycle Cycle(long profile, string sown, string cleared) =>
        new(profile, 1, "Crop", "System", sown, cleared, [cleared], 0, 0, 100, 0, [], new CycleNutrients(0, 0, 0, 0, 0, 0, 0), [], [], [], []);

    private static FieldPlanting Planting(DateOnly sown, DateOnly? harvested) =>
        new() { FieldId = Guid.NewGuid(), CropProfileId = 1, SeasonYear = sown.Year, PlantingDate = sown, HarvestingDate = harvested };

    [Fact]
    public void CyclesBecomeWholeMonthPlantings()
    {
        var planting = Assert.Single(StrategyPlan.Plantings([Cycle(7754, "2026-10", "2027-07")]));

        Assert.Equal(7754, planting.CropProfileId);
        Assert.Equal(new DateOnly(2026, 10, 1), planting.Sown);
        Assert.Equal(new DateOnly(2027, 7, 31), planting.Harvested);
        Assert.Equal(2027, planting.SeasonYear);
    }

    [Fact]
    public void CyclesWithDaysKeepTheSolversSowingAndClearanceDays()
    {
        var plantings = StrategyPlan.Plantings([
            Cycle(1, "2027-04", "2027-08") with { PlantingDate = "2027-04-11", ClearanceDate = "2027-08-11" },
            Cycle(2, "2027-08", "2028-07") with { PlantingDate = "2027-08-21", ClearanceDate = "2028-07-01" }]);

        Assert.Equal(new DateOnly(2027, 4, 11), plantings[0].Sown);
        Assert.Equal(new DateOnly(2027, 8, 11), plantings[0].Harvested);
        Assert.Equal(new DateOnly(2027, 8, 21), plantings[1].Sown);
        Assert.Equal(new DateOnly(2028, 7, 1), plantings[1].Harvested);
    }

    [Fact]
    public void ADaySowingBeforeTheEarlierCropsClearanceEndsItTheDayBefore()
    {
        var plantings = StrategyPlan.Plantings([
            Cycle(1, "2027-04", "2027-08") with { PlantingDate = "2027-04-01", ClearanceDate = "2027-08-21" },
            Cycle(2, "2027-08", "2028-07") with { PlantingDate = "2027-08-11", ClearanceDate = "2028-07-01" }]);

        Assert.Equal(new DateOnly(2027, 8, 10), plantings[0].Harvested);
    }

    [Fact]
    public void SameMonthHandOverEndsTheEarlierCropTheDayBefore()
    {
        var plantings = StrategyPlan.Plantings([Cycle(1, "2027-04", "2027-08"), Cycle(2, "2027-08", "2028-07")]);

        Assert.Equal(new DateOnly(2027, 7, 31), plantings[0].Harvested);
        Assert.Equal(new DateOnly(2027, 8, 1), plantings[1].Sown);
    }

    [Fact]
    public void ReplacesOnlyCropsSownFromTheStart()
    {
        var start = new DateOnly(2026, 12, 1);
        var past = Planting(new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30));
        var future = Planting(new DateOnly(2027, 3, 1), null);

        Assert.Equal([future], StrategyPlan.Replaced([past, future], start));
    }

    [Fact]
    public void ACropStillGrowingIntoThePlanBlocksIt()
    {
        var start = new DateOnly(2026, 12, 1);
        var open = Planting(new DateOnly(2026, 10, 1), null);
        var closedLate = Planting(new DateOnly(2026, 9, 1), new DateOnly(2027, 5, 1));
        var closedEarly = Planting(new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30));
        var first = new DateOnly(2027, 3, 1);

        Assert.Same(open, StrategyPlan.Blocking([closedEarly, open], start, first));
        Assert.Same(closedLate, StrategyPlan.Blocking([closedLate], start, first));
        Assert.Null(StrategyPlan.Blocking([closedEarly], start, first));
    }

    [Fact]
    public void AStrategysWaterAndFertilizerGoIntoThePlan()
    {
        var cycle = Cycle(7754, "2027-04", "2027-08") with { IrrigationMm = 120.04, Fertilizer = new CycleFertilizer("2274", "Nitrophoska", [], 250) };

        var planting = Assert.Single(StrategyPlan.Plantings([cycle, Cycle(7755, "2028-04", "2028-08")]).Take(1));

        Assert.Equal(120, planting.IrrigationMm);
        Assert.Equal(2274, planting.FertilizerMaterialId);
        Assert.Equal(250, planting.FertilizerRateKgHa);
        Assert.Null(StrategyPlan.Plantings([Cycle(7755, "2028-04", "2028-08")])[0].FertilizerMaterialId);
    }
}
