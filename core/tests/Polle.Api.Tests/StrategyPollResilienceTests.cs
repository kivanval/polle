using System.Net;
using System.Text;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;

namespace Polle.Api.Tests;

/// <summary>
/// One strategy that goes wrong must not stop a run's poll: the other cards still have to reach ready or failed,
/// rather than the whole run sitting at "0 of 6 ready" behind a failed request.
/// </summary>
public sealed class StrategyPollResilienceTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    private static FelderClient Solver(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://solver.test/") });

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static StrategyJob Job(string key = "control") => new()
    {
        Key = key, Kind = StrategyJobs.Optimization, TaskId = "t", State = StrategyJobs.Processing,
        SubmittedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
    };

    [Theory]
    [InlineData("\"Internal Server Error\"")]
    [InlineData("[{\"detail\":\"x\"}]")]
    [InlineData("<html>Bad gateway</html>")]
    public async Task AFailedTaskWithAnOddBodyIsAFailureNotAnException(string body)
    {
        var solver = Solver(_ => Answer(HttpStatusCode.InternalServerError, body));

        var state = await solver.GetStateAsync(FelderTaskKind.Optimization, "t", CancellationToken.None);

        Assert.IsType<FelderTaskState.Failed>(state);
    }

    [Fact]
    public async Task AnUnexpectedErrorWhilePollingLeavesThatJobProcessing()
    {
        var solver = Solver(_ => throw new InvalidOperationException("unexpected"));

        var state = await StrategyRunEndpoints.PollAsync(Job(), solver, CancellationToken.None);

        Assert.Null(state);
    }

    [Fact]
    public async Task AnUnexpectedErrorWhileSubmittingFailsOnlyThatJob()
    {
        var solver = Solver(_ => throw new InvalidOperationException("unexpected"));
        var job = Job();
        job.TaskId = null;
        var run = new StrategyRun { FieldId = Guid.NewGuid(), StartMonth = "2026-12", EndMonth = "2030-12" };

        await StrategyRunEndpoints.SubmitAsync(job, run, null, solver, new SolverMaterials(null), CancellationToken.None);

        Assert.Equal(StrategyJobs.Failed, job.State);
    }

    [Theory]
    [InlineData("<html>challenge</html>")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"done\"")]
    public void NameLookupSkipsAResultThatIsNotAnObject(string resultJson)
    {
        Assert.Empty(StrategyProfiles.ProfileIds(resultJson));
        Assert.Empty(StrategyProfiles.FertilizerIds(resultJson));
    }

    [Fact]
    public void AReadyAnswerThatIsNotAResultFailsTheJobSoItCanBeRetried()
    {
        var job = Job();

        StrategyJobs.Apply(job, new FelderTaskState.Ready("<html>challenge</html>"), DateTime.UtcNow);

        Assert.Equal(StrategyJobs.Failed, job.State);
        Assert.True(StrategyJobs.Retryable(job));
    }

    [Fact]
    public void AResultThatCannotBeReadStillAnswersTheJob()
    {
        var job = Job();
        job.State = StrategyJobs.Ready;
        job.ResultJson = "<html>challenge</html>";

        var response = StrategyRunResponse.Job(job, CropProfileNames.Empty);

        Assert.Equal("control", response.Key);
        Assert.Null(response.Result);
    }
}
