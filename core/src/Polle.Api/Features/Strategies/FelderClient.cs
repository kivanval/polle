using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Polle.Api.Features.Strategies;

public enum FelderTaskKind { Optimization, Evaluation }

/// <summary>Where a solver task stands. A finished task carries its raw result JSON (an OptimizeFieldResponse).</summary>
public abstract record FelderTaskState
{
    public sealed record Processing : FelderTaskState;
    public sealed record Ready(string ResultJson) : FelderTaskState;
    public sealed record Failed(string Message) : FelderTaskState;
}

/// <summary>
/// Typed client of the felder solver. Its status route answers 303 when a task is done, so redirects must not be
/// followed automatically (see Program.cs registration).
/// </summary>
public sealed class FelderClient(HttpClient http)
{
    public bool Configured => http.BaseAddress is not null;
    public Uri? BaseAddress => http.BaseAddress;

    public Task<string> SubmitOptimizationAsync(OptimizeFieldRequest request, CancellationToken ct) => SubmitAsync("optimizations", request, ct);
    public Task<string> SubmitEvaluationAsync(EvaluateScenarioRequest request, CancellationToken ct) => SubmitAsync("evaluations", request, ct);

    private async Task<string> SubmitAsync<T>(string route, T request, CancellationToken ct)
    {
        // A buffered body carries Content-Length; chunked uploads are not safe through every tunnel and proxy.
        using var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"api/v1/solver/{route}", content, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(await DetailAsync(response, ct), null, response.StatusCode);
        var task = await response.Content.ReadFromJsonAsync<FelderTaskResponse>(ct);
        return task?.TaskId ?? throw new HttpRequestException("The solver accepted the task without an id.");
    }

    public async Task<FelderTaskState> GetStateAsync(FelderTaskKind kind, string taskId, CancellationToken ct)
    {
        var route = $"api/v1/solver/{(kind == FelderTaskKind.Optimization ? "optimizations" : "evaluations")}/{Uri.EscapeDataString(taskId)}";
        using (var status = await http.GetAsync($"{route}/status", ct))
        {
            // A throttled, blocked or briefly unavailable solver says nothing about the task: ask again on the next poll.
            // StrategyJobs.Expired still gives up on a task that never comes back.
            if (status.StatusCode == HttpStatusCode.OK || Transient(status.StatusCode)) return new FelderTaskState.Processing();
            if (status.StatusCode != HttpStatusCode.SeeOther) return new FelderTaskState.Failed(await DetailAsync(status, ct));
        }
        using var result = await http.GetAsync($"{route}/result", ct);
        if (Transient(result.StatusCode)) return new FelderTaskState.Processing();
        if (!result.IsSuccessStatusCode) return new FelderTaskState.Failed(await DetailAsync(result, ct));
        return new FelderTaskState.Ready(await result.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Answers that say nothing about the request itself, so sending it again later can succeed: no answer at all (null),
    /// a 403 from the Cloudflare edge in front of the solver (the solver app itself never answers 403), timeouts,
    /// throttling, and gateway or Cloudflare origin errors. A plain 500 is the solver reporting a failed task and is final.
    /// </summary>
    public static bool Transient(HttpStatusCode? code) => code is null
        or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
        or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
        // 530 (error 1033): the Cloudflare Tunnel to the solver is down; it comes back when the host reconnects.
        || (int)code is >= 520 and <= 527 or 530;

    private static async Task<string> DetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var json = JsonDocument.Parse(body);
            // A proxy or crashed worker may answer with a JSON string or array; only an object can carry detail.
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String) return detail.GetString()!;
        }
        catch (JsonException) { }
        // No answer from the solver app: name the Cloudflare edge verdict and ray id so the block can be traced in its dashboard.
        var answer = $"The solver answered {(int)response.StatusCode}";
        if (!response.Headers.Server.Any(server => string.Equals(server.Product?.Name, "cloudflare", StringComparison.OrdinalIgnoreCase)))
            return answer + ".";
        var mitigated = response.Headers.TryGetValues("cf-mitigated", out var verdicts) ? $", {string.Join(",", verdicts)}" : "";
        var ray = response.Headers.TryGetValues("CF-RAY", out var rays) ? $", ray {rays.First()}" : "";
        return $"{answer} from its Cloudflare edge{mitigated}{ray}.";
    }
}
