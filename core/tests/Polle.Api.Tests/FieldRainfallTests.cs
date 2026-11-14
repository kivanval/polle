using Polle.Api.Features.Climate;

namespace Polle.Api.Tests;

public sealed class FieldRainfallTests
{
    [Fact]
    public void MonthlyRain_sums_each_calendar_months_daily_normals()
    {
        // 1 mm every January day, 2 mm every other day of the common year.
        var days = Enumerable.Range(0, 365).Select(offset => new DateOnly(2025, 1, 1).AddDays(offset))
            .ToDictionary(day => DailyClimateBaseline.Key(day.Month, day.Day), day => new DailyClimateNormal(0, 10, 5, day.Month == 1 ? 1 : 2));
        var baseline = new DailyClimateBaseline(2015, 2024, days, DateTimeOffset.UnixEpoch, null);

        var rain = FieldRainfallEndpoints.MonthlyRain(baseline);

        Assert.Equal([31, 56, 62, 60, 62, 60, 62, 62, 60, 62, 60, 62], rain);
    }
}
