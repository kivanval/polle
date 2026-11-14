using Polle.Api.Features.Strategies;

namespace Polle.Api.Tests;

public sealed class StrategyResultMapperTests
{
    private static readonly CropProfileNames Names = new(new Dictionary<long, CropProfileName>
    {
        [7754] = new(7726, "Wheat", "Winter Grain Harvest"),
        [7784] = new(7751, "Peas", "Spring Grain Harvest"),
    });

    private const string TwoCycles = """
        {"status":"OPTIMAL","expected_profit_cents":20000000,"schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2026-10","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "expected_revenue_cents":18450000,"expenses_cents":4500000,"yield_multiplier":0.85,"irrigation_applied_mm":120.5,
          "nitrogen_applied_kg_ha":45,"nitrogen_fixed_kg_ha":0,"nitrogen_removed_kg_ha":110,"phosphorus_applied_kg_ha":0,"phosphorus_removed_kg_ha":0,
          "risk_notes":["[AGRONOMIC] Heat shock: 4 days > 25.0°C in stage 'flowering'","[DATA] No salinity for 2030"]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-08","harvest_year_months":["2028-08"],
          "expected_revenue_cents":6000000,"expenses_cents":1950000,"yield_multiplier":0.9,"irrigation_applied_mm":40,
          "nitrogen_applied_kg_ha":0,"nitrogen_fixed_kg_ha":80,"nitrogen_removed_kg_ha":30,"phosphorus_applied_kg_ha":0,"phosphorus_removed_kg_ha":0,
          "risk_notes":[]}]}
        """;

    /// <summary>felder's day-precision shape (2026-11-13): whole dates, sowing on the 1st, 11th or 21st.</summary>
    private const string DayDates = """
        {"status":"OPTIMAL","schedule":[
         {"crop_profile_id":"7754","plant_date":"2026-10-11","clearance_date":"2027-07-21","harvest_dates":["2027-07-11","2027-07-21"],
          "yield_multiplier":1,"notes":[],
          "financials":{"gross_revenue_cents":100000,"base_opex_cents":10000,"planting_cost_cents":0,"water_cost_cents":0,"fertilizer_cost_cents":0}},
         {"crop_profile_id":"7784","plant_date":"2028-04-21","clearance_date":"2028-08-01","harvest_dates":["2028-08-01"],
          "yield_multiplier":1,"notes":[]}]}
        """;

    [Fact]
    public void ReadsDayPreciseDatesAndDerivesTheMonthsFromThem()
    {
        var result = StrategyResultMapper.Map(DayDates, Names)!;

        var wheat = result.Cycles[0];
        Assert.Equal("2026-10-11", wheat.PlantingDate);
        Assert.Equal("2027-07-21", wheat.ClearanceDate);
        Assert.Equal(["2027-07-11", "2027-07-21"], wheat.HarvestDates);
        Assert.Equal("2026-10", wheat.PlantingMonth);
        Assert.Equal("2027-07", wheat.ClearanceMonth);
        Assert.Equal(["2027-07"], wheat.HarvestMonths);
        Assert.Equal("2028-04-21", result.Cycles[1].PlantingDate);
        Assert.Equal("2028-08", result.Cycles[1].ClearanceMonth);
    }

    [Fact]
    public void OlderMonthOnlyResultsHaveNoDays()
    {
        var wheat = StrategyResultMapper.Map(TwoCycles, Names)!.Cycles[0];

        Assert.Equal("2026-10", wheat.PlantingMonth);
        Assert.Equal(["2027-07"], wheat.HarvestMonths);
        Assert.Null(wheat.PlantingDate);
        Assert.Null(wheat.ClearanceDate);
        Assert.Empty(wheat.HarvestDates!);
    }

    [Fact]
    public void ChargesOpexByTheYearsOfTheHarvestDates()
    {
        var cycle = new CropProfileCycle("1", null, null, null, 0, 0, 1, 0, 0, 0, 0, null,
            PlantDate: "2026-10-21", ClearanceDate: "2028-07-11", HarvestDates: ["2027-07-01", "2028-07-01"]);

        Assert.Equal([2027, 2028], StrategyResultMapper.SeasonYears(cycle));
    }

