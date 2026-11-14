using System.Globalization;

namespace Polle.Api.Features.Weather;

public sealed record ClimateScenarioPoint(double Latitude, double Longitude);

public sealed record ClimateScenarioMonth(string Month, double AvgTempC, double MinTempC,
    double MaxTempC, double PrecipitationMm, int ReferenceYearCount);

public sealed record ClimateScenarioSourceMonth(string Month, double AvgTempC, double MinTempC,
    double MaxTempC, double PrecipitationMm);

public sealed record ClimateScenarioResult(ClimateScenarioMonth[] Months,
    ClimateScenarioSourceMonth[] HistoricalSourceMonths, DateTimeOffset RetrievedAtUtc,
    string? ApiVersion, bool CacheHit);

public sealed class ClimateScenarioService(NasaPowerHistoricalWeather weather)
{
    public const string ScenarioType = "historical_10_year_monthly_baseline";
    public const string Source = "NASA POWER monthly point API";
    public const string Aggregation = "For each calendar month, arithmetic mean of ten NASA POWER monthly T2M, T2M_MIN, T2M_MAX and PRECTOTCORR_SUM values. PRECTOTCORR_SUM is used directly as a monthly total in mm.";
    public const string Interpretation = "Historical planning assumption, not a future weather forecast or a WMO 30-year climatological normal. Mean monthly minimum and maximum temperatures are not decade-wide extremes.";
    public const int ReferenceYearCount = 10;

    public async Task<ClimateScenarioResult> GetAsync(double latitude, double longitude,
        DateOnly targetStartMonth, int referenceStartYear, CancellationToken ct)
    {
        var referenceStart = new DateOnly(referenceStartYear, 1, 1);
        var referenceEnd = new DateOnly(referenceStartYear + ReferenceYearCount - 1, 12, 1);
        var historical = await weather.GetAsync(latitude, longitude, referenceStart, referenceEnd, ct);
        var source = historical.Months;
        if (source.Length != ReferenceYearCount * 12)
            throw new WeatherDataUnavailableException("NASA POWER did not provide all 120 reference months.");

        var avgSums = new double[12];
        var minSums = new double[12];
        var maxSums = new double[12];
        var precipitationSums = new double[12];
        var counts = new int[12];
        for (var index = 0; index < source.Length; index++)
        {
            var expected = referenceStart.AddMonths(index);
            var item = source[index];
            if (item.Month != expected.ToString("yyyy-MM", CultureInfo.InvariantCulture) ||
                !double.IsFinite(item.AvgTempC) || !double.IsFinite(item.MinTempC) ||
                !double.IsFinite(item.MaxTempC) || !double.IsFinite(item.PrecipitationMm) ||
                item.MinTempC > item.AvgTempC || item.AvgTempC > item.MaxTempC ||
                item.PrecipitationMm < 0)
                throw new WeatherDataUnavailableException("NASA POWER reference months are incomplete or invalid.");

            var monthIndex = expected.Month - 1;
            avgSums[monthIndex] += item.AvgTempC;
            minSums[monthIndex] += item.MinTempC;
            maxSums[monthIndex] += item.MaxTempC;
            precipitationSums[monthIndex] += item.PrecipitationMm;
            counts[monthIndex]++;
        }

        var months = new ClimateScenarioMonth[12];
        for (var index = 0; index < months.Length; index++)
        {
            var target = targetStartMonth.AddMonths(index);
            var calendarMonth = target.Month - 1;
            if (counts[calendarMonth] != ReferenceYearCount ||
                !double.IsFinite(avgSums[calendarMonth]) ||
                !double.IsFinite(minSums[calendarMonth]) ||
                !double.IsFinite(maxSums[calendarMonth]) ||
                !double.IsFinite(precipitationSums[calendarMonth]))
                throw new WeatherDataUnavailableException("NASA POWER reference months are incomplete or invalid.");

            months[index] = new ClimateScenarioMonth(
                target.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                avgSums[calendarMonth] / ReferenceYearCount,
                minSums[calendarMonth] / ReferenceYearCount,
                maxSums[calendarMonth] / ReferenceYearCount,
                precipitationSums[calendarMonth] / ReferenceYearCount,
                counts[calendarMonth]);
        }

        var sourceMonths = source.Select(item => new ClimateScenarioSourceMonth(item.Month,
            item.AvgTempC, item.MinTempC, item.MaxTempC, item.PrecipitationMm)).ToArray();
        return new(months, sourceMonths, historical.RetrievedAtUtc, historical.ApiVersion, historical.CacheHit);
    }
}
