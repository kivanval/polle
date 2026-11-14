using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Polle.Api.Features.Weather;
using Microsoft.Extensions.Caching.Memory;

namespace Polle.Api.Features.Climate;

/// <summary>Mean daily weather of one calendar day over the reference years.</summary>
public sealed record DailyClimateNormal(double MinTempC, double MaxTempC, double MeanTempC, double PrecipitationMm);

/// <summary>
/// A daily planning assumption for a field point: for each calendar day, the arithmetic mean of that day's NASA POWER
/// daily T2M_MIN, T2M_MAX, T2M and PRECTOTCORR over ten completed reference years. Not a forecast. February 29 has no
/// entry of its own; it takes February 28's mean.
/// </summary>
public sealed record DailyClimateBaseline(int ReferenceStartYear, int ReferenceEndYear,
    IReadOnlyDictionary<int, DailyClimateNormal> Days, DateTimeOffset RetrievedAtUtc, string? ApiVersion)
{
    public DailyClimateNormal For(DateOnly date) =>
        Days[Key(date.Month, date is { Month: 2, Day: 29 } ? 28 : date.Day)];

    internal static int Key(int month, int day) => month * 100 + day;
}

// A dedicated, size-limited process cache. In-flight fetches are shared per point, so the solver's parallel requests
// for one field make one NASA call, and requests for different fields do not wait on each other.
public sealed class DailyClimateBaselineCache : IDisposable
{
    internal MemoryCache Entries { get; } = new(new MemoryCacheOptions { SizeLimit = 256 });
    internal ConcurrentDictionary<string, Lazy<Task<DailyClimateBaseline>>> InFlight { get; } = new();

    public void Dispose() => Entries.Dispose();
}