    [Fact]
    public void ConvertsCentsAndSums()
    {
        var result = StrategyResultMapper.Map(TwoCycles, Names)!;

        Assert.Equal("OPTIMAL", result.Status);
        Assert.Equal(200000m, result.ProfitUsd);
        Assert.Equal(184500m + 60000m, result.RevenueUsd);
        Assert.Equal(45000m + 19500m, result.CostsUsd);
        Assert.Equal(160.5, result.IrrigationMm, 3);
        Assert.Equal(45 - 110 + 80 - 30, result.NitrogenBalanceKgHa, 3);
    }

    private static CropProfileNames WithOpex(double? areaHectares) => new(new Dictionary<long, CropProfileName>
    {
        [7754] = new(7726, "Wheat", "Winter Grain Harvest"),
        [7784] = new(7751, "Peas", "Spring Grain Harvest"),
    }, opexCentsPerHa: new Dictionary<long, IReadOnlyDictionary<int, long>>
    {
        // $350/ha in 2027; peas only have 2026, so 2028 takes the nearest year.
        [7754] = new Dictionary<int, long> { [2026] = 34_000, [2027] = 35_000 },
        [7784] = new Dictionary<int, long> { [2026] = 20_000 },
    }, areaHectares: areaHectares);

    [Fact]
    public void AddsOperatingCostsPerHectareOverTheFieldToCostsAndTakesThemOffProfit()
    {
        var result = StrategyResultMapper.Map(TwoCycles, WithOpex(10))!;

        // Wheat harvested in 2027: 350 $/ha x 10 ha. Peas harvested in 2028, nearest rate 2026: 200 $/ha x 10 ha.
        Assert.Equal(3500m, result.Cycles[0].OperatingCostsUsd);
        Assert.Equal(45000m + 3500m, result.Cycles[0].CostsUsd);
        Assert.Equal(2000m, result.Cycles[1].OperatingCostsUsd);
        Assert.Equal(5500m, result.OperatingCostsUsd);
        Assert.Equal(45000m + 19500m + 5500m, result.CostsUsd);
        Assert.Equal(200000m - 5500m, result.ProfitUsd);
    }

    [Fact]
    public void LeavesCostsAsFelderSentThemWithoutOpexOrArea()
    {
        foreach (var names in new[] { Names, WithOpex(null) })
        {
            var result = StrategyResultMapper.Map(TwoCycles, names)!;
            Assert.Null(result.OperatingCostsUsd);
            Assert.Null(result.Cycles[0].OperatingCostsUsd);
            Assert.Equal(45000m + 19500m, result.CostsUsd);
            Assert.Equal(200000m, result.ProfitUsd);
        }
    }

    [Fact]
    public void ChargesAPerennialOncePerHarvestYear()
    {
        var json = TwoCycles.Replace("\"harvest_year_months\":[\"2027-07\"]", "\"harvest_year_months\":[\"2027-07\",\"2027-09\",\"2028-07\",\"2029-07\"]");
        // 2027 (two cuts, one season), 2028 and 2029; the later years take 2027's rate, the nearest one.
        Assert.Equal(3 * 3500m, StrategyResultMapper.Map(json, WithOpex(10))!.Cycles[0].OperatingCostsUsd);
    }

    [Fact]
    public void ChargesTheClearanceYearWhenNothingIsHarvested()
    {
        var json = TwoCycles.Replace("\"harvest_year_months\":[\"2027-07\"]", "\"harvest_year_months\":[]");
        Assert.Equal(3500m, StrategyResultMapper.Map(json, WithOpex(10))!.Cycles[0].OperatingCostsUsd);
    }

    [Fact]
    public void CarriesEachCyclesNutrientsAndReadsMissingPotassiumAsZero()
    {
        // Only the first cycle reports potassium, as a solver older than potassium would leave it out of the second.
        var json = TwoCycles.Replace("\"nitrogen_removed_kg_ha\":110,", "\"nitrogen_removed_kg_ha\":110,\"potassium_applied_kg_ha\":30,\"potassium_removed_kg_ha\":55,");
        var cycles = StrategyResultMapper.Map(json, Names)!.Cycles;

        Assert.Equal(new CycleNutrients(45, 0, 110, 0, 0, 30, 55), cycles[0].Nutrients);
        Assert.Equal(new CycleNutrients(0, 80, 30, 0, 0, 0, 0), cycles[1].Nutrients);
    }

