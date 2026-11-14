using Polle.Api.Features.Climate;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

/// <summary>
/// "Your current plan" always gets a full audit (summary, agronomic risks, monthly water balance and N-P-K per crop) from
/// the farmer's crops and the field's settings, even when the solver leaves it unscored.
/// </summary>
public sealed class PlanAuditTests
{
    private const long Wheat = 1, Sunflower = 2, Peas = 3;
    private const long WheatGrain = 10, WheatSeed = 11, SunflowerSeedMaterial = 12, SunflowerGrain = 13;

    private static readonly AuditProfile WheatProfile = new(101, Wheat, "Wheat", "Winter Grain Harvest", false, 6.0, 200, WheatGrain, WheatSeed,
        (25, 11, 18), 0,
    [
        new("planting", 5, 30, 0.4, 0.2, -6, 32), new("dormancy", 60, 200, 0.4, 0.2, -25, 25),
        new("regrowth", 150, 260, 1.0, 0.6, -8, 32), new("harvest", 250, 330, 0.7, 0.5, 0, 36),
    ]);

    private static readonly AuditProfile SunflowerProfile = new(102, Sunflower, "Sunflower", "Spring Oilseed Harvest", false, 2.5, 6, SunflowerGrain,
        SunflowerSeedMaterial, (45, 18, 90), 0,
    [
        new("planting", 10, 20, 0.35, 0.4, 2, 35), new("vegetative", 40, 60, 0.8, 0.8, 0, 38), new("harvest", 110, 130, 1.0, 0.8, 0, 40),
    ]);

    private static readonly AuditProfile PeasProfile = new(103, Peas, "Peas", "Spring Grain Harvest", false, 3.0, 220, null, null, (60, 15, 30), 80,
        [new("planting", 10, 20, 0.5, 0.6, -4, 30), new("harvest", 90, 110, 1.0, 0.8, 0, 35)]);

    /// <summary>A continental field: cold winters, hot and dry summers.</summary>
    private static AuditMonthClimate[] Continental() =>
    [
        new(35, 10, -8, -1), new(30, 15, -8, 0), new(30, 35, -3, 6), new(40, 70, 4, 15), new(50, 110, 10, 22), new(55, 140, 14, 26),
        new(40, 160, 16, 29), new(30, 140, 15, 28), new(35, 90, 10, 22), new(30, 50, 5, 14), new(35, 20, 0, 6), new(35, 10, -5, 1),
    ];

    private static PlanAuditInput Input(IReadOnlyList<AuditPlanting> plantings, AuditField? field = null, IReadOnlyList<AuditMonthClimate>? climate = null,
        Dictionary<(long, long), int>? rotation = null) => new("2027-01", "2031-12",
        field ?? new AuditField(10, FieldConditions.DroughtProne, 40, 20, 100, null), plantings,
        new Dictionary<long, AuditProfile> { [101] = WheatProfile, [102] = SunflowerProfile, [103] = PeasProfile },
        new Dictionary<long, IReadOnlyDictionary<int, long>>
        {
            [WheatGrain] = new Dictionary<int, long> { [2027] = 20_000, [2028] = 21_000 },
            [WheatSeed] = new Dictionary<int, long> { [2027] = 40_000 },
            [SunflowerGrain] = new Dictionary<int, long> { [2028] = 45_000 },
            [SunflowerSeedMaterial] = new Dictionary<int, long> { [2028] = 600_000 },
        },
        new Dictionary<long, IReadOnlyDictionary<int, long>> { [101] = new Dictionary<int, long> { [2028] = 30_000 } },
        rotation ?? new Dictionary<(long, long), int> { [(Wheat, Wheat)] = 24, [(Sunflower, Sunflower)] = 72 },
        climate ?? Continental());

