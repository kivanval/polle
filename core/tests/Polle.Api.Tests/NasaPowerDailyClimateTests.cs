using System.Net;
using System.Text.Json;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Weather;
using Microsoft.Extensions.Configuration;

namespace Polle.Api.Tests;

public sealed class NasaPowerDailyClimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static DailyClimateBaseline Parse(string json, int startYear, int endYear)
    {
        using var document = JsonDocument.Parse(json);
        return NasaPowerDailyClimate.Parse(document.RootElement, startYear, endYear, Now);
    }

    [Fact]
    public void Parse_averages_each_calendar_day_over_the_reference_years()
    {
        // 2023 and 2024: each day's values differ by year, so the mean is checkable.
        var json = PowerDaily.Json(2023, 2024, day => day.Year == 2023 ? (10, 4, 16, 0) : (12, 6, 20, 3));

        var baseline = Parse(json, 2023, 2024);

        Assert.Equal((2023, 2024), (baseline.ReferenceStartYear, baseline.ReferenceEndYear));
        Assert.Equal(365, baseline.Days.Count);
        Assert.Equal(new DailyClimateNormal(5, 18, 11, 1.5), baseline.For(new DateOnly(2030, 7, 14)));
        Assert.Equal("v2.10.2", baseline.ApiVersion);
        Assert.Equal(Now, baseline.RetrievedAtUtc);
    }

    [Fact]
    public void Parse_ignores_february_29_and_february_29_takes_february_28()
    {
        // Only 2024's Feb 29 is extreme; it must not enter any mean.
        var json = PowerDaily.Json(2023, 2024, day => day is { Month: 2, Day: 29 } ? (40, 39, 41, 99) : (1, 0, 2, 0));

        var baseline = Parse(json, 2023, 2024);

        Assert.DoesNotContain(DailyClimateBaseline.Key(2, 29), baseline.Days.Keys);
        Assert.Equal(baseline.For(new DateOnly(2028, 2, 28)), baseline.For(new DateOnly(2028, 2, 29)));
        Assert.Equal(new DailyClimateNormal(0, 2, 1, 0), baseline.For(new DateOnly(2028, 2, 29)));
    }

    [Fact]
    public void Parse_rounds_means_to_hundredths()
    {
        var json = PowerDaily.Json(2021, 2023, day => day.Year switch
        {
            2021 => (10, 5, 15, 1),
            2022 => (10, 5, 15, 1),
            _ => (10, 5, 15, 0)
        });

        Assert.Equal(0.67, Parse(json, 2021, 2023).For(new DateOnly(2030, 1, 1)).PrecipitationMm);
    }

    public static TheoryData<string, Action<Dictionary<string, object>>?, PowerDaily.Weather> Unusable => new()
    {
        { "fill value", null, day => day == new DateOnly(2024, 6, 1) ? (-999, 5, 15, 2) : PowerDaily.Steady(day) },
        { "min above mean", null, day => day == new DateOnly(2024, 6, 1) ? (10, 11, 15, 2) : PowerDaily.Steady(day) },
        { "mean above max", null, day => day == new DateOnly(2024, 6, 1) ? (16, 5, 15, 2) : PowerDaily.Steady(day) },
        { "negative precipitation", null, day => day == new DateOnly(2024, 6, 1) ? (10, 5, 15, -1) : PowerDaily.Steady(day) },
        { "temperature unit", root => ((Dictionary<string, object>)root["parameters"])["T2M_MAX"] = new Dictionary<string, string> { ["units"] = "K" }, PowerDaily.Steady },
        { "precipitation unit", root => ((Dictionary<string, object>)root["parameters"])["PRECTOTCORR"] = new Dictionary<string, string> { ["units"] = "mm" }, PowerDaily.Steady },
        { "missing parameter", root => ((Dictionary<string, object>)root["parameters"]).Remove("T2M_MIN"), PowerDaily.Steady },
        { "missing header", root => root.Remove("header"), PowerDaily.Steady },
    };

    [Theory]
    [MemberData(nameof(Unusable))]
    public void Parse_rejects_unusable_data_without_imputing(string problem, Action<Dictionary<string, object>>? edit,
        PowerDaily.Weather weather)
    {
        var json = PowerDaily.Json(2023, 2024, weather, edit);

        Assert.NotEmpty(problem);
        Assert.Throws<WeatherDataUnavailableException>(() => Parse(json, 2023, 2024));
    }

    [Fact]
    public void Parse_rejects_a_missing_day()
    {
        // The response covers 2023 only, but 2023-2024 was asked for.
        var json = PowerDaily.Json(2023, 2023, PowerDaily.Steady);

        Assert.Throws<WeatherDataUnavailableException>(() => Parse(json, 2023, 2024));
    }

    [Theory]
    [InlineData("2026-11-05", 2025)]
    [InlineData("2026-07-01", 2025)]
    [InlineData("2026-06-30", 2024)]
    [InlineData("2027-03-15", 2025)]
    [InlineData("2027-01-01", 2025)]
    public void Reference_years_end_with_the_latest_year_that_ended_six_months_ago(string today, int expectedEnd)
    {
        var now = DateTimeOffset.Parse(today + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expectedEnd, NasaPowerDailyClimate.ReferenceEndYear(now));
    }

    private static NasaPowerDailyClimate Client(StubHandler handler, DailyClimateBaselineCache? cache = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://power.test/") }, cache ?? new DailyClimateBaselineCache(),
            new FixedTimeProvider(Now), new ConfigurationBuilder().Build());

    [Fact]
    public async Task GetAsync_asks_for_ten_completed_years_of_daily_data_at_the_point()
    {
        var handler = new StubHandler(_ => PowerDaily.Ok(PowerDaily.Json(2016, 2025, PowerDaily.Steady)));

        var baseline = await Client(handler).GetAsync(49.5, 31.25, CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("/api/temporal/daily/point", uri.AbsolutePath);
        Assert.Equal("?parameters=T2M,T2M_MIN,T2M_MAX,PRECTOTCORR&community=AG&format=JSON&latitude=49.5&longitude=31.25&start=20160101&end=20251231",
            uri.Query);
        Assert.Equal((2016, 2025), (baseline.ReferenceStartYear, baseline.ReferenceEndYear));
    }

    [Fact]
    public async Task GetAsync_serves_repeat_and_concurrent_requests_from_one_fetch()
    {
        var handler = new StubHandler(_ => PowerDaily.Ok(PowerDaily.Json(2016, 2025, PowerDaily.Steady)));
        var client = Client(handler);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync(49.5, 31.25, CancellationToken.None)));
        await client.GetAsync(49.5, 31.25, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_fetches_each_point_separately()
    {
        var handler = new StubHandler(_ => PowerDaily.Ok(PowerDaily.Json(2016, 2025, PowerDaily.Steady)));
        var client = Client(handler);

        await client.GetAsync(49.5, 31.25, CancellationToken.None);
        await client.GetAsync(49.5, 31.26, CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_retries_server_errors_then_reports_the_upstream_as_unavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<WeatherUpstreamException>(() => Client(handler).GetAsync(49.5, 31.25, CancellationToken.None));
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_does_not_retry_a_rejected_request_and_does_not_cache_failures()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        var client = Client(handler);

        await Assert.ThrowsAsync<WeatherUpstreamException>(() => client.GetAsync(49.5, 31.25, CancellationToken.None));
        await Assert.ThrowsAsync<WeatherUpstreamException>(() => client.GetAsync(49.5, 31.25, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetAsync_reports_invalid_json_as_upstream_failure()
    {
        var handler = new StubHandler(_ => PowerDaily.Ok("{not json"));

        await Assert.ThrowsAsync<WeatherUpstreamException>(() => Client(handler).GetAsync(49.5, 31.25, CancellationToken.None));
    }

    [Theory]
    [InlineData(90, 0)]
    [InlineData(-90, 0)]
    [InlineData(0, 181)]
    [InlineData(double.NaN, 0)]
    public async Task GetAsync_rejects_points_outside_wgs84(double latitude, double longitude)
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("no request expected"));

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetAsync(latitude, longitude, CancellationToken.None));
    }
}