    [Fact]
    public void NamesCyclesFromTheCatalog()
    {
        var cycle = StrategyResultMapper.Map(TwoCycles, Names)!.Cycles[0];

        Assert.Equal((7754L, 7726L, "Wheat", "Winter Grain Harvest"), (cycle.CropProfileId, cycle.CropId, cycle.CropName, cycle.SystemName));
        Assert.Equal(("2026-10", "2027-07"), (cycle.PlantingMonth, cycle.ClearanceMonth));
        Assert.Equal(85, cycle.YieldPercent, 3);
        Assert.Equal(184500m, cycle.RevenueUsd);
    }

    [Fact]
    public void SplitsDataNotes()
    {
        var result = StrategyResultMapper.Map(TwoCycles, Names)!;

        Assert.Equal(["Heat shock: 4 days > 25.0°C in stage 'flowering'"], result.Risks);
        Assert.Equal(["No salinity for 2030"], result.DataGaps);
        // An old tagged [DATA] note is a warning; the other tags are risks; none is a plain note.
        Assert.Equal(["No salinity for 2030"], result.Warnings);
        Assert.Equal(["Heat shock: 4 days > 25.0°C in stage 'flowering'"], result.Cycles[0].Risks);
        Assert.Equal(["No salinity for 2030"], result.Cycles[0].Warnings);
        Assert.Empty(result.Cycles[0].Notes);
    }

    // Felder 2.3 since 2026-11-09 ("Change from RiskNotes to Notes"): structured notes replace the tagged strings.
    private const string StructuredNotes = """
        {"status":"OPTIMAL","expected_profit_cents":1000,"schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2026-10","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":0.65,"irrigation_applied_mm":0,
          "nitrogen_applied_kg_ha":0,"nitrogen_fixed_kg_ha":0,"nitrogen_removed_kg_ha":0,
          "notes":[{"message":"Soil pH too high (7.8 > 7.5)","category":"agronomic","importance":"risk"},
                   {"message":"Unfertilized: yield capped at 65% (soil: silt_loam)","category":"agronomic","importance":"warn"},
                   {"message":"Missing removal rates for: N, P, K","category":"data_missing","importance":"risk"},
                   {"message":"Topsoil organic matter is stable","category":"soil_health","importance":"info"}]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-08","harvest_year_months":["2028-08"],
          "expected_revenue_cents":0,"expenses_cents":0,"yield_multiplier":0.9,"irrigation_applied_mm":0,
          "nitrogen_applied_kg_ha":0,"nitrogen_fixed_kg_ha":0,"nitrogen_removed_kg_ha":0,
          "notes":[{"message":"Soil pH too high (7.8 > 7.5)","category":"agronomic","importance":"risk"},
                   {"message":"Stage 'flowering' timeout: projected 49 days (max 42)","category":"agronomic","importance":"risk"}]}]}
        """;

    [Fact]
    public void SeparatesRisksFromWarningsByImportance()
    {
        var result = StrategyResultMapper.Map(StructuredNotes, Names)!;

        Assert.Equal(["Soil pH too high (7.8 > 7.5)", "Missing removal rates for: N, P, K",
            "Stage 'flowering' timeout: projected 49 days (max 42)"], result.Risks);
        Assert.Equal(["Unfertilized: yield capped at 65% (soil: silt_loam)"], result.Warnings);
        Assert.Equal(["Missing removal rates for: N, P, K"], result.DataGaps);
    }

    [Fact]
    public void KeepsRisksWarningsAndPlainNotesApartOnTheCycle()
    {
        var cycle = StrategyResultMapper.Map(StructuredNotes, Names)!.Cycles[0];

        Assert.Equal(["Soil pH too high (7.8 > 7.5)", "Missing removal rates for: N, P, K"], cycle.Risks);
        Assert.Equal(["Unfertilized: yield capped at 65% (soil: silt_loam)"], cycle.Warnings);
        Assert.Equal(["Topsoil organic matter is stable"], cycle.Notes);
        // A category the API has no rule for (soil_health) still arrives, with its importance.
        Assert.Contains(new CycleNote("Topsoil organic matter is stable", "info", "soil_health"), cycle.NoteDetails);
    }

