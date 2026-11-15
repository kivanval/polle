using Polle.Api.Features.Assistant;
using Polle.Api.Features.Drought;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

/// <summary>
/// Questions about hybrids and varieties go straight to the hybrid tool, which names the crops asked about (or the
/// field's), lists only hybrids from the database, and puts drought-tolerant ones first when the field looks dry.
/// </summary>
public sealed class AssistantHybridTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly Crop Maize = new() { Id = 7706, Name = "Maize", NameUk = "Кукурудза", ScientificName = "Zea mays" };
    private static readonly Crop Sunflower = new() { Id = 7722, Name = "Sunflower", NameUk = "Соняшник", ScientificName = "Helianthus annuus" };
    private static readonly Crop Lentil = new() { Id = 7753, Name = "Lentil", NameUk = "Сочевиця", ScientificName = "Lens culinaris" };

    private static CropHybrid Hybrid(long id, Crop crop, string name, string drought, string heat) => new()
    {
        Id = id, CropId = crop.Id, Crop = crop, Name = name, MaturityGroup = "FAO 300", DroughtTolerance = drought,
        HeatTolerance = heat, KeyTraits = $"{name} traits.", RecommendedConditions = $"{name} conditions."
    };

    private static readonly CropHybrid[] Hybrids =
    [
        Hybrid(1, Maize, "DKC 4351", CropHybrid.Medium, CropHybrid.Medium),
        Hybrid(2, Maize, "P9241", CropHybrid.High, CropHybrid.Medium),
        Hybrid(3, Maize, "SY Torino", CropHybrid.High, CropHybrid.High),
        Hybrid(4, Sunflower, "NK Neoma", CropHybrid.High, CropHybrid.High),
    ];

    private static TrendCell Month(int droughtClass, double tmean, double tmax, double balance, string status = "ok") =>
        new("2027-01", status, 0.5, droughtClass, tmean, tmean - 5, tmax, 40, 40 - balance, balance);

    [Theory]
    [InlineData("Which maize hybrids suit a dry field?")]
    [InlineData("Recommend hybrids for my field")]
    [InlineData("What sunflower varieties should I sow?")]
    [InlineData("Any drought-tolerant cultivars?")]
    [InlineData("Які гібриди кукурудзи посіяти?")]
    [InlineData("Порадь сорти пшениці для посухи")]
    [InlineData("Який сорт сої взяти?")]
    public void HybridQuestionsGoToTheHybridTool(string message) =>
        Assert.Equal(AssistantRouting.RecommendHybrids, AssistantRouting.Preroute(message, null, Today));

    [Theory]
    [InlineData("Як працює сортування полів?")]
    [InlineData("Which plan is best for my field?")]
    public void OtherWordsDoNotLookLikeHybrids(string message) =>
        Assert.NotEqual(AssistantRouting.RecommendHybrids, AssistantRouting.Preroute(message, null, Today));

    [Theory]
    [InlineData("Which corn hybrids?", "Maize")]
    [InlineData("гібриди кукурудзи", "Maize")]
    [InlineData("сорти соняшнику для степу", "Sunflower")]
    [InlineData("sunflower and maize hybrids", "Maize,Sunflower")]
    [InlineData("hybrids for my field", "")]
    public void CropsAreFoundByTheirNamesInEitherLanguage(string message, string expected) =>
        Assert.Equal(expected, string.Join(",", AssistantHybridTool.CropsNamed(message, [Maize, Sunflower, Lentil]).Select(crop => crop.Name)));

    [Fact]
    public void ADryFieldIsRatedFromItsDroughtMonthsAndWarmSeasonBalance()
    {
        var wet = FieldDryness.From([Month(-1, 18, 25, 10), Month(0, 20, 27, -40), Month(-1, 2, 5, -200)]);
        Assert.Equal((CropHybrid.Low, 0, false), (wet.Level, wet.DroughtMonths, wet.Hot));
        Assert.Equal(-30, wet.WarmBalanceMm); // the cold month's deficit does not count

        var dry = FieldDryness.From([Month(1, 22, 31, -90), Month(2, 24, 33, -110), Month(-1, 18, 26, -20)]);
        Assert.Equal((CropHybrid.Medium, 2, true), (dry.Level, dry.DroughtMonths, dry.Hot));

        Assert.Equal(CropHybrid.High, FieldDryness.From([Month(1, 22, 30, -80), Month(1, 23, 31, -80), Month(3, 25, 34, -90)]).Level);
        Assert.Equal(CropHybrid.High, FieldDryness.From([Month(-1, 22, 28, -320)]).Level);
        Assert.Equal(CropHybrid.High, FieldDryness.From([Month(-1, 5, 12, 0, "arid")]).Level);
    }

    [Fact]
    public void ADryHotFieldGetsDroughtAndThenHeatTolerantHybridsFirst()
    {
        var dryness = new FieldDryness(3, -260, false, true, CropHybrid.High);

        var text = AssistantHybridTool.Text([Maize], Hybrids, "North", dryness, droughtAsked: false, uk: false);

        Assert.StartsWith("### Hybrids for North", text);
        Assert.Contains("**Field climate, next 12 months:** high drought risk; 3 months of moderate drought or worse; warm-season water balance −260 mm.", text);
        Assert.Contains("*Drought-tolerant hybrids come first, then heat-tolerant ones.*", text);
        Assert.True(text.IndexOf("SY Torino", StringComparison.Ordinal) < text.IndexOf("P9241", StringComparison.Ordinal));
        Assert.True(text.IndexOf("P9241", StringComparison.Ordinal) < text.IndexOf("DKC 4351", StringComparison.Ordinal));
        Assert.Contains("- **P9241** · FAO 300 · drought tolerance: high · heat tolerance: medium", text);
        Assert.Contains("  - Suits: P9241 conditions.", text);
        Assert.DoesNotContain("NK Neoma", text);
        Assert.EndsWith("Check local adaptation with your agronomist or seed dealer before you buy.*", text);
    }

    [Fact]
    public void WithoutAFieldOrADroughtQuestionTheCatalogOrderStays()
    {
        var text = AssistantHybridTool.Text([Maize, Lentil], Hybrids, null, null, droughtAsked: false, uk: false);

        Assert.StartsWith("### Hybrids", text);
        Assert.DoesNotContain("Field climate", text);
        Assert.DoesNotContain("come first", text);
        Assert.True(text.IndexOf("DKC 4351", StringComparison.Ordinal) < text.IndexOf("P9241", StringComparison.Ordinal));
        Assert.Contains("**Lentil**\n- No hybrids recorded for this crop yet.", text);
    }

    // A stored felder result reduced to what the hybrid tool reads: the crop profile of each cycle.
    private static string Result(params long[] profileIds) =>
        $$"""{"status":"OPTIMAL","schedule":[{{string.Join(",", profileIds.Select(id => $$"""{"crop_profile_id":"{{id}}","plant_date":"2027-04-11"}"""))}}]}""";

    [Fact]
    public void WithNothingPlantedTheHybridsFollowTheSuggestedPlansJustMade()
    {
        var choice = AssistantHybridTool.ChoosePlanProfiles([], [Result(101, 102), Result(102, 103), null, "not json"], Result(201), [301]);

        Assert.Equal(PlanCropSource.SuggestedPlans, choice!.Source);
        Assert.Equal([101L, 102, 103], choice.ProfileIds);
    }

    [Fact]
    public void WithoutSuggestionsTheLastSavedPlanComesBeforeThePastSeason()
    {
        var choice = AssistantHybridTool.ChoosePlanProfiles([], [], Result(201, 202), [301]);

        Assert.Equal(PlanCropSource.SavedPlan, choice!.Source);
        Assert.Equal([201L, 202], choice.ProfileIds);
    }

    [Fact]
    public void PlantingsStillGrowingWinAndThePastSeasonIsTheLastResort()
    {
        var current = AssistantHybridTool.ChoosePlanProfiles([401, 402, 401], [Result(101)], Result(201), [301]);
        Assert.Equal(PlanCropSource.CurrentPlan, current!.Source);
        Assert.Equal([401L, 402], current.ProfileIds);

        var past = AssistantHybridTool.ChoosePlanProfiles([], [Result()], Result(), [301, 302]);
        Assert.Equal(PlanCropSource.LastSeason, past!.Source);
        Assert.Equal([301L, 302], past.ProfileIds);

        Assert.Null(AssistantHybridTool.ChoosePlanProfiles([], [], null, []));
    }

    [Fact]
    public void WhenThePlanIsAskedAboutTheSuggestionsAndTheSavedPlanComeBeforeThePlantings()
    {
        var suggested = AssistantHybridTool.ChoosePlanProfiles([401], [Result(101)], Result(201), [301], planAsked: true);
        Assert.Equal(PlanCropSource.SuggestedPlans, suggested!.Source);
        Assert.Equal([101L], suggested.ProfileIds);

        var saved = AssistantHybridTool.ChoosePlanProfiles([401], [], Result(201), [301], planAsked: true);
        Assert.Equal(PlanCropSource.SavedPlan, saved!.Source);

        var planted = AssistantHybridTool.ChoosePlanProfiles([401], [], null, [301], planAsked: true);
        Assert.Equal(PlanCropSource.CurrentPlan, planted!.Source);
    }

    [Theory]
    [InlineData("Which hybrids suit this plan?", true)]
    [InlineData("hybrids for the suggested plans", true)]
    [InlineData("Hybrids for my strategy", true)]
    [InlineData("Гібриди для цього плану", true)]
    [InlineData("гібриди для запропонованих планів", true)]
    [InlineData("гібриди під стратегію", true)]
    [InlineData("Which hybrids suit my field?", false)]
    [InlineData("гібриди для плантації", false)]
    [InlineData("hybrids for my planting", false)]
    public void APlanIsAskedAboutOnlyWhenTheMessageNamesOne(string message, bool asked) =>
        Assert.Equal(asked, AssistantHybridTool.PlanAsked(message));

    [Theory]
    [InlineData(PlanCropSource.SuggestedPlans, false, "### Hybrids for suggested plan (North)")]
    [InlineData(PlanCropSource.SavedPlan, false, "### Hybrids for saved plan (North)")]
    [InlineData(PlanCropSource.CurrentPlan, false, "### Hybrids for North")]
    [InlineData(PlanCropSource.SuggestedPlans, true, "### Гібриди для запропонованого плану (North)")]
    [InlineData(PlanCropSource.SavedPlan, true, "### Гібриди для збереженого плану (North)")]
    public void TheHeadingNamesThePlanTheCropsCameFrom(PlanCropSource source, bool uk, string heading)
    {
        var text = AssistantHybridTool.Text([Maize], Hybrids, "North", null, droughtAsked: false, uk, source);

        Assert.Equal(heading, text.Split('\n')[0]);
    }

    [Theory]
    [InlineData(PlanCropSource.CurrentPlan, false, "*Based on your current field plan.*")]
    [InlineData(PlanCropSource.SuggestedPlans, false, "*Based on the suggested plans.*")]
    [InlineData(PlanCropSource.SavedPlan, false, "*Based on your saved plan.*")]
    [InlineData(PlanCropSource.LastSeason, false, "*Based on the field's last season.*")]
    [InlineData(PlanCropSource.SuggestedPlans, true, "*За запропонованими планами.*")]
    public void TheAnswerSaysWhichPlanTheCropsCameFrom(PlanCropSource source, bool uk, string line)
    {
        var text = AssistantHybridTool.Text([Maize], Hybrids, "North", null, droughtAsked: false, uk, source);

        Assert.Equal(line, text.Split('\n')[1]);
    }

    [Fact]
    public void AUkrainianQuestionIsAnsweredInUkrainian()
    {
        var text = AssistantHybridTool.Text([Sunflower], Hybrids, null, null, droughtAsked: true, uk: true);

        Assert.StartsWith("### Гібриди: Соняшник", text);
        Assert.Contains("*Спершу посухостійкі гібриди.*", text);
        Assert.Contains("посухостійкість: висока · жаростійкість: висока", text);
        Assert.Contains("перевірте локальну адаптацію з агрономом", text);
    }
}
