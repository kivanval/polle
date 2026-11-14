using System.Text.Json;
using Polle.Api.Common;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Fields;

namespace Polle.Api.Tests;

public sealed class SolverContractTests
{
    [Theory]
    [InlineData("2026-04-01", "2031-10-31", true)]
    [InlineData("2026-04-01", "2026-04-01", true)]
    [InlineData("2026-04-02", "2026-04-01", false)]
    [InlineData("2026-04-01", "2056-03-31", true)]
    [InlineData("2026-04-01", "2056-04-01", false)]
    [InlineData("1899-12-31", "1900-01-02", false)]
    [InlineData("2200-01-01", "2201-01-01", false)]
    [InlineData("2026-4-1", "2026-04-02", false)]
    [InlineData("2026-02-30", "2026-03-01", false)]
    [InlineData("01/04/2026", "2026-04-02", false)]
    [InlineData(null, "2026-04-02", false)]
    [InlineData("2026-04-01", null, false)]
    public void Climate_period_validation(string? start, string? end, bool valid)
    {
        var errors = ClimateDailyEndpoints.Validate(start, end, out _, out _);

        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void Climate_timeline_has_one_record_per_day_including_leap_days()
    {
        var days = new Dictionary<int, DailyClimateNormal>();
        for (var day = new DateOnly(2025, 1, 1); day.Year == 2025; day = day.AddDays(1))
            days[DailyClimateBaseline.Key(day.Month, day.Day)] = new(day.Month, day.Month + 10, day.Month + 5, day.Day);
        var baseline = new DailyClimateBaseline(2016, 2025, days, DateTimeOffset.UnixEpoch, null);

        var timeline = ClimateDailyEndpoints.Timeline(baseline, new DateOnly(2028, 2, 27), new DateOnly(2028, 3, 1));

        Assert.Equal([new(2028, 2, 27), new(2028, 2, 28), new(2028, 2, 29), new(2028, 3, 1)], timeline.Select(item => item.Date));
        Assert.Equal(timeline[1] with { Date = new DateOnly(2028, 2, 29) }, timeline[2]);
        Assert.Equal(new DailyClimateRecord(new DateOnly(2028, 3, 1), 3, 13, 8, 1), timeline[3]);
    }

    [Fact]
    public void Climate_response_uses_the_contract_names()
    {
        var body = new FieldClimate("fld_01", [new DailyClimateRecord(new DateOnly(2026, 4, 15), 4.2, 18.6, 11.4, 1.5)]);

        Assert.Equal("""{"field_id":"fld_01","timeline":[{"date":"2026-04-15","t_min":4.2,"t_max":18.6,"t_mean":11.4,"precip":1.5}]}""",
            JsonSerializer.Serialize(body));
    }

    [Fact]
    public void Field_topology_response_uses_the_contract_names_and_explicit_nulls()
    {
        var body = new FieldTopology("3f1c2a9e-0b7d-4c55-9a51-2f0e6d1c8b40", "North Valley", 120.5, null, null, []);

        Assert.Equal("""{"id":"3f1c2a9e-0b7d-4c55-9a51-2f0e6d1c8b40","name":"North Valley","area_ha":120.5,"initial_depletion_mm":null,"soil":null,"historical_variants":[]}""",
            JsonSerializer.Serialize(body));
    }

    [Theory]
    [InlineData("2026-04", true)]
    [InlineData("2026-12", true)]
    [InlineData("2026-00", false)]
    [InlineData("2026-13", false)]
    [InlineData("2026-4", false)]
    [InlineData("26-04", false)]
    [InlineData("2026-04\n", false)]
    [InlineData("２０２６-04", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Year_month_validation(string? value, bool valid) =>
        Assert.Equal(valid, FieldTopologyEndpoints.IsYearMonth(value));

    [Theory]
    [InlineData("key", "key", false, true)]
    [InlineData("key", "other", false, false)]
    [InlineData("key", "", false, false)]
    [InlineData("key", "other", true, false)]
    [InlineData(null, "", true, true)]
    [InlineData("", "anything", true, true)]
    [InlineData(null, "", false, false)]
    [InlineData("", "", false, false)]
    public void Service_key_rule_matches_finance(string? configured, string supplied, bool development, bool allowed) =>
        Assert.Equal(allowed, SolverServiceKey.Allows(configured, supplied, development));
}
