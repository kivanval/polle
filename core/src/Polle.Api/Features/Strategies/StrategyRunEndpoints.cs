using System.Globalization;
using Polle.Api.Common;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Fields;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Strategies;

/// <summary>
/// "Suggest strategies" submits the strategy optimisations and "Evaluate my plan" the evaluation of the farmer's own plan
/// to the felder solver, each as its own run; the jobs advance whenever the client polls. The solver is never called from
/// the browser.
/// </summary>
public sealed class StrategyRunEndpoints : IEndpoint
{
    /// <summary>In the problem body when the solver, not this API or its database, is what's missing.</summary>
    public const string SolverUnavailableCode = "solver_unavailable";
    internal const string SolverUnavailableText = "The plan solver is temporarily unavailable.";

    /// <summary>503 naming the solver, so the client says the strategy service is down rather than the whole workspace.</summary>
    internal static IResult SolverUnavailable() => TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Plan solver unavailable", detail: $"{SolverUnavailableText} Please try again shortly.",
        extensions: new Dictionary<string, object?> { ["code"] = SolverUnavailableCode });

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Strategies");
        group.MapPost("/fields/{fieldId:guid}/strategy-runs", StartAsync).Produces<StrategyRunResponse>(201).ProducesProblem(422)
            .WithSummary("Ask the solver for the rotation strategies of an owned field, or (planOnly) score its current plan");
        group.MapGet("/strategy-runs/{runId:guid}", GetAsync).Produces<StrategyRunResponse>()
            .WithSummary("Read a strategy run; jobs still processing are checked with the solver first")
            .WithDescription("Poll every few seconds until no job is processing. A job's result appears once its state is ready.");
        group.MapPost("/strategy-runs/{runId:guid}/jobs/{key}/retry", RetryAsync).Produces<StrategyRunResponse>()
            .WithSummary("Resubmit one failed job of a run");
        group.MapGet("/strategy-presets", () => TypedResults.Ok(StrategyCatalog.Presets
                .Select(preset => new StrategyPresetResponse(preset.Key, preset.Name, preset.Summary, StrategySettings.From(preset.Options))).ToArray()))
            .WithSummary("The preset strategies a run uses by default, with the solver settings of each");
    }

    private static async Task<IResult> StartAsync(Guid fieldId, StrategyRunRequest request, AppDbContext db, FelderClient solver,
        SolverMaterials materials, NasaPowerDailyClimate climate, HttpContext context, ILogger<StrategyRunEndpoints> log, CancellationToken ct)
    {
        if (!solver.Configured) return SolverUnavailable();
        var errors = StrategyJobs.HorizonErrors(request.StartMonth, request.EndMonth, DateOnly.FromDateTime(DateTime.UtcNow));
        // Scoring only the current plan runs no strategies, whatever was chosen.
        var (strategies, choiceErrors) = request.PlanOnly ? ([], []) : StrategyChoices.Picks(request.Strategies);
        foreach (var (key, messages) in choiceErrors) errors[key] = messages;
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        var boundary = await OwnedResources.Fields(db, context).Where(field => field.Id == fieldId).Select(field => field.Boundary).SingleOrDefaultAsync(ct);
        if (boundary is null) return TypedResults.NotFound();
        if (!FieldSoil.Available(boundary, LandMask.Default)) return NoSoil();
        if (request.PlanOnly)
        {
            var plantings = await db.FieldPlantings.AsNoTracking().Where(planting => planting.FieldId == fieldId).ToListAsync(ct);
            if (StrategyRequests.Baseline(fieldId, request.StartMonth!, request.EndMonth!, plantings) is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["planOnly"] = ["Your plan has no crops sown in this period. Add crops to the plan, or choose other months."],
                });
        }

        var run = await CreateAsync(fieldId, request.StartMonth!, request.EndMonth!, strategies, db, solver, materials, ct,
            (hasBaseline, now) => StrategyJobs.ForRequest(request.PlanOnly, hasBaseline, strategies, now));
        if (!request.PlanOnly) return TypedResults.Created($"/api/strategy-runs/{run.Id}", StrategyRunResponse.From(run, CropProfileNames.Empty));
        // The plan's evaluation is one quick job: wait for it here so the card shows without waiting for the next poll.
        if (await AwaitQuickResultAsync(run, solver, StrategyJobs.QuickWait, ct, log)) await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/strategy-runs/{run.Id}", await ResponseAsync(db, run, climate, ct, log));
    }

    /// <summary>
    /// Asks the solver about the run's submitted jobs every <see cref="StrategyJobs.QuickPollEvery"/> until none is
    /// processing or <paramref name="wait"/> has passed; true when a job changed (the caller saves). A job not ready by then
    /// stays processing for the client's polls; a job the solver hasn't accepted yet (deferred) is left to them too.
    /// </summary>
    internal static async Task<bool> AwaitQuickResultAsync(StrategyRun run, FelderClient solver, TimeSpan wait, CancellationToken ct,
        ILogger? log = null)
    {
        var deadline = DateTime.UtcNow + wait;
        var changed = false;
        while (DateTime.UtcNow < deadline)
        {
            var pending = run.Jobs.Where(job => job.State == StrategyJobs.Processing && job.TaskId is not null).ToArray();
            if (pending.Length == 0) break;
            await Task.Delay(StrategyJobs.QuickPollEvery, ct);
            var states = await Task.WhenAll(pending.Select(job => PollAsync(job, solver, ct, log)));
            foreach (var (job, state) in pending.Zip(states))
            {
                if (state is null) continue;
                StrategyJobs.Apply(job, state, DateTime.UtcNow);
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Starts a run for a field the caller already checked (owned, with soil, valid horizon), all its jobs submitted to the
    /// solver at once and saved. By default (the assistant) that is the farmer's plan when it has crops in the horizon, then
    /// every chosen strategy; the planning view passes <paramref name="jobs"/> to run either the plan or the strategies.
    /// </summary>
    internal static async Task<StrategyRun> CreateAsync(Guid fieldId, string startMonth, string endMonth, IReadOnlyList<StrategyPick> strategies,
        AppDbContext db, FelderClient solver, SolverMaterials materials, CancellationToken ct, Func<bool, DateTime, List<StrategyJob>>? jobs = null)
    {
        var plantings = await db.FieldPlantings.AsNoTracking().Where(planting => planting.FieldId == fieldId).ToListAsync(ct);
        var baseline = StrategyRequests.Baseline(fieldId, startMonth, endMonth, plantings, materials.Irrigation);
        var run = new StrategyRun { FieldId = fieldId, StartMonth = startMonth, EndMonth = endMonth };
        run.Jobs = jobs?.Invoke(baseline is not null, DateTime.UtcNow) ?? StrategyJobs.Plan(baseline is not null, strategies, DateTime.UtcNow);

        await Task.WhenAll(run.Jobs.Select(job => SubmitAsync(job, run, baseline, solver, materials, ct)));
        db.StrategyRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    private static async Task<IResult> GetAsync(Guid runId, AppDbContext db, FelderClient solver, SolverMaterials materials,
        NasaPowerDailyClimate climate, HttpContext context, ILogger<StrategyRunEndpoints> log, CancellationToken ct)
    {
        var run = await OwnedRunAsync(db, context, runId, ct);
        if (run is null) return TypedResults.NotFound();
        await AdvanceAsync(run, db, solver, materials, ct, log);
        return TypedResults.Ok(await ResponseAsync(db, run, climate, ct, log));
    }

    /// <summary>
    /// Fails jobs the solver never accepted, resubmits deferred ones and polls the processing ones, saving any change.
    /// Shared by the run endpoint's poll and the assistant, which reads a field's run the same way.
    /// </summary>
    internal static async Task AdvanceAsync(StrategyRun run, AppDbContext db, FelderClient solver, SolverMaterials materials, CancellationToken ct,
        ILogger? log = null)
    {
        var now = DateTime.UtcNow;
        var changed = false;
        // Jobs the solver hasn't accepted yet (blocked, throttled or unreachable at submission): give up after a while,
        // otherwise send them again once their backoff has passed.
        foreach (var job in run.Jobs.Where(job => StrategyJobs.SubmissionExpired(job, now)))
        {
            StrategyJobs.Fail(job, $"{SolverUnavailableText} It didn't accept this task: {job.Error ?? "no answer"}", now);
            changed = true;
        }
        var resubmit = run.Jobs.Where(job => StrategyJobs.DueForResubmit(job, now)).ToArray();
        var due = run.Jobs.Where(job => StrategyJobs.DueForPoll(job, now)).ToArray();
        if (solver.Configured && resubmit.Length > 0)
        {
            var baseline = resubmit.Any(job => job.Kind == StrategyJobs.Evaluation) ? await BaselineAsync(db, run, materials, ct) : null;
            await Task.WhenAll(resubmit.Select(job => SubmitAsync(job, run, baseline, solver, materials, ct)));
            changed = true;
        }
        if (solver.Configured && due.Length > 0)
        {
            var states = await Task.WhenAll(due.Select(job => PollAsync(job, solver, ct, log)));
            foreach (var (job, state) in due.Zip(states))
            {
                if (state is not null) StrategyJobs.Apply(job, state, now);
                if (StrategyJobs.Expired(job, now))
                    StrategyJobs.Fail(job, "The solver didn't finish in time.", now);
            }
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The run as the cards show it. The solver's evaluation comes first: since felder's 2026-11-13 update it returns every
    /// planted crop with its own simulated money, yield, monthly water and N-P-K and its own notes (drought, fertilizer,
    /// carry-over, rotation), and those are shown as they are. Only when the solver has finished with the farmer's plan
    /// without scoring it (failed, or no crops back) does its card get this API's audit instead (<see cref="PlanAudit"/>),
    /// so it still has a summary, water balance and N-P-K. The audit adds no risks or warnings: the card shows only the ones
    /// felder returned, so an evaluation without risks reads 0 risks. When it scored only some of the crops (results stored
    /// before that update, which dropped crops it couldn't simulate), the missing ones are filled in from the same audit and
    /// marked estimated (<see cref="PlanAudit.Complete"/>).
    /// </summary>
    internal static async Task<StrategyRunResponse> ResponseAsync(AppDbContext db, StrategyRun run, NasaPowerDailyClimate? climate, CancellationToken ct,
        ILogger? log = null)
    {
        var response = StrategyRunResponse.From(run, await CropProfileNamesAsync(db, run, ct, log));
        var index = Array.FindIndex(response.Jobs, job => job.Key == StrategyCatalog.BaselineKey);
        if (index < 0) return response;
        var baseline = response.Jobs[index];
        var unscored = NeedsAudit(baseline);
        // A scored plan is audited too when the solver left crops out or sent a crop without its water by month.
        if (!unscored && (baseline.State != StrategyJobs.Ready || baseline.Result is not { } scored
            || (scored.Cycles.Length >= await PlantingsInHorizonAsync(db, run, ct) && scored.Cycles.All(cycle => cycle.MonthlyWater.Length > 0)))) return response;
        try
        {
            var audit = await PlanAuditData.AuditAsync(db, climate, run.FieldId, run.StartMonth, run.EndMonth, ct, log);
            if (audit is null) return response;
            var jobs = response.Jobs.ToArray();
            jobs[index] = unscored
                ? baseline with { State = StrategyJobs.Ready, Result = audit, Error = null, Estimated = true }
                : baseline with { Result = PlanAudit.Complete(baseline.Result!, audit) };
            return response with { Jobs = jobs };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.LogError(ex, "Auditing the current plan of strategy run {RunId} failed", run.Id);
            return response;
        }
    }

    /// <summary>The solver is done with the current plan but gave it no crops to show: failed, unreadable or empty.</summary>
    internal static bool NeedsAudit(StrategyJobResponse job) =>
        job.State == StrategyJobs.Failed || (job.State == StrategyJobs.Ready && (job.Result is null || job.Result.Cycles.Length == 0));

    private static async Task<IResult> RetryAsync(Guid runId, string key, AppDbContext db, FelderClient solver, SolverMaterials materials,
        NasaPowerDailyClimate climate, HttpContext context, ILogger<StrategyRunEndpoints> log, CancellationToken ct)
    {
        if (!solver.Configured) return SolverUnavailable();
        var run =await OwnedRunAsync(db, context, runId, ct);
        var job = run?.Jobs.SingleOrDefault(candidate => candidate.Key == key);
        if (run is null || job is null) return TypedResults.NotFound();
        if (!StrategyJobs.Retryable(job)) return TypedResults.Problem(statusCode: 409, title: "Only a plan that couldn't be prepared can be retried.");
        // The outline may have been redrawn since the run started.
        var boundary = await db.Fields.AsNoTracking().Where(field => field.Id == run.FieldId).Select(field => field.Boundary).SingleAsync(ct);
        if (!FieldSoil.Available(boundary, LandMask.Default)) return NoSoil();

        var baseline = job.Kind == StrategyJobs.Evaluation ? await BaselineAsync(db, run, materials, ct) : null;
        job.State = StrategyJobs.Processing;
        job.TaskId = null;
        job.Error = null;
        job.ResultJson = null;
        job.UpdatedAtUtc = job.SubmittedAtUtc = DateTime.UtcNow;
        await SubmitAsync(job, run, baseline, solver, materials, ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ResponseAsync(db, run, climate, ct, log));
    }

    /// <summary>How many of the field's plantings are sown inside the run's horizon: the crops the evaluation was sent.</summary>
    private static Task<int> PlantingsInHorizonAsync(AppDbContext db, StrategyRun run, CancellationToken ct)
    {
        var first = DateOnly.ParseExact($"{run.StartMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var last = DateOnly.ParseExact($"{run.EndMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
        return db.FieldPlantings.AsNoTracking()
            .CountAsync(planting => planting.FieldId == run.FieldId && planting.PlantingDate >= first && planting.PlantingDate <= last, ct);
    }

    private static async Task<EvaluateScenarioRequest?> BaselineAsync(AppDbContext db, StrategyRun run, SolverMaterials materials, CancellationToken ct)
    {
        var plantings = await db.FieldPlantings.AsNoTracking().Where(planting => planting.FieldId == run.FieldId).ToListAsync(ct);
        return StrategyRequests.Baseline(run.FieldId, run.StartMonth, run.EndMonth, plantings, materials.Irrigation);
    }

    /// <summary>
    /// Sends one job to the solver. A transient refusal (Cloudflare block, throttling, gateway error, timeout, no connection)
    /// leaves the job processing for a later poll to resubmit; only a definite answer such as 422 fails it.
    /// SubmittedAtUtc marks the first attempt until the solver accepts, then restarts so the task gets the full GiveUpAfter window.
    /// </summary>
    internal static async Task SubmitAsync(StrategyJob job, StrategyRun run, EvaluateScenarioRequest? baseline, FelderClient solver,
        SolverMaterials materials, CancellationToken ct)
    {
        try
        {
            if (job.Kind == StrategyJobs.Evaluation)
            {
                if (baseline is null) { StrategyJobs.Fail(job, "No crops are sown inside this horizon.", DateTime.UtcNow); return; }
                job.TaskId = await solver.SubmitEvaluationAsync(baseline, ct);
            }
            else
            {
                var strategy = StrategyCatalog.Find(job.Key) ?? throw new InvalidOperationException($"Unknown strategy {job.Key}.");
                job.TaskId = await solver.SubmitOptimizationAsync(StrategyRequests.Optimization(run.FieldId, run.StartMonth, run.EndMonth, strategy, materials), ct);
            }
            job.Error = null;
            job.UpdatedAtUtc = job.SubmittedAtUtc = DateTime.UtcNow;
        }
        catch (HttpRequestException ex) when (FelderClient.Transient(ex.StatusCode))
        {
            StrategyJobs.Defer(job, ex.Message, DateTime.UtcNow);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            StrategyJobs.Defer(job, "The solver didn't answer in time.", DateTime.UtcNow);
        }
        catch (HttpRequestException ex)
        {
            StrategyJobs.Fail(job, ex.Message, DateTime.UtcNow);
        }
        // Anything else (a request this strategy can't be built into, an odd answer) fails this card only; the run's
        // other strategies were submitted in parallel and must still come back.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            StrategyJobs.Fail(job, $"{ex.GetType().Name}: {ex.Message}", DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Null when the solver could not be reached or answered something unexpected: the job stays processing, is asked
    /// again on the next poll, and StrategyJobs.Expired gives up on it. It never throws, so one job can't stop the others.
    /// </summary>
    internal static async Task<FelderTaskState?> PollAsync(StrategyJob job, FelderClient solver, CancellationToken ct, ILogger? log = null)
    {
        try
        {
            return await solver.GetStateAsync(job.Kind == StrategyJobs.Evaluation ? FelderTaskKind.Evaluation : FelderTaskKind.Optimization, job.TaskId!, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.LogWarning(ex, "Polling strategy job {JobKey} (task {TaskId}) failed unexpectedly", job.Key, job.TaskId);
            return null;
        }
    }

    /// <summary>
    /// 422 for a field with no soil (drawn in the sea, or without a valid WGS84 outline): felder would get soil null
    /// from the topology route and must not model it.
    /// </summary>
    internal static IResult NoSoil() => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: FieldSoil.MissingMessage, detail: FieldSoil.MissingMessage);

    private static Task<StrategyRun?> OwnedRunAsync(AppDbContext db, HttpContext context, Guid runId, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        return db.StrategyRuns.Include(run => run.Jobs).SingleOrDefaultAsync(run => run.Id == runId && run.Field.Farm.OwnerUserId == userId, ct);
    }

    /// <summary>
    /// Crop, fertilizer and OpEx lookups for the cards. If they fail (a catalog table changed in the database) the cards
    /// still answer, with unnamed crops and without operating costs, instead of every poll failing and no card showing.
    /// </summary>
    internal static async Task<CropProfileNames> CropProfileNamesAsync(AppDbContext db, StrategyRun run, CancellationToken ct, ILogger? log = null)
    {
        try { return await StrategyProfiles.LoadAsync(db, run.Jobs.Select(job => job.ResultJson), ct, run.FieldId, log); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.LogError(ex, "Loading crop and fertilizer names for strategy run {RunId} failed", run.Id);
            return CropProfileNames.Empty;
        }
    }
}
