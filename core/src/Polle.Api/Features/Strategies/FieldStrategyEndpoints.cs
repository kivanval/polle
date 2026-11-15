using System.ComponentModel;
using Polle.Api.Common;
using System.Globalization;
using Polle.Api.Features.Agriculture;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Strategies;

public sealed record SaveStrategyRequest(Guid RunId, [property: Description("Key of a ready strategy job, e.g. medium or custom-010-ter-qua.")] string? JobKey,
    [property: Description("Optional \"Evaluate my plan\" run of the same field and months: its score of the current plan is kept as the baseline when the strategy's run has none.")] Guid? PlanRunId = null);

public sealed record SavedStrategyResponse(Guid Id, string Key, string Name, string Summary, string StartMonth, string EndMonth, DateTime SavedAtUtc,
    StrategyResult? Result, [property: Description("The farmer's own plan scored in the same run; null when it had no crops in the horizon.")] StrategyResult? Baseline);

/// <summary>Strategies a farmer keeps for a field to compare later. Saving copies the result, so it outlives the run.</summary>
public sealed class FieldStrategyEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Strategies");
        group.MapGet("/fields/{fieldId:guid}/strategies", ListAsync).Produces<SavedStrategyResponse[]>().WithSummary("List an owned field's saved strategies, newest first");
        group.MapPost("/fields/{fieldId:guid}/strategies", SaveAsync).Produces<SavedStrategyResponse>(201)
            .WithSummary("Save a ready strategy of a run for this field");
        group.MapDelete("/strategies/{strategyId:guid}", DeleteAsync).Produces(204).WithSummary("Delete a saved strategy");
        group.MapPost("/strategies/{strategyId:guid}/apply", ApplyAsync).Produces<PlantingResponse[]>()
            .WithSummary("Make a saved strategy the field's crop plan")
            .WithDescription("Replaces the field's crops sown on or after the strategy's first month with the strategy's crops. Earlier crops stay. A crop sown earlier that is still on the field when the first new crop goes in returns 409 until it gets a harvest date.");
    }

    private static async Task<IResult> ListAsync(Guid fieldId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Fields(db, context).AnyAsync(field => field.Id == fieldId, ct)) return TypedResults.NotFound();
        var saved = await db.FieldStrategies.AsNoTracking().Where(strategy => strategy.FieldId == fieldId)
            .OrderByDescending(strategy => strategy.SavedAtUtc).ToListAsync(ct);
        var names = await StrategyProfiles.LoadAsync(db, saved.SelectMany(strategy => new[] { strategy.ResultJson, strategy.BaselineJson }), ct, fieldId);
        return TypedResults.Ok(saved.Select(strategy => Response(strategy, names)));
    }

    private static async Task<IResult> SaveAsync(Guid fieldId, SaveStrategyRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (request.JobKey == StrategyCatalog.BaselineKey)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["jobKey"] = ["Your current plan is already saved as your plan."] });
        var userId = OwnedResources.UserId(context);
        var run = await db.StrategyRuns.AsNoTracking().Include(candidate => candidate.Jobs)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.RunId && candidate.FieldId == fieldId && candidate.Field.Farm.OwnerUserId == userId, ct);
        var job = run?.Jobs.SingleOrDefault(candidate => candidate.Key == request.JobKey);
        if (run is null || job is null) return TypedResults.NotFound();
        if (job.State != StrategyJobs.Ready || job.ResultJson is null) return TypedResults.Problem(statusCode: 409, title: "This plan isn't ready yet.");

        var baseline = ReadyBaseline(run);
        // Strategies and the plan's evaluation run separately; the plan's score counts only for the same field and months.
        if (baseline is null && request.PlanRunId is { } planRunId && planRunId != run.Id)
        {
            var planRun = await db.StrategyRuns.AsNoTracking().Include(candidate => candidate.Jobs)
                .SingleOrDefaultAsync(candidate => candidate.Id == planRunId && candidate.FieldId == fieldId && candidate.Field.Farm.OwnerUserId == userId
                    && candidate.StartMonth == run.StartMonth && candidate.EndMonth == run.EndMonth, ct);
            baseline = planRun is null ? null : ReadyBaseline(planRun);
        }
        var strategy = new FieldStrategy { FieldId = fieldId, Key = job.Key, Name = job.Name, Summary = job.Summary, StartMonth = run.StartMonth, EndMonth = run.EndMonth,
            ResultJson = job.ResultJson, BaselineJson = baseline?.ResultJson };
        db.FieldStrategies.Add(strategy);
        await db.SaveChangesAsync(ct);
        var names = await StrategyProfiles.LoadAsync(db, [strategy.ResultJson, strategy.BaselineJson], ct, fieldId);
        return TypedResults.Created($"/api/fields/{fieldId}/strategies", Response(strategy, names));
    }

    private static async Task<IResult> DeleteAsync(Guid strategyId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        var deleted = await db.FieldStrategies.Where(strategy => strategy.Id == strategyId && strategy.Field.Farm.OwnerUserId == userId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<IResult> ApplyAsync(Guid strategyId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        var strategy = await db.FieldStrategies.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == strategyId && candidate.Field.Farm.OwnerUserId == userId, ct);
        if (strategy is null) return TypedResults.NotFound();
        var plan = StrategyPlan.Plantings(StrategyResultMapper.Map(strategy.ResultJson, CropProfileNames.Empty)?.Cycles ?? []);
        if (plan.Count == 0) return TypedResults.Problem(statusCode: 409, title: "This plan has no crops to plant.");
        // A saved strategy can outlive a crop removed from the catalog; planting it would break the foreign key.
        var profileIds = plan.Select(item => item.CropProfileId).Distinct().ToArray();
        var known = await db.CropProfiles.AsNoTracking().CountAsync(profile => profileIds.Contains(profile.Id), ct);
        if (known < profileIds.Length)
            return TypedResults.Problem(statusCode: 409, title: "This plan uses a crop that is no longer in the catalog.",
                detail: "Suggest plans again to get one with the current crops.",
                extensions: new Dictionary<string, object?> { ["code"] = "strategy_crop_removed" });
        var start = StrategyPlan.First(strategy.StartMonth);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await PlantingEndpoints.LockFieldFamilyAsync(db, strategy.FieldId, ct);
        var existing = await db.FieldPlantings.Include(planting => planting.CropProfile).ThenInclude(profile => profile.Crop)
            .Where(planting => planting.FieldId == strategy.FieldId).ToListAsync(ct);
        if (StrategyPlan.Blocking(existing, start, plan[0].Sown) is { } growing)
            return TypedResults.Problem(statusCode: 409, title: "A crop is still growing",
                detail: $"{growing.CropProfile.Crop.Name} is still on the field on {plan[0].Sown.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}, when this plan sows its first crop. Set an earlier harvest date, then use this plan.",
                extensions: new Dictionary<string, object?> { ["code"] = "planting_still_growing" });

        db.FieldPlantings.RemoveRange(StrategyPlan.Replaced(existing, start));
        await db.SaveChangesAsync(ct);
        foreach (var item in plan)
        {
            var request = new PlantingRequest(item.CropProfileId, item.SeasonYear, item.Sown, item.Harvested, null, item.IrrigationMm, item.FertilizerMaterialId,
                item.FertilizerRateKgHa, item.IrrigationTargetPercent, item.FertilizerTargetPercent);
            if (await PlantingEndpoints.RelatedOverlapAsync(db, strategy.FieldId, null, request, ct) is { } related) return related;
            var planting = new FieldPlanting { FieldId = strategy.FieldId, CropProfileId = item.CropProfileId, SeasonYear = item.SeasonYear,
                PlantingDate = item.Sown, HarvestingDate = item.Harvested };
            // The strategy's water, fertilizer and their target levels, so "Evaluate my plan" scores the copy as the strategy.
            PlantingEndpoints.ApplyInputs(planting, request);
            db.FieldPlantings.Add(planting);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var plantings = await db.FieldPlantings.AsNoTracking().Include(planting => planting.CropProfile)
            .Where(planting => planting.FieldId == strategy.FieldId).OrderBy(planting => planting.PlantingDate).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return TypedResults.Ok(plantings.Select(planting => PlantingResponse.From(planting, today)).ToArray());
    }

    private static StrategyJob? ReadyBaseline(StrategyRun run) =>
        run.Jobs.SingleOrDefault(candidate => candidate.Key == StrategyCatalog.BaselineKey && candidate.State == StrategyJobs.Ready);

    private static SavedStrategyResponse Response(FieldStrategy strategy, CropProfileNames names)
    {
        var (name, summary) = StrategyRunResponse.Describe(strategy.Key, strategy.Name, strategy.Summary);
        return new(strategy.Id, strategy.Key, name, summary, strategy.StartMonth, strategy.EndMonth, strategy.SavedAtUtc,
            StrategyResultMapper.Map(strategy.ResultJson, names), StrategyResultMapper.Map(strategy.BaselineJson, names));
    }
}
