using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Polle.Api.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Answers each request with the next response from a factory and records what was asked.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int calls;
    public int Calls => calls;
    public List<Uri> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        lock (Requests) Requests.Add(request.RequestUri!);
        // Let concurrent callers overlap, as they would against the real service.
        await Task.Delay(50, ct);
        return respond(request);
    }
}

/// <summary>Builds NASA POWER daily point responses shaped like the real API (checked against v2.10.2).</summary>
public static class PowerDaily
{
    public delegate (double Mean, double Min, double Max, double Precipitation) Weather(DateOnly day);

    public static string Json(int startYear, int endYear, Weather weather, Action<Dictionary<string, object>>? edit = null)
    {
        var series = new Dictionary<string, Dictionary<string, double>>
        {
            ["T2M"] = [], ["T2M_MIN"] = [], ["T2M_MAX"] = [], ["PRECTOTCORR"] = []
        };
        for (var day = new DateOnly(startYear, 1, 1); day <= new DateOnly(endYear, 12, 31); day = day.AddDays(1))
        {
            var key = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var (mean, min, max, precipitation) = weather(day);
            series["T2M"][key] = mean;
            series["T2M_MIN"][key] = min;
            series["T2M_MAX"][key] = max;
            series["PRECTOTCORR"][key] = precipitation;
        }
        var root = new Dictionary<string, object>
        {
            ["header"] = new Dictionary<string, object>
            {
                ["fill_value"] = -999.0,
                ["api"] = new Dictionary<string, object> { ["version"] = "v2.10.2", ["name"] = "POWER Daily API" }
            },
            ["parameters"] = new Dictionary<string, object>
            {
                ["T2M"] = new Dictionary<string, string> { ["units"] = "C" },
                ["T2M_MIN"] = new Dictionary<string, string> { ["units"] = "C" },
                ["T2M_MAX"] = new Dictionary<string, string> { ["units"] = "C" },
                ["PRECTOTCORR"] = new Dictionary<string, string> { ["units"] = "mm/day" }
            },
            ["properties"] = new Dictionary<string, object> { ["parameter"] = series }
        };
        edit?.Invoke(root);
        return JsonSerializer.Serialize(root);
    }

    public static (double, double, double, double) Steady(DateOnly day) => (10, 5, 15, 2);

    public static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
