using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

/// <summary>A recurring farm-level shift template; it does not specify a worker count or calculated capacity.</summary>
public sealed record OperatorShiftRequest(
    [property: Required, Range(1, 12), Description("Month number, January = 1.")] int? Month,
    [property: Required, Range(1, 7), Description("ISO weekday, Monday = 1 and Sunday = 7.")] int? DayOfWeek,
    [property: Required, Description("Local same-day start, HH:mm; no timezone or year is assigned.")] TimeOnly? StartTime,
    [property: Required, Description("Local same-day end, HH:mm; must be later than start.")] TimeOnly? EndTime,
    [property: Description("Labour type id from GET /api/labor-categories; null is the farm default week.")] int? LaborCategoryId = null);

/// <summary>One shift of a week template; month is chosen by the surrounding request.</summary>
public sealed record OperatorShiftTemplate(
    [property: Required, Range(1, 7), Description("ISO weekday, Monday = 1 and Sunday = 7.")] int? DayOfWeek,
    [property: Required, Description("Local same-day start, HH:mm.")] TimeOnly? StartTime,
    [property: Required, Description("Local same-day end, HH:mm; later than start. Use 23:59 for the end of the day.")] TimeOnly? EndTime);

/// <summary>A whole week template written to one or more months in one step, replacing what those months had.</summary>
public sealed record OperatorScheduleRequest(
    [property: Required, Description("Months to replace, January = 1: one to twelve distinct values.")] int[]? Months,
    [property: Required, Description("Every shift of the week template, up to 168. An empty list clears the chosen months.")] OperatorShiftTemplate[]? Shifts,
    [property: Description("Labour type id from GET /api/labor-categories whose week is replaced; null replaces the farm default week. A type with no shifts of its own follows the default.")] int? LaborCategoryId = null);

/// <summary>A stored same-day shift template, without a calendar year or timezone.</summary>
public sealed record OperatorShiftResponse(int Id, Guid FarmId,
    [property: Description("Labour type id; null for the farm default week.")] int? LaborCategoryId,
    int Month, int DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static OperatorShiftResponse From(FarmOperatorShift shift) =>
        new(shift.Id, shift.FarmId, shift.LaborCategoryId, shift.Month, shift.DayOfWeek, shift.StartTime, shift.EndTime, shift.CreatedAtUtc, shift.UpdatedAtUtc);
}

