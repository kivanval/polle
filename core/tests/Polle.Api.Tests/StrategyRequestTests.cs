using System.Text.Json;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

public sealed class StrategyRequestTests
{
    private static readonly Guid FieldId = Guid.Parse("b4fdc56d-3e7e-4488-a43e-a31be772245e");
    private static readonly SolverMaterials Materials = new("2215");

    private static FieldPlanting Planting(long profile, DateOnly sown, DateOnly? harvested = null) =>
        new() { FieldId = FieldId, CropProfileId = profile, SeasonYear = sown.Year, PlantingDate = sown, HarvestingDate = harvested };

    private static OptimizeFieldRequest Optimization(string key) =>
        StrategyRequests.Optimization(FieldId, "2026-12", "2031-10", StrategyCatalog.Find(key)!, Materials);

    [Fact]
    public void SixPresetsRunByDefaultInIvansOrder()
    {
        Assert.Equal(["control", "rainfed-potential", "medium", "full-inputs", "full-inputs-estimates", "cash-grab"], StrategyCatalog.DefaultKeys);
        Assert.Equal(["Control", "Rainfed potential", "Medium", "Full inputs", "Full inputs with estimates", "Cash grab"],
            StrategyCatalog.Presets.Select(preset => preset.Name));
    }

    [Theory]
    // key, missing data, soil depletion, irrigation applied, irrigation resolution, fertilizer resolution
    [InlineData("control", false, false, false, "BINARY", "QUATERNARY")]
    [InlineData("rainfed-potential", false, true, false, "BINARY", "QUATERNARY")]
    [InlineData("medium", false, false, true, "TERNARY", "QUATERNARY")]
    [InlineData("full-inputs", false, false, true, "CONTINUOUS", "CONTINUOUS")]
    [InlineData("full-inputs-estimates", true, false, true, "CONTINUOUS", "CONTINUOUS")]
    [InlineData("cash-grab", true, true, true, "CONTINUOUS", "CONTINUOUS")]
    public void PresetsSendIvansFlags(string key, bool missingData, bool soilDepletion, bool irrigation, string irrigationResolution, string fertilizerResolution)
    {
        var strategy = Optimization(key).Strategy;
        Assert.Equal(missingData, strategy.Risk.AllowMissingData);
        Assert.Equal(soilDepletion, strategy.Risk.AllowSoilDepletion);
        Assert.True(strategy.Risk.AllowAgronomicRisks);
        Assert.Equal(irrigation, strategy.Replenishment.Irrigation.Apply);
        Assert.Equal(irrigationResolution, strategy.Replenishment.Irrigation.Resolution);
        Assert.True(strategy.Replenishment.Fertilizers.Apply);
        Assert.Equal(fertilizerResolution, strategy.Replenishment.Fertilizers.Resolution);
    }

    [Theory]
    [InlineData("bold-irrigated", true, true, true, true)]
    [InlineData("safe-fertilized", false, true, false, false)]
    [InlineData("balanced-dryland", false, false, true, false)]
    public void StrategiesOfTheEarlierMatrixStillResolve(string key, bool irrigation, bool fertilizer, bool risks, bool depletion)
    {
        var strategy = StrategyCatalog.Find(key)!;
        Assert.DoesNotContain(key, StrategyCatalog.DefaultKeys);
        Assert.Equal(new StrategySettings(irrigation, fertilizer, risks, depletion, false, irrigation ? "QUATERNARY" : "OFF", fertilizer ? "QUATERNARY" : "OFF"),
            StrategySettings.For(key));
        Assert.False(Optimization(key).Strategy.Risk.AllowMissingData);
        Assert.False(string.IsNullOrEmpty(strategy.Name));
    }

    [Fact]
    public void CustomKeysSpellOutTheirSettings()
    {
        var options = new StrategyOptions(AllowMissingData: false, AllowAgronomicRisks: false, AllowSoilDepletion: true, ReplenishmentLevel.Binary, ReplenishmentLevel.Off);
        var key = StrategyCatalog.CustomKey(options);

        Assert.Equal("custom-001-bin-off", key);
        var strategy = StrategyCatalog.Find(key)!;
        Assert.Equal(options, strategy.Options);
        Assert.Equal("Custom: water on or off, fertilizer off", strategy.Name);
        var request = Optimization(key).Strategy;
        Assert.True(request.Replenishment.Irrigation.Apply);
        Assert.Equal("BINARY", request.Replenishment.Irrigation.Resolution);
        Assert.False(request.Replenishment.Fertilizers.Apply);
        Assert.False(request.Risk.AllowAgronomicRisks);
        Assert.True(key.Length <= 40, "field_strategies.key is varchar(40)");
    }

