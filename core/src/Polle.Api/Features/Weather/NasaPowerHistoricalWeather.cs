using System.Globalization;
using System.Net;
using System.Text.Json;
using Polle.Api.Features.Solver;
using Microsoft.Extensions.Caching.Memory;

namespace Polle.Api.Features.Weather;

public sealed record HistoricalWeatherResult(SolverMonthlyWeather[] Months, DateTimeOffset RetrievedAtUtc,
    string? ApiVersion, bool CacheHit);

public sealed class WeatherDataUnavailableException(string message) : Exception(message);
public sealed class WeatherUpstreamException(string message) : Exception(message);

// A dedicated, size-limited process cache. The gate also coalesces identical concurrent reads.
public sealed class HistoricalWeatherCache : IDisposable
{
    internal MemoryCache Entries { get; } = new(new MemoryCacheOptions { SizeLimit = 256 });
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public void Dispose()
    {
        Entries.Dispose();
        Gate.Dispose();
    }
}

public sealed class NasaPowerHistoricalWeather(HttpClient http, HistoricalWeatherCache cache,
    TimeProvider clock, IConfiguration configuration)
{
    private const string Parameters = "T2M,T2M_MIN,T2M_MAX,PRECTOTCORR,PRECTOTCORR_SUM";
    private const string CacheSchema = "power-monthly-ag-v3";

    public async Task<HistoricalWeatherResult> GetAsync(double latitude, double longitude,
        DateOnly startMonth, DateOnly endMonth, CancellationToken ct)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            latitude is <= -90 or >= 90 || longitude is < -180 or > 180)
            throw new ArgumentException("A finite WGS84 field point is required.");
        if (startMonth.Day != 1 || endMonth.Day != 1 || startMonth.Year < 1981 ||
            endMonth.Year >= clock.GetUtcNow().Year || endMonth < startMonth ||
            (endMonth.Year - startMonth.Year) * 12 + endMonth.Month - startMonth.Month >= 120)
            throw new ArgumentOutOfRangeException(nameof(startMonth), "Request 1–120 completed historical months from 1981 through the previous calendar year.");

        // Exact round-trip coordinates avoid undocumented grid snapping. The local parser revision is
        // part of the key; NASA's reported API version is retained in each entry for provenance.
        var key = string.Join('|', CacheSchema, http.BaseAddress,
            configuration["Weather:NasaPowerDataVersion"] ?? "unspecified", Parameters,
            latitude.ToString("R", CultureInfo.InvariantCulture),
            longitude.ToString("R", CultureInfo.InvariantCulture),
            startMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            endMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        if (cache.Entries.TryGetValue<HistoricalWeatherResult>(key, out var hit))
            return hit! with { CacheHit = true };

        await cache.Gate.WaitAsync(ct);
        try
        {
            if (cache.Entries.TryGetValue<HistoricalWeatherResult>(key, out hit))
                return hit! with { CacheHit = true };
            var result = await FetchAsync(latitude, longitude, startMonth, endMonth, ct);
            // Recently published years can be revised; older complete years are more stable.
            var ttl = endMonth.Year >= clock.GetUtcNow().Year - 2
                ? TimeSpan.FromDays(1) : TimeSpan.FromDays(90);
            cache.Entries.Set(key, result, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl, Size = 1
            });
            return result;
        }
        finally { cache.Gate.Release(); }
    }

    private async Task<HistoricalWeatherResult> FetchAsync(double latitude, double longitude,
        DateOnly startMonth, DateOnly endMonth, CancellationToken ct)
    {
        var uri = "api/temporal/monthly/point?parameters=" + Parameters +
            "&community=AG&format=JSON&latitude=" + latitude.ToString("R", CultureInfo.InvariantCulture) +
            "&longitude=" + longitude.ToString("R", CultureInfo.InvariantCulture) +
            "&start=" + startMonth.Year.ToString(CultureInfo.InvariantCulture) +
            "&end=" + endMonth.Year.ToString(CultureInfo.InvariantCulture);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var attemptBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptBudget.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, attemptBudget.Token);
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500)
                {
                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
                        continue;
                    }
                    throw new WeatherUpstreamException("NASA POWER is temporarily unavailable.");
                }
                if (!response.IsSuccessStatusCode)
                    throw new WeatherUpstreamException("NASA POWER rejected the historical weather request.");
                if (response.Content.Headers.ContentLength is > 2_000_000)
                    throw new WeatherUpstreamException("NASA POWER returned an oversized response.");
                await response.Content.LoadIntoBufferAsync(2_000_000, attemptBudget.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(attemptBudget.Token);
                using var document = await JsonDocument.ParseAsync(stream,
                    new JsonDocumentOptions { MaxDepth = 16 }, attemptBudget.Token);
                return Parse(document.RootElement, startMonth, endMonth, clock.GetUtcNow());
            }
            catch (Exception ex) when ((ex is HttpRequestException or HttpIOException) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or HttpIOException)
            {
                throw new WeatherUpstreamException("NASA POWER could not be reached.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
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

    internal static HistoricalWeatherResult Parse(JsonElement root, DateOnly startMonth,
        DateOnly endMonth, DateTimeOffset retrievedAt)
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
            // POWER monthly PRECTOTCORR is a mean daily rate. The SUM parameter is
            // the monthly accumulation, though API v2.10.0 labels it mm/day too.
            if (metadata.GetProperty("PRECTOTCORR").GetProperty("units").GetString() != "mm/day")
                throw new WeatherDataUnavailableException("NASA POWER precipitation units changed.");
            if (metadata.GetProperty("PRECTOTCORR_SUM").GetProperty("units").GetString() is not ("mm" or "mm/day"))
                throw new WeatherDataUnavailableException("NASA POWER precipitation total units changed.");
            var months = new List<SolverMonthlyWeather>();
            for (var month = startMonth; month <= endMonth; month = month.AddMonths(1))
            {
                var key = month.ToString("yyyyMM", CultureInfo.InvariantCulture);
                var avg = Value("T2M", key);
                var min = Value("T2M_MIN", key);
                var max = Value("T2M_MAX", key);
                var dailyPrecipitation = Value("PRECTOTCORR", key);
                var monthlyPrecipitation = Value("PRECTOTCORR_SUM", key);
                if (min > avg || avg > max || dailyPrecipitation < 0 || monthlyPrecipitation < 0)
                    throw new WeatherDataUnavailableException("NASA POWER returned inconsistent monthly weather.");
                months.Add(new SolverMonthlyWeather(month.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                    avg, min, max, monthlyPrecipitation));
            }
            var version = header.TryGetProperty("api", out var api) &&
                api.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
            return new([.. months], retrievedAt, version, false);

            double Value(string parameter, string key)
            {
                if (!series.GetProperty(parameter).TryGetProperty(key, out var element) ||
                    element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) ||
                    !double.IsFinite(value) || value == fill)
                    throw new WeatherDataUnavailableException("NASA POWER has missing monthly weather values.");
                return value;
            }
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            throw new WeatherDataUnavailableException("NASA POWER omitted required weather metadata.");
        }
    }
}