public sealed class OperatorShiftEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Operator schedule");
        group.MapGet("/farms/{farmId:guid}/operator-shifts", ListAsync).Produces<OperatorShiftResponse[]>()
            .WithSummary("List recurring working-time windows: the farm default week and each labour type's own week");
        group.MapPut("/farms/{farmId:guid}/operator-shifts/schedule", ReplaceScheduleAsync).Produces<OperatorShiftResponse[]>()
            .WithSummary("Replace the shifts of the chosen months with one week template, atomically")
            .WithDescription("Used by the schedule matrix, one labour type (or the farm default) at a time. Months and other labour types not listed are untouched. Shifts of one weekday must not overlap; adjacent shifts are allowed. Another request for the same farm waits for this one to finish.");
        group.MapPost("/farms/{farmId:guid}/operator-shifts", CreateAsync).Produces<OperatorShiftResponse>(201)
            .WithSummary("Add a same-day monthly weekday shift template");
        group.MapPut("/farms/{farmId:guid}/operator-shifts/{shiftId:int}", UpdateAsync).Produces<OperatorShiftResponse>()
            .WithSummary("Update one owned shift template");
        group.MapDelete("/farms/{farmId:guid}/operator-shifts/{shiftId:int}", DeleteAsync).Produces(204)
            .WithSummary("Remove one owned shift template");
    }

    private static async Task<IResult> ListAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var shifts = await OwnedResources.OperatorShifts(db, context).AsNoTracking().Where(shift => shift.FarmId == farmId)
            .OrderBy(shift => shift.LaborCategoryId).ThenBy(shift => shift.Month).ThenBy(shift => shift.DayOfWeek).ThenBy(shift => shift.StartTime).ThenBy(shift => shift.Id)
            .ToListAsync(ct);
        return TypedResults.Ok(shifts.Select(OperatorShiftResponse.From));
    }

    private static async Task<IResult> CreateAsync(Guid farmId, OperatorShiftRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(request, farmId, null, db, context, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var shift = new FarmOperatorShift { FarmId = farmId };
        Apply(shift, request);
        db.FarmOperatorShifts.Add(shift);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/farms/{farmId}/operator-shifts/{shift.Id}", OperatorShiftResponse.From(shift));
    }

    private static async Task<IResult> ReplaceScheduleAsync(Guid farmId, OperatorScheduleRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var errors = ValidateSchedule(request.Months, request.Shifts);
        await ValidateLaborCategoryAsync(request.LaborCategoryId, errors, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var laborCategoryId = request.LaborCategoryId;
        var userId = OwnedResources.UserId(context);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Serialize schedule writes per farm so two saves cannot interleave their delete-and-insert. Never locks another owner's farm.
        var owned = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.farms WHERE id = {farmId} AND owner_user_id = {userId} FOR UPDATE").ToListAsync(ct);
        if (owned.Count == 0) { return TypedResults.NotFound(); }
        var months = request.Months!.Distinct().Order().ToArray();
        await OwnedResources.OperatorShifts(db, context).Where(shift => shift.FarmId == farmId && shift.LaborCategoryId == laborCategoryId && months.Contains(shift.Month)).ExecuteDeleteAsync(ct);
        var created = months.SelectMany(month => request.Shifts!.Select(template => new FarmOperatorShift
        {
            FarmId = farmId, LaborCategoryId = laborCategoryId, Month = month, DayOfWeek = template.DayOfWeek!.Value, StartTime = template.StartTime!.Value, EndTime = template.EndTime!.Value
        })).ToList();
        db.FarmOperatorShifts.AddRange(created);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(created.OrderBy(shift => shift.Month).ThenBy(shift => shift.DayOfWeek).ThenBy(shift => shift.StartTime).Select(OperatorShiftResponse.From));
    }

    /// <summary>Checks a week template and its months; shared with the per-worker schedules.</summary>
    internal static Dictionary<string, string[]> ValidateSchedule(int[]? requestMonths, OperatorShiftTemplate[]? requestShifts)
    {
        var errors = new Dictionary<string, string[]>();
        if (requestMonths is not { Length: >= 1 and <= 12 } months || months.Any(month => month is < 1 or > 12))
        { errors["months"] = ["Choose between one and twelve months, January through December."]; }
        if (requestShifts is not { Length: <= 168 } shifts) { errors["shifts"] = ["Send the week as a list of at most 168 shifts; an empty list clears the months."]; return errors; }
        var problems = new List<string>();
        foreach (var (shift, index) in shifts.Select((shift, index) => (shift, index)))
        {
            var label = $"Shift {index + 1}";
            if (shift is null || shift.DayOfWeek is not (>= 1 and <= 7)) { problems.Add($"{label}: choose a day from Monday through Sunday."); continue; }
            if (shift.StartTime is not { } start || shift.EndTime is not { } end
                || start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0) { problems.Add($"{label}: enter start and end times in HH:mm, to the minute."); continue; }
            if (start >= end) { problems.Add($"{label}: the end must be later than the start on the same day."); }
        }
        if (problems.Count == 0)
        {
            foreach (var day in shifts.GroupBy(shift => shift.DayOfWeek!.Value))
            {
                var ordered = day.OrderBy(shift => shift.StartTime).ToArray();
                for (var i = 1; i < ordered.Length; i++)
                {
                    if (ordered[i].StartTime < ordered[i - 1].EndTime) { problems.Add($"Shifts on weekday {day.Key} overlap."); break; }
                }
            }
        }
        if (problems.Count > 0) { errors["shifts"] = problems.Take(5).ToArray(); }
        return errors;
    }

    private static async Task<IResult> UpdateAsync(Guid farmId, int shiftId, OperatorShiftRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var shift = await OwnedResources.OperatorShifts(db, context).SingleOrDefaultAsync(shift => shift.FarmId == farmId && shift.Id == shiftId, ct);
        if (shift is null) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(request, farmId, shiftId, db, context, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        Apply(shift, request);
        shift.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OperatorShiftResponse.From(shift));
    }

    private static async Task<IResult> DeleteAsync(Guid farmId, int shiftId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var deleted = await OwnedResources.OperatorShifts(db, context).Where(shift => shift.FarmId == farmId && shift.Id == shiftId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(OperatorShiftRequest request, Guid farmId, int? exceptId,
        AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Month is not (>= 1 and <= 12)) { errors["month"] = ["Choose a month from January through December."]; }
        if (request.DayOfWeek is not (>= 1 and <= 7)) { errors["dayOfWeek"] = ["Choose a day from Monday through Sunday."]; }
        if (request.StartTime is not { } start || start.Ticks % TimeSpan.TicksPerMinute != 0)
        { errors["startTime"] = ["Enter a start time in HH:mm, to the minute."]; }
        if (request.EndTime is not { } end || end.Ticks % TimeSpan.TicksPerMinute != 0)
        { errors["endTime"] = ["Enter an end time in HH:mm, to the minute."]; }
        if (request.StartTime is { } validStart && request.EndTime is { } validEnd && validStart >= validEnd)
        { errors["endTime"] = ["The end must be later on the same day. Split overnight work into separate daily shifts."]; }
        await ValidateLaborCategoryAsync(request.LaborCategoryId, errors, db, ct);
        if (errors.Count > 0) { return errors; }
        var overlaps = await OwnedResources.OperatorShifts(db, context).AnyAsync(shift => shift.FarmId == farmId && shift.Id != exceptId
            && shift.LaborCategoryId == request.LaborCategoryId
            && shift.Month == request.Month && shift.DayOfWeek == request.DayOfWeek
            && shift.StartTime < request.EndTime && request.StartTime < shift.EndTime, ct);
        if (overlaps) { errors["startTime"] = ["This shift overlaps another shift for the selected month and weekday."]; }
        return errors;
    }

    private static async Task ValidateLaborCategoryAsync(int? laborCategoryId, Dictionary<string, string[]> errors, AppDbContext db, CancellationToken ct)
    {
        if (laborCategoryId is { } id && !await db.LaborCategories.AnyAsync(category => category.Id == id, ct))
        { errors["laborCategoryId"] = ["Choose a labour type from the list."]; }
    }

    private static void Apply(FarmOperatorShift shift, OperatorShiftRequest request)
    {
        shift.LaborCategoryId = request.LaborCategoryId;
        shift.Month = request.Month!.Value;
        shift.DayOfWeek = request.DayOfWeek!.Value;
        shift.StartTime = request.StartTime!.Value;
        shift.EndTime = request.EndTime!.Value;
    }
}
