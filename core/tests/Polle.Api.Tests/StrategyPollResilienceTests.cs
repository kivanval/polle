using System.Net;
using System.Text;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Microsoft.Extensions.Logging;

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

    /// <summary>Keeps what was logged, so a test can check the trace a submission leaves.</summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static StrategyRun Run() => new() { FieldId = Guid.NewGuid(), StartMonth = "2026-12", EndMonth = "2030-12" };

    [Fact]
    public async Task ASubmissionTheSolverRefusesIsLoggedWithItsReasonAndKeptForAnotherTry()
    {
        var solver = Solver(_ => Answer(HttpStatusCode.Forbidden, """{"detail":"blocked at the edge"}"""));
        var job = Job();
        job.TaskId = null;
        var log = new ListLogger();

        await StrategyRunEndpoints.SubmitAsync(job, Run(), null, solver, new SolverMaterials(null), CancellationToken.None, log);

        Assert.Equal(StrategyJobs.Processing, job.State);
        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("control", message);
        Assert.Contains("blocked at the edge", message);
    }

    [Fact]
    public async Task ASubmissionTheSolverAcceptsIsLoggedWithItsTask()
    {
        var solver = Solver(_ => Answer(HttpStatusCode.Accepted, """{"task_id":"task-42"}"""));
        var job = Job();
        job.TaskId = null;
        var log = new ListLogger();

        await StrategyRunEndpoints.SubmitAsync(job, Run(), null, solver, new SolverMaterials(null), CancellationToken.None, log);

        Assert.Equal("task-42", job.TaskId);
        var (level, message) = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, level);
        Assert.Contains("task-42", message);
    }

    [Fact]
    public async Task AnEvaluationWithNoCropsInTheHorizonIsLoggedAsFailed()
    {
        var job = Job(StrategyCatalog.BaselineKey);
        job.Kind = StrategyJobs.Evaluation;
        job.TaskId = null;
        var log = new ListLogger();

        await StrategyRunEndpoints.SubmitAsync(job, Run(), null, Solver(_ => throw new InvalidOperationException("not called")),
            new SolverMaterials(null), CancellationToken.None, log);

        Assert.Equal(StrategyJobs.Failed, job.State);
        Assert.Equal(LogLevel.Warning, Assert.Single(log.Entries).Level);
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

    /// <summary>"Evaluate my plan" waits for the solver's quick answer, so the result comes back with the request itself.</summary>
    [Fact]
    public async Task AQuickEvaluationIsReadyWhenTheRequestAnswers()
    {
        var checks = 0;
        var solver = Solver(request => request.RequestUri!.AbsolutePath.EndsWith("/status")
            ? ++checks < 2 ? Answer(HttpStatusCode.OK, """{"status":"processing"}""") : Answer(HttpStatusCode.SeeOther, "")
            : Answer(HttpStatusCode.OK, """{"status":"FEASIBLE","schedule":[]}"""));
        var job = Job(StrategyCatalog.BaselineKey);
        job.Kind = StrategyJobs.Evaluation;
        var run = new StrategyRun { FieldId = Guid.NewGuid(), StartMonth = "2026-12", EndMonth = "2030-12", Jobs = [job] };

        var changed = await StrategyRunEndpoints.AwaitQuickResultAsync(run, solver, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.True(changed);
        Assert.Equal(StrategyJobs.Ready, job.State);
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task ASlowEvaluationIsLeftProcessingForTheClientsPolls()
    {
        var solver = Solver(_ => Answer(HttpStatusCode.OK, """{"status":"processing"}"""));
        var job = Job(StrategyCatalog.BaselineKey);
        job.Kind = StrategyJobs.Evaluation;
        var run = new StrategyRun { FieldId = Guid.NewGuid(), StartMonth = "2026-12", EndMonth = "2030-12", Jobs = [job] };

        await StrategyRunEndpoints.AwaitQuickResultAsync(run, solver, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        Assert.Equal(StrategyJobs.Processing, job.State);
    }
}