    // Felder 2.3 since 2026-11-10: month-by-month water and nutrients replace the cycle totals.
    private const string MonthlyDynamics = """
        {"status":"OPTIMAL","expected_profit_cents":1000,"schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2027-04","clearance_year_month":"2027-06","harvest_year_months":["2027-06"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":0.9,
          "monthly_dynamics":{
            "irrigation_applied_mm":{"2027-05":20.5,"2027-06":30},
            "water_removed_mm":{"2027-04":15,"2027-05":60.25,"2027-06":80},
            "nitrogen_applied_kg_ha":{"2027-04":40,"2027-05":20},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{"2027-06":90},
            "phosphorus_applied_kg_ha":{"2027-04":15},"phosphorus_removed_kg_ha":{"2027-06":25},
            "potassium_applied_kg_ha":{"2027-04":10},"potassium_removed_kg_ha":{"2027-06":45}},
          "notes":[{"importance":"warn","category":"agronomic","message":"Late frost possible"}]}]}
        """;

    [Fact]
    public void ReadsMonthlyWaterAndSumsTheMonthsIntoTotals()
    {
        var result = StrategyResultMapper.Map(MonthlyDynamics, Names)!;
        var cycle = result.Cycles[0];

        Assert.Equal([new MonthlyWater("2027-04", 0, 15), new MonthlyWater("2027-05", 20.5, 60.25), new MonthlyWater("2027-06", 30, 80)], cycle.MonthlyWater);
        Assert.Equal(50.5, cycle.IrrigationMm, 3);
        Assert.Equal(155.25, result.WaterUseMm!.Value, 3);
        Assert.Equal(new CycleNutrients(60, 0, 90, 15, 25, 10, 45), cycle.Nutrients);
        Assert.Equal(60 - 90, result.NitrogenBalanceKgHa, 3);
        Assert.Equal(["Late frost possible"], cycle.Warnings);
        Assert.Empty(result.Risks);
    }

    [Fact]
    public void OlderResultsHaveNoMonthlyWaterOrWaterUse()
    {
        var result = StrategyResultMapper.Map(TwoCycles, Names)!;

        Assert.Empty(result.Cycles[0].MonthlyWater);
        Assert.Null(result.WaterUseMm);
    }

    [Fact]
    public void ReadsTaggedStringNotesUnderTheNewKey()
    {
        var json = TwoCycles.Replace("\"risk_notes\"", "\"notes\"");

        var result = StrategyResultMapper.Map(json, Names)!;

        Assert.Equal(["Heat shock: 4 days > 25.0°C in stage 'flowering'"], result.Risks);
        Assert.Equal(["No salinity for 2030"], result.DataGaps);
    }

    [Fact]
    public void DataMissingTagIsADataGap()
    {
        var json = TwoCycles.Replace("[DATA] No salinity for 2030", "[DATA_MISSING] Missing removal rates for: N, P, K");

        var result = StrategyResultMapper.Map(json, Names)!;

        Assert.Equal(["Missing removal rates for: N, P, K"], result.DataGaps);
        Assert.DoesNotContain("Missing removal rates for: N, P, K", result.Risks);
    }

    [Fact]
    public void UnknownProfileFallsBackToUnknownCrop()
    {
        var cycle = StrategyResultMapper.Map(TwoCycles, CropProfileNames.Empty)!.Cycles[1];

        Assert.Equal("Unknown crop", cycle.CropName);
        Assert.Equal(0, cycle.CropId);
    }

    [Fact]
    public void InfeasibleKeepsStatusAndEmptyCycles()
    {
        var result = StrategyResultMapper.Map("""{"status":"INFEASIBLE","expected_profit_cents":null,"schedule":[]}""", Names)!;

        Assert.Equal("INFEASIBLE", result.Status);
        Assert.Null(result.ProfitUsd);
        Assert.Empty(result.Cycles);
    }

    [Fact]
    public void BrokenJsonMapsToNull()
    {
        Assert.Null(StrategyResultMapper.Map("not json", Names));
        Assert.Null(StrategyResultMapper.Map(null, Names));
    }