    [Fact]
    public void TheCurrentPlanGetsASummaryRisksWaterByMonthAndNutrientsForEachCrop()
    {
        var result = PlanAudit.Build(Input([
            new(101, new DateOnly(2027, 10, 1), null),
            new(102, new DateOnly(2028, 4, 20), null),
        ]))!;

        Assert.Equal(PlanAudit.EstimatedStatus, result.Status);
        Assert.Equal(["Wheat", "Sunflower"], result.Cycles.Select(cycle => cycle.CropName));
        // Strategy summary: money over the whole 10 ha field from the catalog's yield, prices, seed and OpEx.
        var wheat = result.Cycles[0];
        Assert.Equal("2027-10", wheat.PlantingMonth);
        Assert.True(wheat.RevenueUsd > 0);
        Assert.Equal(3000m, wheat.CostBreakdown!.OperatingUsd); // $300/ha × 10 ha
        Assert.Equal(800m, wheat.CostBreakdown.PlantingUsd); // 200 kg/ha × 10 ha = 2 t of seed at $400/t
        Assert.Equal(result.RevenueUsd - result.CostsUsd, result.ProfitUsd);
        // Water balance by month: every month a crop is on the field, with what it needs.
        Assert.All(result.Cycles, cycle => Assert.NotEmpty(cycle.MonthlyWater));
        Assert.Equal(wheat.PlantingMonth, wheat.MonthlyWater[0].Month);
        Assert.Equal(wheat.ClearanceMonth, wheat.MonthlyWater[^1].Month);
        Assert.Contains(wheat.MonthlyWater, month => month.WaterUseMm > 0);
        Assert.NotNull(result.WaterUseMm);
        // Nutrients per crop: what the harvest takes off, with the fertilizer it would need.
        Assert.All(result.Cycles, cycle => Assert.True(cycle.Nutrients.NitrogenRemovedKgHa > 0 && cycle.Nutrients.PhosphorusRemovedKgHa > 0
            && cycle.Nutrients.PotassiumRemovedKgHa > 0));
        Assert.All(result.Cycles, cycle => Assert.Contains(cycle.Notes, note => note.StartsWith("Fertilizer need: the plan adds no fertilizer.")));
        // Agronomic risks: the dry summer hits the sunflower.
        Assert.Contains(result.Cycles[1].Risks, risk => risk.StartsWith("Drought"));
        Assert.Contains(result.Risks, risk => risk.StartsWith("Drought"));
    }

    [Fact]
    public void ADroughtCutsTheYieldAndARainyFieldDoesNot()
    {
        var plan = new AuditPlanting[] { new(102, new DateOnly(2028, 4, 20), null) };
        var dry = PlanAudit.Build(Input(plan))!.Cycles[0];
        var wet = PlanAudit.Build(Input(plan, new AuditField(10, FieldConditions.DroughtLow, null, null, null, 0),
            Continental().Select(month => month with { RainMm = 250 }).ToArray()))!.Cycles[0];

        Assert.True(dry.YieldPercent < 100);
        Assert.Equal(100, wet.YieldPercent);
        Assert.DoesNotContain(wet.Risks, risk => risk.StartsWith("Drought"));
        Assert.True(wet.RevenueUsd > dry.RevenueUsd);
    }

    [Fact]
    public void TheFertilizerNoteUsesTheBalanceUnitsOnlyAndSaysWhereTheFiguresComeFrom()
    {
        var wheat = PlanAudit.Build(Input([new(101, new DateOnly(2027, 10, 1), null)]))!.Cycles[0];
        var note = Assert.Single(wheat.NoteDetails, detail => detail.Category == "nutrients");

        // The same pure N, P and K kg/ha as the crop's N-P-K balance; no second set of oxide figures beside them.
        Assert.Contains($"{wheat.Nutrients.NitrogenRemovedKgHa:0} kg N, {wheat.Nutrients.PhosphorusRemovedKgHa:0} kg P and {wheat.Nutrients.PotassiumRemovedKgHa:0} kg K", note.Message);
        Assert.DoesNotContain("P₂O₅", note.Message);
        Assert.DoesNotContain("K₂O", note.Message);
        Assert.Contains("t/ha", note.Message);
        Assert.Contains("crop catalog's removal per tonne", note.Message);
        Assert.Equal(StrategyResultMapper.Info, note.Importance);
    }

