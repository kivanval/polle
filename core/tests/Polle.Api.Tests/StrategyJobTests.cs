using System.Net;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Polle.Api.Tests;

public sealed class StrategyJobTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
    private static StrategyJob Job(string key, string state = StrategyJobs.Processing) =>
        new() { Key = key, Kind = StrategyJobs.Optimization, TaskId = "t", State = state, UpdatedAtUtc = Now.AddMinutes(-1) };

    [Fact]
    public void ReadyStoresResult()
    {
        var job = Job("bold-irrigated");

        StrategyJobs.Apply(job, new FelderTaskState.Ready("""{"status":"OPTIMAL"}"""), Now);

        Assert.Equal(StrategyJobs.Ready, job.State);
        Assert.Equal("""{"status":"OPTIMAL"}""", job.ResultJson);
        Assert.Equal(Now, job.UpdatedAtUtc);
    }

    [Fact]
    public void ProcessingOnlyTouchesTheClock()
    {
        var job = Job("bold-irrigated");

        StrategyJobs.Apply(job, new FelderTaskState.Processing(), Now);

        Assert.Equal(StrategyJobs.Processing, job.State);
        Assert.Null(job.ResultJson);
        Assert.Equal(Now, job.UpdatedAtUtc);
    }

    [Fact]
    public void FailedStatusMarksOnlyThatJob()
    {
        var failing = Job("safe-dryland");
        var other = Job("bold-dryland");

        StrategyJobs.Apply(failing, new FelderTaskState.Failed("Optimization task failed: x"), Now);

        Assert.Equal(StrategyJobs.Failed, failing.State);
        Assert.Equal("Optimization task failed: x", failing.Error);
        Assert.Equal(StrategyJobs.Processing, other.State);
    }

    [Fact]
    public void RetryResubmitsOneJob()
    {
        Assert.True(StrategyJobs.Retryable(Job("safe-dryland", StrategyJobs.Failed)));
        Assert.False(StrategyJobs.Retryable(Job("safe-dryland", StrategyJobs.Ready)));
        Assert.False(StrategyJobs.Retryable(Job("safe-dryland")));
    }

    [Fact]
    public void RunHasOneJobPerStrategy()
    {
        Assert.Equal(StrategyCatalog.Presets.Count, StrategyJobs.Plan(hasBaseline: false, null, Now).Count);
        var withBaseline = StrategyJobs.Plan(hasBaseline: true, null, Now);
        Assert.Equal(StrategyCatalog.Presets.Count + 1, withBaseline.Count);
        Assert.Equal(StrategyCatalog.BaselineKey, withBaseline[0].Key);
        Assert.Equal(StrategyJobs.Evaluation, withBaseline[0].Kind);
        Assert.All(withBaseline.Skip(1), job => Assert.Equal(StrategyJobs.Optimization, job.Kind));
    }

    [Fact]
    public void RunHasOneJobPerChosenStrategy() =>
        Assert.Equal([StrategyCatalog.BaselineKey, "medium", "custom-000-ter-off"],
            StrategyJobs.Plan(hasBaseline: true, [new("medium"), new("custom-000-ter-off")], Now).Select(job => job.Key));

    [Fact]
    public void PollsAJobAtMostEverySecond()
    {
        var job = Job("bold-irrigated");
        job.UpdatedAtUtc = Now.AddMilliseconds(-500);
        Assert.False(StrategyJobs.DueForPoll(job, Now));
        job.UpdatedAtUtc = Now.AddSeconds(-1);
        Assert.True(StrategyJobs.DueForPoll(job, Now));
        job.State = StrategyJobs.Ready;
        Assert.False(StrategyJobs.DueForPoll(job, Now));
    }

    [Fact]
    public void GiveUpCountsFromTheLastSubmission()
    {
        var retried = Job("safe-dryland");
        retried.SubmittedAtUtc = Now.AddSeconds(-5); // resubmitted just now, in a run created an hour ago
        Assert.False(StrategyJobs.Expired(retried, Now));

        var stuck = Job("bold-dryland");
        stuck.SubmittedAtUtc = Now.AddMinutes(-16);
        Assert.True(StrategyJobs.Expired(stuck, Now));

        stuck.State = StrategyJobs.Ready;
        Assert.False(StrategyJobs.Expired(stuck, Now));
    }

    [Fact]
    public void DeferredSubmissionStaysProcessingWithoutATask()
    {
        var job = Job("safe-dryland");
        job.TaskId = null;

        StrategyJobs.Defer(job, "The solver answered 403.", Now);

        Assert.Equal(StrategyJobs.Processing, job.State);
        Assert.Null(job.TaskId);
        Assert.Equal("The solver answered 403.", job.Error);
        Assert.Equal(Now, job.UpdatedAtUtc);
        Assert.False(StrategyJobs.DueForPoll(job, Now.AddMinutes(1)));
    }

    [Fact]
    public void ResubmissionBacksOff()
    {
        var job = Job("safe-dryland");
        job.TaskId = null;
        job.SubmittedAtUtc = Now.AddSeconds(-10); // first attempt 10 s ago: wait half of that between attempts
        job.UpdatedAtUtc = Now.AddSeconds(-4);
        Assert.False(StrategyJobs.DueForResubmit(job, Now));
        job.UpdatedAtUtc = Now.AddSeconds(-6);
        Assert.True(StrategyJobs.DueForResubmit(job, Now));

        job.SubmittedAtUtc = Now.AddMinutes(-2); // the wait never exceeds 30 s
        job.UpdatedAtUtc = Now.AddSeconds(-31);
        Assert.True(StrategyJobs.DueForResubmit(job, Now));

        job.TaskId = "t";
        Assert.False(StrategyJobs.DueForResubmit(job, Now));
    }

    [Fact]
    public void SubmissionGivesUpAfterThreeMinutes()
    {
        var job = Job("safe-dryland");
        job.TaskId = null;
        job.SubmittedAtUtc = Now.AddMinutes(-2);
        Assert.False(StrategyJobs.SubmissionExpired(job, Now));
        job.SubmittedAtUtc = Now.AddMinutes(-4);
        Assert.True(StrategyJobs.SubmissionExpired(job, Now));

        job.TaskId = "t";
        Assert.False(StrategyJobs.SubmissionExpired(job, Now));
    }

    [Theory]
    [InlineData("2026-13", "2031-10", "startMonth")]
    [InlineData("2026-12", "2031-1", "endMonth")]
    [InlineData("2021-10", "2031-10", "startMonth")]
    [InlineData("2027-05", "2027-04", "endMonth")]
    [InlineData("2026-12", "2056-12", "endMonth")]
    public void HorizonValidation(string start, string end, string key)
    {
        Assert.Contains(key, StrategyJobs.HorizonErrors(start, end, new DateOnly(2026, 11, 8)).Keys);
    }

    [Theory]
    [InlineData("2026-11", "2036-10")]
    [InlineData("2026-12", "2046-11")]
    [InlineData("2026-12", "2056-11")]
    [InlineData("2027-01", "2056-12")]
    [InlineData("2026-10", "2031-10")]
    [InlineData("2021-11", "2026-10")]
    public void HorizonsUpToThirtyYearsAreValid(string start, string end)
    {
        Assert.Empty(StrategyJobs.HorizonErrors(start, end, new DateOnly(2026, 11, 8)));
    }

    /// <summary>The solver fetches prices and climate for its whole horizon; the longest horizon must pass both APIs.</summary>
    [Theory]
    [InlineData("2026-12", "2056-11")]
    [InlineData("2027-01", "2056-12")]
    [InlineData("2021-11", "2051-10")]
    public void LongestHorizonFitsTheFinanceAndClimateApis(string start, string end)
    {
        Assert.Empty(StrategyJobs.HorizonErrors(start, end, new DateOnly(2026, 11, 8)));
        var first = DateOnly.ParseExact($"{start}-01", "yyyy-MM-dd");
        var last = DateOnly.ParseExact($"{end}-01", "yyyy-MM-dd").AddMonths(1).AddDays(-1);

        Assert.Empty(Polle.Api.Features.Finance.FinanceEndpoints.Validate(new Polle.Api.Features.Finance.PricingRequest(first.Year, last.Year, [])));
        Assert.Empty(Polle.Api.Features.Finance.FinanceEndpoints.Validate(new Polle.Api.Features.Finance.FinanceRequestV2(first.Year, last.Year, [])));
        Assert.Empty(Polle.Api.Features.Climate.ClimateDailyEndpoints.Validate(first.ToString("yyyy-MM-dd"), last.ToString("yyyy-MM-dd"), out _, out _));
    }

    [Fact]
    public void SolverRequestCarriesTheWholeRequestedHorizon()
    {
        var request = StrategyRequests.Optimization(Guid.NewGuid(), "2026-12", "2056-11", StrategyCatalog.Presets[0], new SolverMaterials(null));
        Assert.Equal(new PlanningHorizon("2026-12", "2056-11"), request.Horizon);
    }

    [Fact]
    public async Task StrategyRoutesNeedASession()
    {
        var client = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("DataProtection:PersistKeysToDatabase", "false");
        }).CreateClient();

        var response = await client.GetAsync($"/api/strategy-runs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