    private const string Fertilized = """
        {"status":"OPTIMAL","expected_profit_cents":1000,"schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2027-04","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":1,"fertilizer_material_id":"3101",
          "monthly_dynamics":{
            "water_applied_mm":{"2027-05":12,"2027-06":0},"water_removed_mm":{"2027-05":50},
            "nitrogen_applied_kg_ha":{"2027-04":40,"2027-05":0},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{},
            "phosphorus_applied_kg_ha":{"2027-04":15,"2027-06":5},"phosphorus_removed_kg_ha":{},
            "potassium_applied_kg_ha":{"2027-04":10},"potassium_removed_kg_ha":{}},
          "notes":[]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-08","harvest_year_months":["2028-08"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":1,"fertilizer_material_id":null,
          "monthly_dynamics":{"water_applied_mm":{},"water_removed_mm":{},"nitrogen_applied_kg_ha":{},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{},
            "phosphorus_applied_kg_ha":{},"phosphorus_removed_kg_ha":{},"potassium_applied_kg_ha":{},"potassium_removed_kg_ha":{}},
          "notes":[]}]}
        """;

    [Fact]
    public void ReportsTheFertilizerProductAndTheMonthsItWentOn()
    {
        var names = new CropProfileNames(new Dictionary<long, CropProfileName> { [7754] = new(7726, "Wheat", "Winter") },
            new Dictionary<long, string> { [3101] = "NPK 16-16-16" });
        var cycles = StrategyResultMapper.Map(Fertilized, names)!.Cycles;

        var fertilizer = cycles[0].Fertilizer!;
        Assert.Equal("3101", fertilizer.MaterialId);
        Assert.Equal("NPK 16-16-16", fertilizer.Name);
        Assert.Equal([new FertilizerApplication("2027-04", 40, 15, 10), new FertilizerApplication("2027-06", 0, 5, 0)], fertilizer.Applications);
        Assert.Null(cycles[1].Fertilizer);
    }

    [Fact]
    public void ReadsIrrigationUnderTheWaterAppliedName()
    {
        var cycle = StrategyResultMapper.Map(Fertilized, Names)!.Cycles[0];
        Assert.Equal(12, cycle.IrrigationMm, 3);
        Assert.Equal(new MonthlyWater("2027-05", 12, 50), cycle.MonthlyWater.Single(month => month.Month == "2027-05"));
        // Without a materials lookup the product keeps its id and has no name.
        Assert.Null(cycle.Fertilizer!.Name);
    }

    [Fact]
    public void FindsTheFertilizerIdsToName()
    {
        Assert.Equal([3101L], StrategyProfiles.FertilizerIds(Fertilized));
        Assert.Equal([7754L, 7784L], StrategyProfiles.ProfileIds(Fertilized));
    }

    // felder 2.4 (2026-11-11): the product and how many kg of it per hectare, instead of the bare material id.
    private const string FertilizedWithRate = """
        {"status":"OPTIMAL","expected_profit_cents":1000,"schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2027-04","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":1,
          "fertilizer_application":{"material_id":"3101","rate_kg_ha":242.4},
          "monthly_dynamics":{"water_applied_mm":{},"water_removed_mm":{},
            "nitrogen_applied_kg_ha":{"2027-04":40},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{},
            "phosphorus_applied_kg_ha":{"2027-04":40},"phosphorus_removed_kg_ha":{},"potassium_applied_kg_ha":{"2027-04":40},"potassium_removed_kg_ha":{}},
          "notes":[]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-08","harvest_year_months":["2028-08"],
          "expected_revenue_cents":1000,"expenses_cents":0,"yield_multiplier":1,"fertilizer_application":null,
          "monthly_dynamics":{"water_applied_mm":{},"water_removed_mm":{},"nitrogen_applied_kg_ha":{},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{},
            "phosphorus_applied_kg_ha":{},"phosphorus_removed_kg_ha":{},"potassium_applied_kg_ha":{},"potassium_removed_kg_ha":{}},
          "notes":[]}]}
        """;

