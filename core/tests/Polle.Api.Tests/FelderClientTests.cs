using System.Net;
using System.Text;
using Polle.Api.Features.Strategies;

namespace Polle.Api.Tests;

public sealed class FelderClientTests
{
    private const string Result = """
        {"status":"OPTIMAL","expected_profit_cents":55300000,"schedule":[{"crop_profile_id":"7754","planting_year_month":"2026-10",
         "clearance_year_month":"2027-07","harvest_year_months":["2027-07"],"expected_revenue_cents":18450000,"expenses_cents":4500000,
         "yield_multiplier":0.85,"irrigation_applied_mm":120.5,"nitrogen_applied_kg_ha":45,"nitrogen_fixed_kg_ha":0,"nitrogen_removed_kg_ha":110,
         "phosphorus_applied_kg_ha":10,"phosphorus_removed_kg_ha":20,"risk_notes":["[AGRONOMIC] Heat shock"]}]}
        """;

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static FelderClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, out StubHandler handler)
    {
        handler = new StubHandler(respond);
        return new FelderClient(new HttpClient(handler) { BaseAddress = new Uri("https://solver.test/") });
    }

    [Fact]
    public async Task SubmitReturnsTaskId()
    {
        var client = Client(_ => Json(HttpStatusCode.Accepted, """{"task_id":"t-1"}"""), out var handler);

        var taskId = await client.SubmitOptimizationAsync(StrategyRequests.Optimization(Guid.NewGuid(), "2026-12", "2031-10",
            StrategyCatalog.Presets[0], new SolverMaterials(null)), CancellationToken.None);

        Assert.Equal("t-1", taskId);
        Assert.Equal("/api/v1/solver/optimizations", handler.Requests.Single().AbsolutePath);
    }

    [Fact]
    public async Task SubmitSendsAContentLength()
    {
        long? length = null;
        var client = Client(request => { length = request.Content!.Headers.ContentLength; return Json(HttpStatusCode.Accepted, """{"task_id":"t-1"}"""); }, out _);

        await client.SubmitOptimizationAsync(StrategyRequests.Optimization(Guid.NewGuid(), "2026-12", "2031-10",
            StrategyCatalog.Presets[0], new SolverMaterials(null)), CancellationToken.None);

        Assert.True(length > 0);
    }

    [Fact]
    public async Task StatusProcessing()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, """{"status":"processing"}"""), out _);

        Assert.IsType<FelderTaskState.Processing>(await client.GetStateAsync(FelderTaskKind.Optimization, "t-1", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData((HttpStatusCode)522)]
    public async Task ThrottledStatusKeepsProcessing(HttpStatusCode code)
    {
        var client = Client(_ => Json(code, """{"detail":"Too Many Requests"}"""), out _);

        Assert.IsType<FelderTaskState.Processing>(await client.GetStateAsync(FelderTaskKind.Optimization, "t-1", CancellationToken.None));
    }

    [Fact]
    public async Task SeeOtherFetchesResult()
    {
        var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Json(HttpStatusCode.SeeOther, """{"detail":"Task is completed."}""")
            : Json(HttpStatusCode.OK, Result), out var handler);

        var state = await client.GetStateAsync(FelderTaskKind.Evaluation, "t-9", CancellationToken.None);

        var ready = Assert.IsType<FelderTaskState.Ready>(state);
        Assert.Contains("55300000", ready.ResultJson);
        Assert.Equal(["/api/v1/solver/evaluations/t-9/status", "/api/v1/solver/evaluations/t-9/result"], handler.Requests.Select(uri => uri.AbsolutePath));
    }

    [Fact]
    public async Task ResultErrorIsFailed()
    {
        var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Json(HttpStatusCode.SeeOther, "{}")
            : Json(HttpStatusCode.InternalServerError, """{"detail":"Optimization task failed: x"}"""), out _);

        var failed = Assert.IsType<FelderTaskState.Failed>(await client.GetStateAsync(FelderTaskKind.Optimization, "t-1", CancellationToken.None));
        Assert.Equal("Optimization task failed: x", failed.Message);
    }

    [Fact]
    public async Task UnavailableResultKeepsProcessing()
    {
        var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? Json(HttpStatusCode.SeeOther, "{}")
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("<html>down</html>", Encoding.UTF8, "text/html") }, out _);

        Assert.IsType<FelderTaskState.Processing>(await client.GetStateAsync(FelderTaskKind.Optimization, "t-1", CancellationToken.None));
    }

    [Fact]
    public async Task SubmitBlockedByCloudflareIsTransientAndNamesTheRay()
    {
        var client = Client(_ =>
        {
            var blocked = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<!DOCTYPE html><title>Attention Required!</title>", Encoding.UTF8, "text/html") };
            blocked.Headers.Server.ParseAdd("cloudflare");
            blocked.Headers.Add("CF-RAY", "a57e0000aaaa-VIE");
            blocked.Headers.Add("cf-mitigated", "challenge");
            return blocked;
        }, out _);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SubmitOptimizationAsync(StrategyRequests.Optimization(Guid.NewGuid(),
            "2026-12", "2031-10", StrategyCatalog.Presets[0], new SolverMaterials(null)), CancellationToken.None));

        Assert.True(FelderClient.Transient(error.StatusCode));
        Assert.Contains("403", error.Message);
        Assert.Contains("Cloudflare", error.Message);
        Assert.Contains("challenge", error.Message);
        Assert.Contains("a57e0000aaaa-VIE", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData((HttpStatusCode)530, true)]
    [InlineData((HttpStatusCode)529, false)]
    public void TransientCodes(HttpStatusCode code, bool transient) => Assert.Equal(transient, FelderClient.Transient(code));

    [Fact]
    public void NetworkErrorsAreTransient() => Assert.True(FelderClient.Transient(null));

    [Fact]
    public async Task UnknownTaskIsFailed()
    {
        var client = Client(_ => Json(HttpStatusCode.NotFound, """{"detail":"Not Found"}"""), out _);

        Assert.IsType<FelderTaskState.Failed>(await client.GetStateAsync(FelderTaskKind.Optimization, "t-x", CancellationToken.None));
    }
}
