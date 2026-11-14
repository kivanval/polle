using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed record WorkerRequest(
    [property: Required, MaxLength(150)] string? FullName,
    [property: Required, Description("hourly (rate is USD per worked hour) or monthly (rate is a USD salary per month).")] string? PayType,
    [property: Required, Description("USD, 0 or more with at most 2 decimals.")] decimal? PayRateUsd,
    [property: MaxLength(100), Description("Free-text job title, e.g. tractor driver.")] string? Position = null,
    [property: Description("Optional labour type id from GET /api/labor-categories.")] int? LaborCategoryId = null,
    [property: MaxLength(40)] string? Phone = null,
    DateOnly? HiredOn = null,
    [property: Description("Inactive workers keep their history but are left out of planned payroll. Defaults to true.")] bool? IsActive = null,
    [property: MaxLength(2000)] string? Notes = null);

/// <summary>A worker's own week, written to one or more months at once, replacing what those months had.</summary>
public sealed record WorkerScheduleRequest(
    [property: Required, Description("Months to replace, January = 1: one to twelve distinct values.")] int[]? Months,
    [property: Required, Description("Every shift of the week, up to 168. An empty list clears the chosen months.")] OperatorShiftTemplate[]? Shifts);

public sealed record WorkerShiftResponse(int Id, int Month, int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime)
{
    public static WorkerShiftResponse From(FarmWorkerShift shift) => new(shift.Id, shift.Month, shift.DayOfWeek, shift.StartTime, shift.EndTime);
}

public sealed record WorkerResponse(int Id, Guid FarmId, string FullName, string? Position, int? LaborCategoryId, string? Phone,
    string PayType, decimal PayRateUsd, DateOnly? HiredOn, bool IsActive, string? Notes,
    [property: Description("The worker's own recurring shifts, by month and weekday.")] WorkerShiftResponse[] Shifts,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static WorkerResponse From(FarmWorker worker, IEnumerable<FarmWorkerShift> shifts) => new(worker.Id, worker.FarmId, worker.FullName,
        worker.Position, worker.LaborCategoryId, worker.Phone, worker.PayType, worker.PayRateUsd, worker.HiredOn, worker.IsActive, worker.Notes,
        shifts.OrderBy(shift => shift.Month).ThenBy(shift => shift.DayOfWeek).ThenBy(shift => shift.StartTime).Select(WorkerShiftResponse.From).ToArray(),
        worker.CreatedAtUtc, worker.UpdatedAtUtc);
}