    [Fact]
    public void CustomSettingsEqualToAPresetKeepACustomKey()
    {
        var key = StrategyCatalog.CustomKey(StrategyCatalog.Find("medium")!.Options);
        Assert.Equal("custom-010-ter-qua", key);
        Assert.Equal(StrategyCatalog.Find("medium")!.Options, StrategyCatalog.Find(key)!.Options);
        Assert.Equal("Water in 3 steps, fertilizer in 4 steps. Accepts agronomic risks, keeps the soil fed and plans only with complete figures.",
            StrategyCatalog.Find(key)!.Summary);
    }

    [Theory]
    [InlineData("custom-2")]
    [InlineData("custom-001-bin")]
    [InlineData("custom-001-xyz-off")]
    [InlineData("nonsense")]
    public void UnknownKeysDontResolve(string key) => Assert.Null(StrategyCatalog.Find(key));

    [Fact]
    public void NoChoiceRunsThePresets()
    {
        Assert.Equal(StrategyCatalog.DefaultKeys, StrategyChoices.Picks(null).Picks.Select(pick => pick.Key));
        Assert.Equal(StrategyCatalog.DefaultKeys, StrategyChoices.Picks([]).Picks.Select(pick => pick.Key));
    }

    [Fact]
    public void ChoicesBecomeKeysInOrderWithoutRepeatedPresets()
    {
        var (picks, errors) = StrategyChoices.Picks([
            new(Preset: "cash-grab"),
            new(AllowMissingData: false, AllowAgronomicRisks: false, AllowSoilDepletion: false, Irrigation: "ternary", Fertilizer: "OFF"),
            new(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: false, Irrigation: "TERNARY", Fertilizer: "QUATERNARY"),
            new(Preset: "cash-grab"),
        ]);
        Assert.Empty(errors);
        Assert.Equal(["cash-grab", "custom-000-ter-off", "custom-010-ter-qua"], picks.Select(pick => pick.Key));
    }

    /// <summary>
    /// The reported bug: six presets plus the new-strategy form's default settings (equal to Medium) showed "7 chosen
    /// strategies" but planned six, because the custom one was folded into Medium.
    /// </summary>
    [Fact]
    public void ScoringOnlyThePlanRunsJustTheEvaluation()
    {
        var jobs = StrategyJobs.Plan(hasBaseline: true, [], DateTime.UtcNow);

        var job = Assert.Single(jobs);
        Assert.Equal(StrategyCatalog.BaselineKey, job.Key);
        Assert.Equal(StrategyJobs.Evaluation, job.Kind);
    }

    [Fact]
    public void SixPresetsAndACustomStrategyPlanSevenJobsWithTheFarmersNameAndDescription()
    {
        StrategyChoice[] choices =
        [
            .. StrategyCatalog.DefaultKeys.Select(key => new StrategyChoice(Preset: key)),
            new(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: false, Irrigation: "TERNARY", Fertilizer: "QUATERNARY",
                Name: "  My dryland test ", Description: "Medium inputs,\n tried on the north field."),
        ];

        var (picks, errors) = StrategyChoices.Picks(choices);
        var jobs = StrategyJobs.Plan(hasBaseline: false, picks, DateTime.UtcNow);

        Assert.Empty(errors);
        Assert.Equal(7, jobs.Count);
        Assert.Equal([.. StrategyCatalog.DefaultKeys, "custom-010-ter-qua"], jobs.Select(job => job.Key));
        Assert.All(jobs, job => Assert.Equal(StrategyJobs.Optimization, job.Kind));
        var custom = jobs[^1];
        Assert.Equal("My dryland test", custom.Name);
        Assert.Equal("Medium inputs, tried on the north field.", custom.Summary);
        Assert.All(jobs.Take(6), job => Assert.Null(job.Name));

        var response = StrategyRunResponse.Job(custom, CropProfileNames.Empty);
        Assert.Equal(("My dryland test", "Medium inputs, tried on the north field."), (response.Name, response.Summary));
        Assert.Equal("TERNARY", response.Settings!.IrrigationLevel);
        Assert.Equal("Medium", StrategyRunResponse.Job(jobs[2], CropProfileNames.Empty).Name);
    }

