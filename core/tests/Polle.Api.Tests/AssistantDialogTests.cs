using Polle.Api.Features.Assistant;
using Polle.Api.Features.Strategies;

namespace Polle.Api.Tests;

/// <summary>
/// The assistant keeps what the conversation established, assumes sensible defaults instead of asking for a format, and
/// runs or reads the field's strategies on its own when asked about them.
/// </summary>
public sealed class AssistantDialogTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly Guid Farm = Guid.NewGuid(), North = Guid.NewGuid(), South = Guid.NewGuid();

    private static AssistantHistoryItem User(string text) => new("user", text);
    private static AssistantHistoryItem Bot(string text) => new("assistant", text);

    [Fact]
    public void AStartMonthAndThenABareNumberOfYearsDoNotLoop()
    {
        // Turn 1: "2026-10" alone. The start is read, the length is assumed, and nothing is asked.
        var first = AssistantPeriod.Resolve("2027-10", [], null, Today);
        Assert.Equal(("2027-10", 5, false, true), (first.StartMonth, first.Years, first.StartAssumed, first.YearsAssumed));
        var context = new AssistantConversationContext(AssistantRouting.PrepareScenario, Farm, first.StartMonth, first.Years);

        // Turn 2: "10" continues the same tool, without the model, and keeps 2026-10.
        Assert.Equal(AssistantRouting.PrepareScenario, AssistantRouting.Preroute("10", context, Today));
        var second = AssistantPeriod.Resolve("10", [User("2027-10"), Bot("No length given, so I use 5 years.")], context, Today);
        Assert.Equal(("2027-10", 10, false, false), (second.StartMonth, second.Years, second.StartAssumed, second.YearsAssumed));
        Assert.Equal("2037-09", second.EndMonth);
    }

    [Fact]
    public void TheHistoryAloneCarriesTheStartWhenTheContextIsLost()
    {
        var period = AssistantPeriod.Resolve("10", [User("2027-10"), Bot("Specify the planning horizon.")], null, Today);

        Assert.Equal(("2027-10", 10), (period.StartMonth, period.Years));
    }

    [Fact]
    public void AStartThatHasPassedMovesToThisMonthAndTheAnswerSaysSo()
    {
        // The screenshot's dialogue, replayed after October 2026: "2026-10", then "10".
        var first = AssistantPeriod.Resolve("2026-10", [], null, Today);
        Assert.Equal(("2026-11", "2026-10"), (first.StartMonth, first.PastStart));
        Assert.Contains("2026-10 уже минув, тож починаю з 2026-11", first.AssumedText(uk: true));
        var context = new AssistantConversationContext(AssistantRouting.PrepareScenario, Farm, first.StartMonth, first.Years);

        var second = AssistantPeriod.Resolve("10", [User("2026-10"), Bot(first.AssumedText(uk: true)!)], context, Today);

        Assert.Equal(("2026-11", 10), (second.StartMonth, second.Years));
        Assert.Null(second.AssumedText(uk: true));
    }

    [Fact]
    public void ANewStartReplacesTheOldOneAndKeepsItsLength()
    {
        var context = new AssistantConversationContext(AssistantRouting.AnalyzeStrategies, Farm, "2026-12", 10, North, Guid.NewGuid());

        var period = AssistantPeriod.Resolve("з березня 2027", [User("Проаналізуй поле на 10 років")], context, Today);

        Assert.Equal(("2027-03", 10), (period.StartMonth, period.Years));
        Assert.Equal(AssistantRouting.AnalyzeStrategies, AssistantRouting.Preroute("з березня 2027", context, Today));
    }

    [Theory]
    [InlineData("Яку стратегію краще обрати для мого поля?")]
    [InlineData("Which strategy is best for my field?")]
    [InlineData("Проаналізуй моє поле на 5 років")]
    [InlineData("Порівняй стратегії для поля Північне")]
    [InlineData("What should I plant on my field next year?")]
    [InlineData("Запусти солвер")]
    public void StrategyQuestionsRunTheStrategyToolWithoutAskingForAFormat(string message)
    {
        Assert.Equal(AssistantRouting.AnalyzeStrategies, AssistantRouting.Preroute(message, null, Today));
        var period = AssistantPeriod.Resolve(message, [], null, Today);
        Assert.Matches(@"^\d{4}-\d{2}$", period.StartMonth);
        Assert.InRange(period.Years, 1, AssistantPeriod.MaxYears);
    }

    [Fact]
    public void WithoutAPeriodTheDefaultsAreNextMonthAndFiveYears()
    {
        var period = AssistantPeriod.Resolve("Яку стратегію краще обрати для мого поля?", [], null, Today);

        Assert.Equal(("2026-12", 5, true, true), (period.StartMonth, period.Years, period.StartAssumed, period.YearsAssumed));
        Assert.Equal("2031-11", period.EndMonth);
    }

    [Fact]
    public void AFieldsLastRunStandsInBeforeTheDefaults()
    {
        var period = AssistantPeriod.Resolve("Які стратегії для поля?", [], null, Today, new PeriodParts("2027-01", 8));

        Assert.Equal(("2027-01", 8, false, false), (period.StartMonth, period.Years, period.StartAssumed, period.YearsAssumed));
    }

    [Theory]
    [InlineData("з жовтня 2026 на 10 років", "2026-11", 10)] // October 2026 is past: next month instead
    [InlineData("з жовтня 2027 на 10 років", "2027-10", 10)]
    [InlineData("Проаналізуй поле на 5 років", "2026-12", 5)]
    [InlineData("з наступного року", "2027-01", 5)]
    [InlineData("from next month for three years", "2026-12", 3)]
    [InlineData("October 2027 for five years", "2027-10", 5)]
    [InlineData("починаючи з 2028 на п'ять років", "2028-01", 5)]
    [InlineData("на 36 місяців з 2027-04", "2027-04", 3)]
    [InlineData("з осені", "2027-09", 5)]
    [InlineData("з березня", "2027-03", 5)]
    [InlineData("на рік з грудня", "2026-12", 1)]
    [InlineData("до 2030 року", "2026-12", 5)]
    public void DatesAreReadFromNaturalLanguage(string message, string start, int years)
    {
        var period = AssistantPeriod.Resolve(message, [], null, Today);

        Assert.Equal((start, years), (period.StartMonth, period.Years));
    }

    [Fact]
    public void ALengthAboveTheLimitIsClamped() => Assert.Equal(AssistantPeriod.MaxYears, AssistantPeriod.Resolve("на 25 років", [], null, Today).Years);

    [Theory]
    [InlineData("10", true)]
    [InlineData("2026-10", true)]
    [InlineData("на 10 років", true)]
    [InlineData("з жовтня 2027", true)]
    [InlineData("for 7 years", true)]
    [InlineData("Проаналізуй поле", false)]
    [InlineData("What is crop rotation?", false)]
    public void APeriodOnlyReplyIsRecognised(string message, bool periodOnly) =>
        Assert.Equal(periodOnly, AssistantPeriod.IsPeriodOnly(message, Today));

    [Theory]
    [InlineData("Prepare a solver scenario draft.")]
    [InlineData("Покажи JSON сценарію")]
    [InlineData("What is crop rotation?")]
    [InlineData("What farms do I have?")]
    [InlineData("10")]
    public void OtherRequestsAreLeftToTheModel(string message) => Assert.Null(AssistantRouting.Preroute(message, null, Today));

    [Fact]
    public void AnotherPeriodAfterAnAnalysisReRunsIt()
    {
        var context = new AssistantConversationContext(AssistantRouting.AnalyzeStrategies, Farm, "2026-12", 5, North, Guid.NewGuid());

        Assert.Equal(AssistantRouting.AnalyzeStrategies, AssistantRouting.Preroute("А якщо на 10 років?", context, Today));
        Assert.Equal(AssistantRouting.AnalyzeStrategies, AssistantRouting.Preroute("10", context, Today));
    }

    private static readonly AssistantStrategyField[] Fields =
    [
        new(North, Farm, "Home", "Північне", 40, null),
        new(South, Farm, "Home", "Південне", 25, null),
        new(Guid.NewGuid(), Farm, "Home", "Північне-2", 5, North),
    ];

    [Fact]
    public void TheFieldIsTheOneNamedThenTheOpenOneThenTheConversationsThenTheOnlyOne()
    {
        Assert.Equal(South, AssistantStrategyTool.PickField("стратегії для поля Південне", Fields, null, North, null).Field?.Id);
        Assert.Equal(Fields[2].Id, AssistantStrategyTool.PickField("стратегії для поля Північне-2", Fields, null, null, null).Field?.Id);
        Assert.Equal(North, AssistantStrategyTool.PickField("Яку стратегію обрати?", Fields, Farm, North, null).Field?.Id);
        Assert.Equal(South, AssistantStrategyTool.PickField("А на 10 років?", Fields, Farm, null, South).Field?.Id);
        Assert.Equal(North, AssistantStrategyTool.PickField("Яку стратегію обрати?", Fields[..1], null, null, null).Field?.Id);
        var unclear = AssistantStrategyTool.PickField("Яку стратегію обрати?", Fields, Farm, null, null);
        Assert.Equal("ambiguous", unclear.Status);
        Assert.Equal(2, unclear.Choices.Length); // subfields aren't offered
    }

    private static StrategyResult Result(decimal profit, double irrigation, string[] risks, params string[] crops) => new("OPTIMAL", profit, profit + 1000, 1000,
        irrigation, 0, crops.Select(crop => new StrategyCycle(1, 1, crop, "", "2027-04", "2027-08", ["2027-08"], profit + 1000, 1000, 100, irrigation, [],
            new CycleNutrients(0, 0, 0, 0, 0, 0, 0), risks, [], [], [])).ToArray(), risks, [], [], null);

    [Fact]
    public void TheAnswerNamesTheStrategiesWithTheirNumbersAndRecommendsOne()
    {
        var run = new StrategyRunResponse(Guid.NewGuid(), North, "2026-12", "2031-11", DateTime.UtcNow,
        [
            new(StrategyCatalog.BaselineKey, "Your current plan", "", StrategyJobs.Ready,
                Result(2000, 0, ["Drought: rain covers only 60%"], "Wheat") with { Status = PlanAudit.EstimatedStatus }, null, Estimated: true),
            new("full-inputs", "Full inputs", "", StrategyJobs.Ready, Result(9000, 300, ["Heat at flowering", "Rotation"], "Maize", "Soybean"), null),
            new("control", "Control", "", StrategyJobs.Ready, Result(5000, 0, [], "Wheat", "Peas"), null),
            new("cash-grab", "Cash grab", "", StrategyJobs.Processing, null, null),
        ]);
        var period = AssistantPeriod.Resolve("Яку стратегію обрати?", [], null, Today);

        var text = StrategyRunDigest.Text(run, "Північне", 40, period, uk: true, started: true);

        Assert.StartsWith("### Плани для поля «Північне» · 2026-12 – 2031-11\n", text);
        Assert.Contains("**Період:** з 2026-12 на 5 років (до 2031-11).", text);
        Assert.Contains("Початок не вказано", text);
        Assert.Contains("**Готово:** 3 з 4", text);
        Assert.Contains("*Оцінка за довідником культур", text);
        Assert.Contains("- Прибуток: **$2,000**", text);
        Assert.Contains("1. **Full inputs**: прибуток **$9,000** (+$7,000 до поточного плану)", text);
        Assert.Contains("   - Витрати $1,000 · полив 300 мм (120,000 м³) · ризиків 2", text);
        Assert.Contains("   - Культури: Maize → Soybean", text);
        Assert.Contains("Рекомендую **«Full inputs»**", text);
        Assert.Contains("Менше ризиків — «Control»", text);
        Assert.Contains("- Головні ризики «Full inputs»: Heat at flowering · Rotation", text);
        Assert.Contains("- Ще рахуються: Cash grab", text);
        // Sections in order, each its own block after a blank line.
        var sections = new[] { "\n\n**Поточний план**\n", "\n\n**Найкращі варіанти**\n", "\n\n**Недоступні / ще рахуються**\n", "\n\n**Рекомендація**\n" };
        var positions = sections.Select(section => text.IndexOf(section, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position > 0));
        Assert.Equal(positions.Order(), positions);
        Assert.True(text.IndexOf("**Full inputs**", StringComparison.Ordinal) < text.IndexOf("**Control**", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEarlierRunIsDatedAndCanBeRecalculated()
    {
        var run = new StrategyRunResponse(Guid.NewGuid(), North, "2027-01", "2031-12", DateTime.UtcNow,
            [new("control", "Control", "", StrategyJobs.Ready, Result(5000, 0, [], "Wheat"), null)]);

        var text = StrategyRunDigest.Text(run, "North", 40, new ResolvedPeriod("2027-01", 5, false, false), uk: false, started: false, new DateOnly(2026, 11, 10));

        Assert.StartsWith("### Plans for North · 2027-01 – 2031-12\n", text);
        Assert.Contains("from 2027-01 for 5 years (to 2031-12)", text);
        Assert.Contains("calculation from 2026-11-10", text);
        Assert.Contains("I'd choose **Control**", text);
        Assert.Contains("Your plan has no crops sown in this period", text);
        Assert.DoesNotContain("Unavailable / In progress", text);
        Assert.DoesNotContain("No period given", text);
        Assert.DoesNotContain("strateg", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheBrowserMaySendBackOnlyAContextThisApiMade()
    {
        Assert.True(AssistantEndpoints.ValidContext(new(AssistantRouting.AnalyzeStrategies, Farm, "2026-12", 5, North, Guid.NewGuid())));
        Assert.True(AssistantEndpoints.ValidContext(new(AssistantRouting.PrepareScenario, Farm, "2026-10", 10)));
        Assert.True(AssistantEndpoints.ValidContext(new("get_farm_summary", Farm, null, null)));
        Assert.False(AssistantEndpoints.ValidContext(new(AssistantRouting.AnalyzeStrategies, Farm, "2026-12", 5)));
        Assert.False(AssistantEndpoints.ValidContext(new(AssistantRouting.PrepareScenario, Farm, "2026-13", 5)));
        Assert.False(AssistantEndpoints.ValidContext(new(AssistantRouting.PrepareScenario, Farm, "2026-10", 21)));
        Assert.False(AssistantEndpoints.ValidContext(new("delete_everything", Farm, null, null)));
    }
}