    [Fact]
    public void ALegumesFertilizerNeedCountsTheNitrogenItFixes()
    {
        var peas = PlanAudit.Build(Input([new(103, new DateOnly(2028, 4, 1), null)]))!.Cycles[0];
        var note = Assert.Single(peas.Notes, text => text.StartsWith("Fertilizer need"));

        Assert.Contains("(after the 80 kg N it fixes from the air)", note);
    }

    [Fact]
    public void TheDroughtNoteSaysWhereTheNeedAndTheRainComeFrom()
    {
        var sunflower = PlanAudit.Build(Input([new(102, new DateOnly(2028, 4, 20), null)]))!.Cycles[0];
        var drought = Assert.Single(sunflower.Risks, risk => risk.StartsWith("Drought:"));

        Assert.Contains("the Sunflower needs about", drought);
        Assert.Contains("the field's ten-year average weather", drought);
        Assert.Contains("rain plus the water stored in the soil covers about", drought);
        Assert.Contains("The plan has no irrigation", drought);
    }

    [Fact]
    public void ACropTooSoonAfterItselfIsARotationRisk()
    {
        var result = PlanAudit.Build(Input([
            new(101, new DateOnly(2027, 10, 1), new DateOnly(2028, 7, 20)),
            new(101, new DateOnly(2028, 9, 25), null),
        ]))!;

        Assert.DoesNotContain(result.Cycles[0].Risks, risk => risk.StartsWith("Crop rotation"));
        Assert.Contains(result.Cycles[1].Risks, risk => risk.StartsWith("Crop rotation: Wheat comes back after 2 months"));
    }

    [Fact]
    public void AnEarlierPlantingBeforeTheHorizonStillCountsForRotation()
    {
        var result = PlanAudit.Build(Input([
            new(102, new DateOnly(2026, 4, 20), new DateOnly(2026, 9, 10)),
            new(102, new DateOnly(2028, 4, 20), null),
        ]))!;

        Assert.Single(result.Cycles);
        Assert.Contains(result.Cycles[0].Risks, risk => risk.StartsWith("Crop rotation: Sunflower comes back after 19 months"));
    }

    [Fact]
    public void SowingInFrostIsAWrongMonthRisk()
    {
        var result = PlanAudit.Build(Input([new(102, new DateOnly(2028, 1, 10), null)]))!;

        Assert.Contains(result.Cycles[0].Risks, risk => risk.StartsWith("Sowing month"));
    }

    [Fact]
    public void HarvestsThatOutrunTheSoilTestAreSoilDepletion()
    {
        var result = PlanAudit.Build(Input([new(101, new DateOnly(2027, 10, 1), null), new(102, new DateOnly(2028, 9, 1), null)],
            new AuditField(10, FieldConditions.DroughtLow, 30, 5, 400, 0)))!;

        Assert.Contains(result.Cycles.SelectMany(cycle => cycle.NoteDetails), note => note.Category == "soil_health" && note.Message.Contains("nitrogen"));
        Assert.Contains(result.Cycles.SelectMany(cycle => cycle.NoteDetails), note => note.Category == "soil_health" && note.Message.Contains("phosphorus"));
        Assert.DoesNotContain(result.Cycles.SelectMany(cycle => cycle.NoteDetails), note => note.Category == "soil_health" && note.Message.Contains("potassium"));
    }

    [Fact]
    public void LegumesFixNitrogenAndMissingFiguresAreWarnings()
    {
        var result = PlanAudit.Build(Input([new(103, new DateOnly(2028, 4, 1), null)]))!;
        var peas = result.Cycles[0];

        Assert.Equal(80, peas.Nutrients.NitrogenFixedKgHa);
        Assert.Contains(peas.Warnings, warning => warning.Contains("no product to sell"));
        Assert.Equal(0, peas.RevenueUsd);
    }