    [Fact]
    public void ReportsHowMuchOfTheProductGoesOnPerHectare()
    {
        var names = new CropProfileNames(new Dictionary<long, CropProfileName>(), new Dictionary<long, string> { [3101] = "Nitrophoska" });
        var cycles = StrategyResultMapper.Map(FertilizedWithRate, names)!.Cycles;

        var fertilizer = cycles[0].Fertilizer!;
        Assert.Equal("3101", fertilizer.MaterialId);
        Assert.Equal("Nitrophoska", fertilizer.Name);
        Assert.Equal(242.4, fertilizer.RateKgHa);
        Assert.Null(cycles[1].Fertilizer);
        Assert.Equal([3101L], StrategyProfiles.FertilizerIds(FertilizedWithRate));
    }

    [Fact]
    public void OlderResultsHaveNoProductRate() => Assert.Null(StrategyResultMapper.Map(Fertilized, Names)!.Cycles[0].Fertilizer!.RateKgHa);

    // felder 2.4 (2026-11-12): money per cycle as a breakdown, OpEx included; no expected_revenue_cents, expenses_cents or profit.
    private const string WithFinancials = """
        {"status":"OPTIMAL","schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2027-04","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "financials":{"gross_revenue_cents":1000000,"base_opex_cents":200000,"planting_cost_cents":50000,"water_cost_cents":25000,"fertilizer_cost_cents":25000},
          "yield_multiplier":0.9,"notes":[]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-08","harvest_year_months":["2028-08"],
          "financials":{"gross_revenue_cents":300000,"base_opex_cents":100000,"planting_cost_cents":0,"water_cost_cents":0,"fertilizer_cost_cents":0},
          "yield_multiplier":1,"notes":[]}]}
        """;

    [Fact]
    public void ReadsRevenueAndCostsFromTheFinancialsBreakdown()
    {
        var result = StrategyResultMapper.Map(WithFinancials, Names)!;
        Assert.Equal([10000m, 3000m], result.Cycles.Select(cycle => cycle.RevenueUsd));
        Assert.Equal([3000m, 1000m], result.Cycles.Select(cycle => cycle.CostsUsd));
        Assert.Equal([2000m, 1000m], result.Cycles.Select(cycle => cycle.OperatingCostsUsd));
        Assert.Equal(13000m, result.RevenueUsd);
        Assert.Equal(4000m, result.CostsUsd);
        Assert.Equal(9000m, result.ProfitUsd);
        Assert.Equal(3000m, result.OperatingCostsUsd);
    }

    [Fact]
    public void BreaksEachCropsCostsDownFromTheFinancials()
    {
        var result = StrategyResultMapper.Map(WithFinancials, Names)!;
        Assert.Equal(new CostBreakdown(2000m, 500m, 250m, 250m), result.Cycles[0].CostBreakdown);
        Assert.Equal(new CostBreakdown(3000m, 500m, 250m, 250m), result.CostBreakdown);
        // Results stored before felder 2.4 have one expenses figure only.
        Assert.Null(StrategyResultMapper.Map(Fertilized, Names)!.Cycles[0].CostBreakdown);
        Assert.Null(StrategyResultMapper.Map(Fertilized, Names)!.CostBreakdown);
    }

    [Fact]
    public void FinancialsAlreadyHoldOpExSoTheCatalogRatesAreNotAddedAgain()
    {
        var names = new CropProfileNames(new Dictionary<long, CropProfileName>(), null,
            new Dictionary<long, IReadOnlyDictionary<int, long>> { [7754] = new Dictionary<int, long> { [2027] = 99_999 } }, 10);
        var result = StrategyResultMapper.Map(WithFinancials, names)!;
        Assert.Equal(3000m, result.Cycles[0].CostsUsd);
        Assert.Equal(9000m, result.ProfitUsd);
    }

