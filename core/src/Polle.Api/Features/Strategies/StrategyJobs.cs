using System.Globalization;
using System.Text.RegularExpressions;
using Polle.Domain.Agriculture;

namespace Polle.Api.Features.Strategies;

/// <summary>Pure rules for strategy jobs: which jobs a run has, how a solver answer changes one, and horizon checks.</summary>
public static partial class StrategyJobs
{
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Optimization = "optimization";
    public const string Evaluation = "evaluation";
    /// <summary>
    /// Thirty years: the horizon must end before the start month plus this many months. The solver asks the finance and
    /// climate APIs for the whole horizon, so their span limits follow this one.
    /// </summary>
    public const int MaxHorizonMonths = 360;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    /// <summary>
    /// How long "Evaluate my plan" waits for the solver before answering: felder scores a plan in a second or two, so the
    /// result usually comes back with the request itself. A slower one is left to the client's polls.
    /// </summary>
    public static readonly TimeSpan QuickWait = TimeSpan.FromSeconds(6);
    /// <summary>How often the solver is asked while waiting for a quick result.</summary>
    public static readonly TimeSpan QuickPollEvery = TimeSpan.FromMilliseconds(400);
    /// <summary>A job still processing this long after its last submission is given up, so a lost solver task can't spin forever.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);
    /// <summary>A job the solver keeps refusing to accept (blocked, throttled, unreachable) is given up this long after its first attempt.</summary>
    public static readonly TimeSpan SubmitGiveUpAfter = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MinResubmitWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxResubmitWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The jobs of a new run, all processing: the farmer's plan first when it has crops in the horizon, then every chosen
    /// strategy (the presets by default), each custom one with the farmer's name and description.
    /// </summary>
    /// <summary>
    /// The jobs of a run started from the planning view. "Evaluate my plan" (planOnly) scores only the farmer's plan;
    /// "Suggest strategies" runs only the chosen strategies and never the plan's evaluation, so each button does just its
    /// own work and the view compares the two runs when both are there.
    /// </summary>
    public static List<StrategyJob> ForRequest(bool planOnly, bool hasBaseline, IReadOnlyList<StrategyPick> strategies, DateTime now) =>
        planOnly ? Plan(hasBaseline, [], now) : Plan(hasBaseline: false, strategies, now);

    public static List<StrategyJob> Plan(bool hasBaseline, IReadOnlyList<StrategyPick>? strategies, DateTime now) =>
        [.. (hasBaseline ? [new StrategyPick(StrategyCatalog.BaselineKey)] : Array.Empty<StrategyPick>())
            .Concat(strategies ?? StrategyCatalog.DefaultKeys.Select(key => new StrategyPick(key)).ToArray())
            .Select(pick => new StrategyJob
            {
                Key = pick.Key, Name = pick.Name, Summary = pick.Summary,
                Kind = pick.Key == StrategyCatalog.BaselineKey ? Evaluation : Optimization,
                State = Processing, UpdatedAtUtc = now, SubmittedAtUtc = now,
            })];

    public static void Apply(StrategyJob job, FelderTaskState state, DateTime now)
    {
        switch (state)
        {
            // A "ready" answer this API can't read (an HTML page from a proxy, a malformed body) is a failed card the farmer can retry.
            case FelderTaskState.Ready ready when !Readable(ready.ResultJson):
                job.State = Failed; job.Error = Truncate($"The solver's result couldn't be read: {ready.ResultJson}"); break;
            case FelderTaskState.Ready ready: job.State = Ready; job.ResultJson = ready.ResultJson; job.Error = null; break;
            case FelderTaskState.Failed failed: job.State = Failed; job.Error = Truncate(failed.Message); break;
        }
        job.UpdatedAtUtc = now;
    }

    private static bool Readable(string resultJson)
    {
        try { return StrategyResultMapper.Map(resultJson, CropProfileNames.Empty) is not null; }
        catch (Exception) { return false; }
    }

    public static void Fail(StrategyJob job, string message, DateTime now) => Apply(job, new FelderTaskState.Failed(message), now);

    /// <summary>
    /// The solver couldn't take the job for a transient reason: keep it processing without a task and remember why,
    /// so polls resubmit it (DueForResubmit) until SubmissionExpired turns that reason into the failure.
    /// </summary>
    public static void Defer(StrategyJob job, string message, DateTime now)
    {
        job.State = Processing;
        job.TaskId = null;
        job.Error = Truncate(message);
        job.UpdatedAtUtc = now;
    }

    /// <summary>Exponential-style backoff: wait half the time since the first attempt (2 s to 30 s) between attempts.</summary>
    public static bool DueForResubmit(StrategyJob job, DateTime now)
    {
        if (job.State != Processing || job.TaskId is not null) return false;
        var wait = TimeSpan.FromTicks(Math.Clamp((now - job.SubmittedAtUtc).Ticks / 2, MinResubmitWait.Ticks, MaxResubmitWait.Ticks));
        return now - job.UpdatedAtUtc >= wait;
    }

    public static bool SubmissionExpired(StrategyJob job, DateTime now) =>
        job.State == Processing && job.TaskId is null && now - job.SubmittedAtUtc > SubmitGiveUpAfter;

    public static bool Retryable(StrategyJob job) => job.State == Failed;

    /// <summary>Measured from the job's own last submission, so a retry in an old run gets a full window.</summary>
    public static bool Expired(StrategyJob job, DateTime now) => job.State == Processing && now - job.SubmittedAtUtc > GiveUpAfter;

    /// <summary>Clients poll every few seconds; the solver is asked about a job at most once per interval.</summary>
    public static bool DueForPoll(StrategyJob job, DateTime now) => job.State == Processing && job.TaskId is not null && now - job.UpdatedAtUtc >= PollInterval;

    /// <summary>How far back a horizon may start: past seasons can be evaluated and replanned against what happened.</summary>
    public const int MaxYearsBack = 5;

    public static Dictionary<string, string[]> HorizonErrors(string? startMonth, string? endMonth, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        var start = Month(startMonth);
        var end = Month(endMonth);
        if (start is null) errors["startMonth"] = ["Use a month like 2026-12."];
        else if (start < new DateOnly(today.Year, today.Month, 1).AddYears(-MaxYearsBack))
            errors["startMonth"] = [$"Start at most {MaxYearsBack} years back, from {new DateOnly(today.Year, today.Month, 1).AddYears(-MaxYearsBack):yyyy-MM} on."];
        if (end is null) errors["endMonth"] = ["Use a month like 2031-10."];
        else if (start is { } first && end < first) errors["endMonth"] = ["The end month can’t come before the start."];
        else if (start is { } from && end >= from.AddMonths(MaxHorizonMonths)) errors["endMonth"] = ["Plan at most thirty years ahead."];
        return errors;
    }

    private static DateOnly? Month(string? value) =>
        value is not null && MonthPattern().IsMatch(value)
            ? DateOnly.ParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static string Truncate(string message) => message.Length <= 2000 ? message : message[..2000];

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthPattern();
}