/// <summary>The people on each farm, their pay terms and each person's own working week.</summary>
public sealed class WorkerEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Workers");
        group.MapGet("/workers", ListAsync).Produces<WorkerResponse[]>()
            .WithSummary("List the current user's workers with their shifts, optionally for one farm");
        group.MapPost("/farms/{farmId:guid}/workers", CreateAsync).Produces<WorkerResponse>(201).WithSummary("Add a worker to an owned farm");
        group.MapPut("/workers/{workerId:int}", UpdateAsync).Produces<WorkerResponse>().WithSummary("Replace a worker's details and pay terms");
        group.MapDelete("/workers/{workerId:int}", DeleteAsync).Produces(204)
            .WithSummary("Remove a worker and their shifts; expenses linked to them are kept without the link");
        group.MapPut("/workers/{workerId:int}/schedule", ReplaceScheduleAsync).Produces<WorkerResponse>()
            .WithSummary("Replace the worker's shifts in the chosen months with one week, atomically")
            .WithDescription("Months not listed are untouched. Shifts of one weekday must not overlap; adjacent shifts are allowed.");
    }

    private static async Task<IResult> ListAsync(Guid? farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var workers = await OwnedResources.Workers(db, context).AsNoTracking().Where(worker => farmId == null || worker.FarmId == farmId)
            .OrderBy(worker => worker.FullName).ThenBy(worker => worker.Id).ToListAsync(ct);
        var ids = workers.Select(worker => worker.Id).ToArray();
        var shifts = (await db.FarmWorkerShifts.AsNoTracking().Where(shift => ids.Contains(shift.WorkerId)).ToListAsync(ct)).ToLookup(shift => shift.WorkerId);
        return TypedResults.Ok(workers.Select(worker => WorkerResponse.From(worker, shifts[worker.Id])));
    }

    private static async Task<IResult> CreateAsync(Guid farmId, WorkerRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var worker = new FarmWorker { FarmId = farmId, FullName = "", PayType = "" };
        Apply(worker, request);
        db.FarmWorkers.Add(worker);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/workers/{worker.Id}", WorkerResponse.From(worker, []));
    }

    private static async Task<IResult> UpdateAsync(int workerId, WorkerRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var worker = await OwnedResources.Workers(db, context).SingleOrDefaultAsync(worker => worker.Id == workerId, ct);
        if (worker is null) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        Apply(worker, request);
        worker.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var shifts = await db.FarmWorkerShifts.AsNoTracking().Where(shift => shift.WorkerId == workerId).ToListAsync(ct);
        return TypedResults.Ok(WorkerResponse.From(worker, shifts));
    }

    private static async Task<IResult> DeleteAsync(int workerId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var deleted = await OwnedResources.Workers(db, context).Where(worker => worker.Id == workerId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<IResult> ReplaceScheduleAsync(int workerId, WorkerScheduleRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var errors = OperatorShiftEndpoints.ValidateSchedule(request.Months, request.Shifts);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var userId = OwnedResources.UserId(context);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize schedule writes per worker so two saves cannot interleave their delete-and-insert.
        var owned = await db.Database.SqlQuery<int>($"""
            SELECT w.id AS "Value" FROM public.farm_workers w JOIN public.farms f ON f.id = w.farm_id
            WHERE w.id = {workerId} AND f.owner_user_id = {userId} FOR UPDATE OF w
            """).ToListAsync(ct);
        if (owned.Count == 0) { return TypedResults.NotFound(); }
        var months = request.Months!.Distinct().Order().ToArray();
        await db.FarmWorkerShifts.Where(shift => shift.WorkerId == workerId && months.Contains(shift.Month)).ExecuteDeleteAsync(ct);
        db.FarmWorkerShifts.AddRange(months.SelectMany(month => request.Shifts!.Select(template => new FarmWorkerShift
        {
            WorkerId = workerId, Month = month, DayOfWeek = template.DayOfWeek!.Value, StartTime = template.StartTime!.Value, EndTime = template.EndTime!.Value
        })));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var worker = await db.FarmWorkers.AsNoTracking().SingleAsync(worker => worker.Id == workerId, ct);
        var shifts = await db.FarmWorkerShifts.AsNoTracking().Where(shift => shift.WorkerId == workerId).ToListAsync(ct);
        return TypedResults.Ok(WorkerResponse.From(worker, shifts));
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(WorkerRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FullName)) { errors["fullName"] = ["Enter the worker's name."]; }
        else if (request.FullName.Trim().Length > 150) { errors["fullName"] = ["Maximum length is 150 characters."]; }
        if (request.PayType is null || !FarmWorker.PayTypes.Contains(request.PayType)) { errors["payType"] = ["Choose hourly or monthly pay."]; }
        if (request.PayRateUsd is not { } rate || rate < 0 || rate >= 10_000_000m || decimal.Round(rate, 2) != rate)
        { errors["payRateUsd"] = ["Enter the rate in USD, 0 or more, below 10 million, with cents at most."]; }
        if (request.Position?.Trim().Length > 100) { errors["position"] = ["Maximum length is 100 characters."]; }
        if (request.Phone?.Trim().Length > 40) { errors["phone"] = ["Maximum length is 40 characters."]; }
        if (request.Notes?.Length > 2000) { errors["notes"] = ["Maximum length is 2000 characters."]; }
        if (request.HiredOn is { } hired && (hired.Year < 1900 || hired.Year > 2200)) { errors["hiredOn"] = ["Enter a hiring date between 1900 and 2200."]; }
        if (request.LaborCategoryId is { } id && !await db.LaborCategories.AnyAsync(category => category.Id == id, ct))
        { errors["laborCategoryId"] = ["Choose a labour type from the list."]; }
        return errors;
    }

    private static void Apply(FarmWorker worker, WorkerRequest request)
    {
        worker.FullName = request.FullName!.Trim();
        worker.Position = AgricultureValidation.Optional(request.Position);
        worker.LaborCategoryId = request.LaborCategoryId;
        worker.Phone = AgricultureValidation.Optional(request.Phone);
        worker.PayType = request.PayType!;
        worker.PayRateUsd = request.PayRateUsd!.Value;
        worker.HiredOn = request.HiredOn;
        worker.IsActive = request.IsActive ?? true;
        worker.Notes = AgricultureValidation.Optional(request.Notes);
    }
}
