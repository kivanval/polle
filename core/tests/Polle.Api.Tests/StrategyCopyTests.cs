using Polle.Api.Features.Strategies;

namespace Polle.Api.Tests;

/// <summary>Strategy names and summaries are shown as written, so they read as plain sentences with no semicolons.</summary>
public sealed class StrategyCopyTests
{
    [Fact]
    public void PresetSummariesHaveNoSemicolons()
    {
        Assert.All(StrategyCatalog.Presets, preset =>
        {
            Assert.DoesNotContain(';', preset.Name);
            Assert.DoesNotContain(';', preset.Summary);
        });
    }

    public static TheoryData<bool, bool, bool> Flags()
    {
        var data = new TheoryData<bool, bool, bool>();
        foreach (var missing in new[] { false, true })
            foreach (var risks in new[] { false, true })
                foreach (var soil in new[] { false, true })
                    data.Add(missing, risks, soil);
        return data;
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void CustomStrategySummariesHaveNoSemicolons(bool missing, bool risks, bool soil)
    {
        foreach (var irrigation in Enum.GetValues<ReplenishmentLevel>())
            foreach (var fertilizer in Enum.GetValues<ReplenishmentLevel>())
            {
                var custom = StrategyCatalog.Find(StrategyCatalog.CustomKey(new StrategyOptions(missing, risks, soil, irrigation, fertilizer)));
                Assert.NotNull(custom);
                Assert.DoesNotContain(';', custom.Name);
                Assert.DoesNotContain(';', custom.Summary);
                // Two sentences: the second starts with a capital.
                var second = custom.Summary.Split(". ", 2)[1];
                Assert.True(char.IsUpper(second[0]), custom.Summary);
            }
    }
}