public sealed class NasaPowerDailyClimate(HttpClient http, DailyClimateBaselineCache cache,
    TimeProvider clock, IConfiguration configuration)
{
    public const int ReferenceYearCount = 10;
    public const string Source = "NASA POWER daily point API";
    private const string Parameters = "T2M,T2M_MIN,T2M_MAX,PRECTOTCORR";
    private const string CacheSchema = "power-daily-baseline-v1";
    private const int MaxResponseBytes = 4_000_000;

    /// <summary>
    /// The last reference year: the latest calendar year that ended at least six months ago. POWER publishes corrected
    /// precipitation with a delay, so the year just ended can still have gaps early in the next one.
    /// </summary>
    public static int ReferenceEndYear(DateTimeOffset now) => now.AddMonths(-6).Year - 1;

    public async Task<DailyClimateBaseline> GetAsync(double latitude, double longitude, CancellationToken ct)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            latitude is <= -90 or >= 90 || longitude is < -180 or > 180)
            throw new ArgumentException("A finite WGS84 field point is required.");

        var endYear = ReferenceEndYear(clock.GetUtcNow());
        var startYear = endYear - ReferenceYearCount + 1;
        // Exact round-trip coordinates avoid undocumented grid snapping, as for the monthly client.
        var key = string.Join('|', CacheSchema, http.BaseAddress,
            configuration["Weather:NasaPowerDataVersion"] ?? "unspecified", Parameters,
            latitude.ToString("R", CultureInfo.InvariantCulture),
            longitude.ToString("R", CultureInfo.InvariantCulture),
            startYear.ToString(CultureInfo.InvariantCulture), endYear.ToString(CultureInfo.InvariantCulture));
        if (cache.Entries.TryGetValue<DailyClimateBaseline>(key, out var hit)) return hit!;

        // The shared fetch must not be cancelled by whichever caller started it; each caller stops waiting on its own.
        var fetch = cache.InFlight.GetOrAdd(key, _ => new Lazy<Task<DailyClimateBaseline>>(
            () => FetchAndCacheAsync(key, latitude, longitude, startYear, endYear)));
        return await fetch.Value.WaitAsync(ct);
    }

    private async Task<DailyClimateBaseline> FetchAndCacheAsync(string key, double latitude, double longitude,
        int startYear, int endYear)
    {
        try
        {
            var result = await FetchAsync(latitude, longitude, startYear, endYear);
            // Completed reference years rarely change; the key moves to new years on its own each July.
            cache.Entries.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30), Size = 1
            });
            return result;
        }
        finally { cache.InFlight.TryRemove(key, out _); }
    }

    private async Task<DailyClimateBaseline> FetchAsync(double latitude, double longitude, int startYear, int endYear)
    {
        var uri = "api/temporal/daily/point?parameters=" + Parameters +
            "&community=AG&format=JSON&latitude=" + latitude.ToString("R", CultureInfo.InvariantCulture) +
            "&longitude=" + longitude.ToString("R", CultureInfo.InvariantCulture) +
            "&start=" + startYear.ToString(CultureInfo.InvariantCulture) + "0101" +
            "&end=" + endYear.ToString(CultureInfo.InvariantCulture) + "1231";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var attemptBudget = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, attemptBudget.Token);
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)));
                        continue;
                    }
                    throw new WeatherUpstreamException("NASA POWER is temporarily unavailable.");
                }
                if (!response.IsSuccessStatusCode)
                    throw new WeatherUpstreamException("NASA POWER rejected the daily weather request.");
                if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                    throw new WeatherUpstreamException("NASA POWER returned an oversized response.");
                await response.Content.LoadIntoBufferAsync(MaxResponseBytes, attemptBudget.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(attemptBudget.Token);
                using var document = await JsonDocument.ParseAsync(stream,
                    new JsonDocumentOptions { MaxDepth = 16 }, attemptBudget.Token);
                return Parse(document.RootElement, startYear, endYear, clock.GetUtcNow());
            }
            catch (Exception ex) when ((ex is HttpRequestException or HttpIOException) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)));
            }
            catch (Exception ex) when (ex is HttpRequestException or HttpIOException)
            {
                throw new WeatherUpstreamException("NASA POWER could not be reached.");
            }
            catch (OperationCanceledException) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)));
            }
            catch (OperationCanceledException)
            {
                throw new WeatherUpstreamException("NASA POWER timed out.");
            }
            catch (JsonException)
            {
                throw new WeatherUpstreamException("NASA POWER returned invalid JSON.");
            }
        }
        throw new WeatherUpstreamException("NASA POWER could not be reached.");
    }

    /// <summary>
    /// Averages each calendar day over the reference years. A fill value, a missing day or parameter, other unit
    /// labels, impossible temperature ordering or negative precipitation make the baseline unavailable. No value is
    /// imputed. Means are rounded to 0.01, which keeps min ≤ mean ≤ max.
    /// </summary>
    internal static DailyClimateBaseline Parse(JsonElement root, int startYear, int endYear, DateTimeOffset retrievedAt)
    {
        try
        {
            var header = root.GetProperty("header");
            var fill = header.GetProperty("fill_value").GetDouble();
            var metadata = root.GetProperty("parameters");
            var series = root.GetProperty("properties").GetProperty("parameter");
            foreach (var name in new[] { "T2M", "T2M_MIN", "T2M_MAX" })
                if (metadata.GetProperty(name).GetProperty("units").GetString() != "C")
                    throw new WeatherDataUnavailableException("NASA POWER temperature units changed.");
            // A daily PRECTOTCORR value in mm/day is that day's precipitation in mm.
            if (metadata.GetProperty("PRECTOTCORR").GetProperty("units").GetString() != "mm/day")
                throw new WeatherDataUnavailableException("NASA POWER precipitation units changed.");

            var sums = new Dictionary<int, (double Min, double Max, double Mean, double Precipitation, int Years)>();
            for (var day = new DateOnly(startYear, 1, 1); day <= new DateOnly(endYear, 12, 31); day = day.AddDays(1))
            {
                if (day is { Month: 2, Day: 29 }) continue;
                var key = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var mean = Value("T2M", key);
                var min = Value("T2M_MIN", key);
                var max = Value("T2M_MAX", key);
                var precipitation = Value("PRECTOTCORR", key);
                if (min > mean || mean > max || precipitation < 0)
                    throw new WeatherDataUnavailableException("NASA POWER returned inconsistent daily weather.");
                var calendarDay = DailyClimateBaseline.Key(day.Month, day.Day);
                var sum = sums.GetValueOrDefault(calendarDay);
                sums[calendarDay] = (sum.Min + min, sum.Max + max, sum.Mean + mean,
                    sum.Precipitation + precipitation, sum.Years + 1);
            }

            var years = endYear - startYear + 1;
            var days = sums.ToDictionary(pair => pair.Key, pair => new DailyClimateNormal(
                Round(pair.Value.Min / years), Round(pair.Value.Max / years),
                Round(pair.Value.Mean / years), Round(pair.Value.Precipitation / years)));
            var version = header.TryGetProperty("api", out var api) &&
                api.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
            return new(startYear, endYear, days, retrievedAt, version);

            double Value(string parameter, string key)
            {
                if (!series.GetProperty(parameter).TryGetProperty(key, out var element) ||
                    element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) ||
                    !double.IsFinite(value) || value == fill)
                    throw new WeatherDataUnavailableException("NASA POWER has missing daily weather values.");
                return value;
            }
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new WeatherDataUnavailableException("NASA POWER omitted required weather metadata.");
        }
    }

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