    /// <summary>
    /// felder's EvaluateScenarioResponse since 2026-11-13: every planned crop back as a full cycle with the solver's own
    /// simulation and notes, including a crop the simulation stopped on (here the peas, cut short by a drought shock).
    /// </summary>
    private const string Evaluation = """
        {"status":"FEASIBLE","schedule":[
         {"crop_profile_id":"7754","planting_year_month":"2026-10","clearance_year_month":"2027-07","harvest_year_months":["2027-07"],
          "financials":{"gross_revenue_cents":900000,"base_opex_cents":200000,"planting_cost_cents":80000,"water_cost_cents":0,"fertilizer_cost_cents":0},
          "yield_multiplier":0.62,
          "monthly_dynamics":{"water_applied_mm":{"2027-05":0,"2027-06":0},"water_removed_mm":{"2027-05":95.5,"2027-06":120},
           "nitrogen_applied_kg_ha":{"2026-10":0},"nitrogen_fixed_kg_ha":{"2026-10":0},"nitrogen_removed_kg_ha":{"2027-07":72.4},
           "phosphorus_applied_kg_ha":{"2026-10":0},"phosphorus_removed_kg_ha":{"2027-07":12.1},
           "potassium_applied_kg_ha":{"2026-10":0},"potassium_removed_kg_ha":{"2027-07":30.2}},
          "notes":[{"importance":"warn","category":"agronomic","message":"Unfertilized: yield limited to 62%"},
           {"importance":"warn","category":"agronomic","message":"Carryover N deficit: 12.3/40.0 kg/ha"},
           {"importance":"info","category":"data_missing","message":"Missing OpEx for year 2027 (7754). Falling back to year 2026."}]},
         {"crop_profile_id":"7784","planting_year_month":"2028-04","clearance_year_month":"2028-04","harvest_year_months":[],
          "financials":{"gross_revenue_cents":0,"base_opex_cents":0,"planting_cost_cents":0,"water_cost_cents":0,"fertilizer_cost_cents":0},
          "yield_multiplier":1.0,
          "monthly_dynamics":{"water_applied_mm":{},"water_removed_mm":{},"nitrogen_applied_kg_ha":{},"nitrogen_fixed_kg_ha":{},"nitrogen_removed_kg_ha":{},
           "phosphorus_applied_kg_ha":{},"phosphorus_removed_kg_ha":{},"potassium_applied_kg_ha":{},"potassium_removed_kg_ha":{}},
          "notes":[{"importance":"risk","category":"agronomic","message":"Severe drought shock: flowering (12d > tolerance 7d)"}]}]}
        """;

    [Fact]
    public void AnEvaluationShowsEveryCropWithTheSolversOwnSimulationAndNotes()
    {
        var result = StrategyResultMapper.Map(Evaluation, Names)!;

        Assert.Equal("FEASIBLE", result.Status);
        Assert.Equal(["Wheat", "Peas"], result.Cycles.Select(cycle => cycle.CropName));
        var wheat = result.Cycles[0];
        // The solver's simulated yield, water use by month and N-P-K, not an estimate.
        Assert.Equal(62, wheat.YieldPercent);
        Assert.False(wheat.Estimated);
        Assert.Equal([new MonthlyWater("2027-05", 0, 95.5), new MonthlyWater("2027-06", 0, 120)], wheat.MonthlyWater);
        Assert.Equal(new CycleNutrients(0, 0, 72.4, 0, 12.1, 0, 30.2), wheat.Nutrients);
        Assert.Null(wheat.Fertilizer);
        Assert.Equal(new CostBreakdown(2000m, 800m, 0m, 0m), wheat.CostBreakdown);
        // The solver's messages, word for word, under their importance and category.
        Assert.Equal(["Unfertilized: yield limited to 62%", "Carryover N deficit: 12.3/40.0 kg/ha"], wheat.Warnings);
        Assert.Equal(["Missing OpEx for year 2027 (7754). Falling back to year 2026."], wheat.Notes);
        Assert.Contains(wheat.NoteDetails, note => note is { Category: "data_missing", Importance: StrategyResultMapper.Info });
        // A crop the simulation stopped on still comes back, with the solver's reason as a risk.
        var peas = result.Cycles[1];
        Assert.Equal(["Severe drought shock: flowering (12d > tolerance 7d)"], peas.Risks);
        Assert.Equal(0m, peas.RevenueUsd);
        Assert.Empty(peas.MonthlyWater);
        Assert.Equal(["Severe drought shock: flowering (12d > tolerance 7d)"], result.Risks);
        Assert.Equal(215.5, result.WaterUseMm);
        Assert.Equal(9000m - 2800m, result.ProfitUsd);
    }
}