    [Fact]
    public void WithoutClimateTheAuditStillAnswersAndSaysWhy()
    {
        var result = PlanAudit.Build(Input([new(101, new DateOnly(2027, 10, 1), null)]) with { Climate = null })!;

        Assert.Single(result.Cycles);
        Assert.Empty(result.Cycles[0].MonthlyWater);
        Assert.Null(result.WaterUseMm);
        Assert.Contains(result.DataGaps, gap => gap.Contains("climate"));
        Assert.True(result.Cycles[0].Nutrients.NitrogenRemovedKgHa > 0);
    }

    [Fact]
    public void TheAuditKeepsTheFarmersSowingAndHarvestDays()
    {
        var result = PlanAudit.Build(Input([new(101, new DateOnly(2027, 10, 11), new DateOnly(2028, 7, 21)), new(102, new DateOnly(2029, 4, 21), null)]))!;

        Assert.Equal(("2027-10-11", "2028-07-21"), (result.Cycles[0].PlantingDate, result.Cycles[0].ClearanceDate));
        Assert.Equal("2027-10", result.Cycles[0].PlantingMonth);
        // Still growing: the clearance is the audit's estimate, so only its month is given.
        Assert.Equal(("2029-04-21", (string?)null), (result.Cycles[1].PlantingDate, result.Cycles[1].ClearanceDate));
    }

    [Fact]
    public void NothingSownInTheHorizonHasNoAudit() =>
        Assert.Null(PlanAudit.Build(Input([new(101, new DateOnly(2026, 10, 1), new DateOnly(2027, 7, 1))])));

    [Fact]
    public void HargreavesGivesSummerMoreEvaporationThanWinter()
    {
        var summer = PlanAudit.Hargreaves(new DailyClimateNormal(15, 29, 22, 1), 50, 196);
        var winter = PlanAudit.Hargreaves(new DailyClimateNormal(-6, 1, -2, 1), 50, 15);

        Assert.InRange(summer, 3.5, 7);
        Assert.InRange(winter, 0, 1);
    }

    private static readonly AuditPlanting[] ThreeCrops =
    [
        new(101, new DateOnly(2027, 10, 1), null), new(102, new DateOnly(2028, 9, 20), null), new(103, new DateOnly(2029, 4, 1), null),
    ];

    /// <summary>The solver's evaluation of the plan as it comes back with its own figures: no audit notes on its crops.</summary>
    private static StrategyResult Scored(params StrategyCycle[] cycles) => new("FEASIBLE", 5000m, 9000m, 4000m, 0, -10,
        [.. cycles.Select(cycle => cycle with { NoteDetails = [], Risks = [], Warnings = [], Notes = [] })], [], [], [], 120, null, null);

    [Fact]
    public void ACropTheSolverLeftOutOfTheEvaluationIsFilledInFromTheAuditWithAWarning()
    {
        var audit = PlanAudit.Build(Input(ThreeCrops))!;
        Assert.Equal(3, audit.Cycles.Length);
        var scored = Scored(audit.Cycles[0], audit.Cycles[1]);

        var result = PlanAudit.Complete(scored, audit);

        Assert.Equal("FEASIBLE", result.Status);
        Assert.Equal(["Wheat", "Sunflower", "Peas"], result.Cycles.Select(cycle => cycle.CropName));
        Assert.Equal([false, false, true], result.Cycles.Select(cycle => cycle.Estimated));
        var peas = result.Cycles[2];
        Assert.Contains(peas.NoteDetails, note => note is { Category: PlanAudit.UnscoredCategory, Importance: StrategyResultMapper.Warning });
        // The farmer's own sowing day, not just its month.
        Assert.StartsWith("Estimated: the planner couldn't score the Peas sown on 1 Apr 2029", peas.Warnings[0]);
        Assert.Equal("2029-04-01", peas.PlantingDate);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Estimated: the planner couldn't score the Peas"));
        // The audited crop's own findings come along; the solver's crops keep theirs (none here).
        Assert.Equal(audit.Cycles[2].Risks, peas.Risks);
        Assert.Empty(result.Cycles[0].NoteDetails);
        // Totals add the estimated crop to the solver's.
        Assert.Equal(9000m + peas.RevenueUsd, result.RevenueUsd);
        Assert.Equal(4000m + peas.CostsUsd, result.CostsUsd);
        Assert.Equal(5000m + peas.RevenueUsd - peas.CostsUsd, result.ProfitUsd);
        Assert.Equal(Math.Round(120 + peas.MonthlyWater.Sum(month => month.WaterUseMm), 1), result.WaterUseMm);
    }