    [Fact]
    public async Task EverySevenChosenStrategiesReachTheSolver()
    {
        var (picks, _) = StrategyChoices.Picks([
            .. StrategyCatalog.DefaultKeys.Select(key => new StrategyChoice(Preset: key)),
            new(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: false, Irrigation: "TERNARY", Fertilizer: "QUATERNARY", Name: "Mine"),
        ]);
        var handler = new StubHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            { Content = new System.Net.Http.StringContent("""{"task_id":"t"}""", System.Text.Encoding.UTF8, "application/json") });
        var solver = new FelderClient(new HttpClient(handler) { BaseAddress = new Uri("https://solver.test/") });
        var run = new StrategyRun { FieldId = FieldId, StartMonth = "2026-12", EndMonth = "2031-10" };
        run.Jobs = StrategyJobs.Plan(hasBaseline: false, picks, DateTime.UtcNow);

        await Task.WhenAll(run.Jobs.Select(job => StrategyRunEndpoints.SubmitAsync(job, run, null, solver, Materials, CancellationToken.None)));

        Assert.Equal(7, handler.Requests.Count(uri => uri.AbsolutePath.EndsWith("/optimizations", StringComparison.Ordinal)));
        Assert.All(run.Jobs, job => Assert.Equal("t", job.TaskId));
    }

    [Fact]
    public async Task SuggestingStrategiesNeverEvaluatesTheCurrentPlan()
    {
        var (picks, _) = StrategyChoices.Picks([new(Preset: "control"), new(Preset: "medium")]);
        var handler = new StubHandler(_ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            { Content = new System.Net.Http.StringContent("""{"task_id":"t"}""", System.Text.Encoding.UTF8, "application/json") });
        var solver = new FelderClient(new HttpClient(handler) { BaseAddress = new Uri("https://solver.test/") });
        var run = new StrategyRun { FieldId = FieldId, StartMonth = "2026-12", EndMonth = "2031-10" };
        // The field's plan has crops in the horizon, yet "Suggest strategies" leaves it to "Evaluate my plan".
        run.Jobs = StrategyJobs.ForRequest(planOnly: false, hasBaseline: true, picks, DateTime.UtcNow);

        await Task.WhenAll(run.Jobs.Select(job => StrategyRunEndpoints.SubmitAsync(job, run, null, solver, Materials, CancellationToken.None)));

        Assert.Equal(["control", "medium"], run.Jobs.Select(job => job.Key));
        Assert.DoesNotContain(run.Jobs, job => job.Kind == StrategyJobs.Evaluation || job.Key == StrategyCatalog.BaselineKey);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.EndsWith("/optimizations", uri.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluatingThePlanRunsNoStrategies()
    {
        var (picks, _) = StrategyChoices.Picks([new(Preset: "control")]);

        var job = Assert.Single(StrategyJobs.ForRequest(planOnly: true, hasBaseline: true, picks, DateTime.UtcNow));

        Assert.Equal((StrategyCatalog.BaselineKey, StrategyJobs.Evaluation), (job.Key, job.Kind));
    }

    [Fact]
    public void OnlyTheChosenStrategiesRun()
    {
        var (picks, errors) = StrategyChoices.Picks([
            new(Preset: "control"),
            new(Preset: "cash-grab"),
            new(AllowMissingData: true, AllowAgronomicRisks: false, AllowSoilDepletion: false, Irrigation: "OFF", Fertilizer: "BINARY"),
        ]);

        Assert.Empty(errors);
        Assert.Equal(["control", "cash-grab", "custom-100-off-bin"], StrategyJobs.Plan(hasBaseline: false, picks, DateTime.UtcNow).Select(job => job.Key));
    }

    [Fact]
    public void ACustomStrategyWithoutANameOrDescriptionReadsFromItsSettings()
    {
        var (picks, _) = StrategyChoices.Picks([
            new(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: true, Irrigation: "OFF", Fertilizer: "CONTINUOUS", Name: " ", Description: ""),
        ]);
        var job = StrategyJobs.Plan(hasBaseline: false, picks, DateTime.UtcNow).Single();

        Assert.Null(job.Name);
        Assert.Null(job.Summary);
        var response = StrategyRunResponse.Job(job, CropProfileNames.Empty);
        Assert.Equal("Custom: water off, fertilizer dosed finely", response.Name);
        Assert.Equal("Rain only, fertilizer dosed finely. Accepts agronomic risks, may draw the soil down and plans only with complete figures.", response.Summary);
    }

    [Fact]
    public void TwoCustomStrategiesWithTheSameSettingsAreRefusedNotDropped()
    {
        var custom = new StrategyChoice(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: false, Irrigation: "OFF", Fertilizer: "OFF");
        var (_, errors) = StrategyChoices.Picks([custom, custom with { Name = "Again" }]);

        Assert.Equal(["strategies[1]"], errors.Keys);
    }

    [Fact]
    public void OverlongNamesAndDescriptionsAreRefused()
    {
        var custom = new StrategyChoice(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: false, Irrigation: "OFF", Fertilizer: "OFF");
        var (_, errors) = StrategyChoices.Picks([
            custom with { Name = new string('n', StrategyText.NameMaxLength + 1) },
            custom with { Irrigation = "BINARY", Description = new string('d', StrategyText.SummaryMaxLength + 1) },
            custom with { Irrigation = "TERNARY", Name = new string('n', StrategyText.NameMaxLength) },
        ]);

        Assert.Equal(["strategies[0]", "strategies[1]"], errors.Keys.Order());
    }

    [Fact]
    public void BadChoicesAreRefusedOneByOne()
    {
        var (_, errors) = StrategyChoices.Picks([
            new(Preset: "control"),
            new(Preset: "bold-irrigated"),
            new(AllowMissingData: false, AllowAgronomicRisks: true, Irrigation: "OFF", Fertilizer: "OFF"),
            new(AllowMissingData: false, AllowAgronomicRisks: true, AllowSoilDepletion: true, Irrigation: "1", Fertilizer: "OFF"),
        ]);
        Assert.Equal(["strategies[1]", "strategies[2]", "strategies[3]"], errors.Keys.Order());
    }

    [Fact]
    public void ARunHasAtMostTwelveStrategies()
    {
        var (_, errors) = StrategyChoices.Picks(Enumerable.Repeat(new StrategyChoice(Preset: "control"), StrategyCatalog.MaxStrategiesPerRun + 1).ToArray());
        Assert.Contains("strategies", errors.Keys);
    }

    [Fact]
    public void TheCurrentPlanHasNoStrategySettingsSinceItsWaterAndFertilizerArePerCrop() =>
        Assert.Null(StrategySettings.For(StrategyCatalog.BaselineKey));

    [Fact]
    public void RiskSettingsSendSoilDepletion()
    {
        var json = JsonSerializer.Serialize(new RiskSettings(false, true, false));
        Assert.Equal("""{"allow_missing_data":false,"allow_agronomic_risks":true,"allow_soil_depletion":false}""", json);
    }

    [Fact]
    public void IrrigatedStrategiesPriceTheirInputs()
    {
        var irrigated = Optimization("full-inputs").Strategy.Replenishment;
        Assert.Equal("2215", irrigated.Irrigation.MaterialId);
        // felder picks the fertilizer product itself: no N/P/K material ids go out.
        Assert.DoesNotContain("material_id", JsonSerializer.Serialize(irrigated.Fertilizers));
    }

    [Fact]
    public void BaselineIncludesOpenEndedPlanting()
    {
        var request = StrategyRequests.Baseline(FieldId, "2026-12", "2031-10", [Planting(7754, new DateOnly(2027, 4, 10))]);

        Assert.NotNull(request);
        var crop = Assert.Single(request.Schedule);
        Assert.Equal("7754", crop.CropProfileId);
        Assert.Equal(new DateOnly(2027, 4, 10), crop.PlantDate);
        // Rain-fed and unfertilized, as the farmer records no fertilizer or irrigation per planting.
        Assert.Null(crop.FertilizerApplication);
        Assert.Null(crop.IrrigationApplication);
        Assert.False(request.Risk.AllowMissingData);
        Assert.True(request.Risk.AllowAgronomicRisks);
        Assert.True(request.Risk.AllowSoilDepletion);
    }

    [Fact]
    public void BaselineSkipsPlantingsOutsideHorizon()
    {
        Assert.Null(StrategyRequests.Baseline(FieldId, "2026-12", "2031-10",
            [Planting(7754, new DateOnly(2024, 10, 1), new DateOnly(2025, 7, 20)), Planting(7754, new DateOnly(2031, 11, 1))]));
    }

    [Fact]
    public void BaselineOrdersCropsBySowingDate()
    {
        var request = StrategyRequests.Baseline(FieldId, "2026-12", "2031-10",
            [Planting(2, new DateOnly(2029, 4, 1)), Planting(1, new DateOnly(2027, 4, 1))])!;
        Assert.Equal(["1", "2"], request.Schedule.Select(crop => crop.CropProfileId));
    }

    [Fact]
    public void BaselineSendsTheSowingDayAsEnteredNotTheFirstOfTheMonth()
    {
        var json = JsonSerializer.Serialize(StrategyRequests.Baseline(FieldId, "2026-12", "2031-10",
            [Planting(1, new DateOnly(2027, 4, 11)), Planting(2, new DateOnly(2028, 9, 21)), Planting(3, new DateOnly(2029, 5, 1))]));

        Assert.Contains("\"plant_date\":\"2027-04-11\"", json);
        Assert.Contains("\"plant_date\":\"2028-09-21\"", json);
        Assert.Contains("\"plant_date\":\"2029-05-01\"", json);
    }

    [Fact]
    public void SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(Optimization("cash-grab"));
        Assert.Contains("\"start_year_month\":\"2026-12\"", json);
        Assert.Contains("\"allow_agronomic_risks\":true", json);
        Assert.Contains("\"allow_missing_data\":true", json);
        Assert.Contains("\"resolution\":\"CONTINUOUS\"", json);
        Assert.DoesNotContain("n_material_id", json);
        Assert.Contains($"\"field_id\":\"{FieldId}\"", json);

        var baseline = JsonSerializer.Serialize(StrategyRequests.Baseline(FieldId, "2026-12", "2031-10", [Planting(7754, new DateOnly(2027, 4, 10))]));
        Assert.Contains("\"plant_date\":\"2027-04-10\"", baseline);
        Assert.Contains("\"crop_profile_id\":\"7754\"", baseline);
        // felder's 2026-11-13 evaluation contract: no request-wide materials or 0-1 levels, and no empty applications.
        foreach (var gone in new[] { "materials", "irrigation_level", "fertilizer_level", "fertilizer_application", "irrigation_application" })
            Assert.DoesNotContain($"\"{gone}\"", baseline);
    }

    [Fact]
    public void APlannedCropWithInputsSendsThemAsFelderApplications()
    {
        var json = JsonSerializer.Serialize(new ScenarioPlannedCrop("7754", new DateOnly(2027, 4, 10),
            new FelderFertilizerApplication("301", 150), new FelderIrrigationApplication("2215", 80)));

        Assert.Contains("\"fertilizer_application\":{\"material_id\":\"301\",\"rate_kg_ha\":150}", json);
        Assert.Contains("\"irrigation_application\":{\"material_id\":\"2215\",\"amount_mm\":80}", json);
    }

    [Fact]
    public void BaselineSendsEachCropsOwnWaterAndFertilizer()
    {
        var fed = Planting(7754, new DateOnly(2027, 4, 10));
        fed.IrrigationMm = 80; fed.FertilizerMaterialId = 2274; fed.FertilizerRateKgHa = 250;
        var request = StrategyRequests.Baseline(FieldId, "2026-12", "2031-10", [fed, Planting(7755, new DateOnly(2028, 4, 10))], "2215")!;

        Assert.Equal(new FelderFertilizerApplication("2274", 250), request.Schedule[0].FertilizerApplication);
        Assert.Equal(new FelderIrrigationApplication("2215", 80), request.Schedule[0].IrrigationApplication);
        Assert.Null(request.Schedule[1].FertilizerApplication);
        Assert.Null(request.Schedule[1].IrrigationApplication);
    }

    [Fact]
    public void BaselineSendsTheTargetLevelsAPlanCopiedFromAStrategyKeeps()
    {
        var copied = Planting(7754, new DateOnly(2027, 4, 10));
        copied.IrrigationMm = 120; copied.IrrigationTargetPercent = 60;
        copied.FertilizerMaterialId = 2274; copied.FertilizerRateKgHa = 180; copied.FertilizerTargetPercent = 75;

        var request = StrategyRequests.Baseline(FieldId, "2026-12", "2031-10", [copied], "2215")!;

        Assert.Equal(new FelderIrrigationApplication("2215", 120, 60), request.Schedule[0].IrrigationApplication);
        Assert.Equal(new FelderFertilizerApplication("2274", 180, 75), request.Schedule[0].FertilizerApplication);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"irrigation_application\":{\"material_id\":\"2215\",\"amount_mm\":120,\"target_percentage\":60}", json);
        Assert.Contains("\"fertilizer_application\":{\"material_id\":\"2274\",\"rate_kg_ha\":180,\"target_percentage\":75}", json);
    }

    [Fact]
    public void BaselineSendsTheHarvestDateAsTheClearanceDate()
    {
        var request = StrategyRequests.Baseline(FieldId, "2026-12", "2031-10",
            [Planting(7754, new DateOnly(2027, 4, 11), new DateOnly(2027, 8, 20)), Planting(7755, new DateOnly(2028, 4, 1))])!;

        Assert.Equal(new DateOnly(2027, 8, 20), request.Schedule[0].ClearanceDate);
        Assert.Null(request.Schedule[1].ClearanceDate);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"clearance_date\":\"2027-08-20\"", json);
        Assert.Equal(1, json.Split("clearance_date").Length - 1);
    }
}