    [Fact]
    public void AnEvaluationWithEveryCropComesBackUnchanged()
    {
        var audit = PlanAudit.Build(Input(ThreeCrops))!;
        // The solver may place a crop in another month than entered: it still counts as that planting.
        var scored = Scored(audit.Cycles[0], audit.Cycles[1] with { PlantingMonth = "2028-10" }, audit.Cycles[2]);

        Assert.Same(scored, PlanAudit.Complete(scored, audit));
    }

    [Theory]
    [InlineData(StrategyJobs.Failed, false, true)]
    [InlineData(StrategyJobs.Ready, false, true)]
    [InlineData(StrategyJobs.Ready, true, false)]
    [InlineData(StrategyJobs.Processing, false, false)]
    public void OnlyAnUnscoredCurrentPlanIsAudited(string state, bool withCrops, bool audited)
    {
        var result = withCrops ? PlanAudit.Build(Input([new(101, new DateOnly(2027, 10, 1), null)])) : null;
        var job = new StrategyJobResponse(StrategyCatalog.BaselineKey, "Your current plan", "", state,
            result ?? (state == StrategyJobs.Ready ? new StrategyResult("INFEASIBLE", null, 0, 0, 0, 0, [], [], [], [], null) : null), null);

        Assert.Equal(audited, StrategyRunEndpoints.NeedsAudit(job));
    }

    [Fact]
    public void IrrigationAndFertilizerInThePlanCountInTheAudit()
    {
        var sown = new DateOnly(2028, 4, 20);
        var dry = PlanAudit.Build(Input([new(102, sown, null)]))!.Cycles[0];
        var fed = PlanAudit.Build(Input([new(102, sown, null, 300, new AuditFertilizer(2274, 300, 16.5, 16.5, 16.5, "Nitrophoska"))]))!.Cycles[0];

        Assert.True(fed.YieldPercent > dry.YieldPercent);
        Assert.Equal(300, fed.IrrigationMm);
        Assert.Equal(300, fed.MonthlyWater.Sum(month => month.IrrigationMm), 1);
        Assert.True(fed.Nutrients.NitrogenAppliedKgHa > 0);
        Assert.Equal("Nitrophoska", fed.Fertilizer?.Name);
        Assert.Contains(fed.Notes, note => note.StartsWith("Fertilizer: the plan's 300 kg/ha"));
    }

    [Fact]
    public void ACropTheSolverSentWithoutWaterByMonthTakesTheAuditsWater()
    {
        var audit = PlanAudit.Build(Input(ThreeCrops))!;
        var scored = Scored(audit.Cycles[0] with { MonthlyWater = [] }, audit.Cycles[1], audit.Cycles[2]);

        var result = PlanAudit.Complete(scored, audit);

        var wheat = result.Cycles[0];
        Assert.Equal(audit.Cycles[0].MonthlyWater.Select(month => month.WaterUseMm), wheat.MonthlyWater.Select(month => month.WaterUseMm));
        Assert.Contains(wheat.Notes, note => note.StartsWith("Water by month is estimated"));
        Assert.False(wheat.Estimated);
        Assert.Empty(result.Cycles[1].NoteDetails);
    }
}
