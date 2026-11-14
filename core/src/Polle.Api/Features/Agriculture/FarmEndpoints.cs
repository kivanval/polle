using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed class FarmEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Farms");
        group.MapGet("/farms", ListAsync).Produces<FarmResponse[]>().WithSummary("List the current user's farms");
        group.MapGet("/farms/{farmId:guid}", GetAsync).Produces<FarmResponse>().WithSummary("Get an owned farm");
        group.MapPost("/farms", CreateAsync).Produces<FarmResponse>(201).WithSummary("Create a farm for the current user");
        group.MapPut("/farms/{farmId:guid}", UpdateAsync).Produces<FarmResponse>().WithSummary("Replace an owned farm's details");
        group.MapPut("/farms/{farmId:guid}/name", RenameAsync).Produces<FarmResponse>()
            .WithSummary("Rename an owned farm without changing its other details");
        group.MapDelete("/farms/{farmId:guid}", DeleteAsync).Produces(204)
            .WithSummary("Delete an owned farm with its fields, subfields, plantings, budget, machinery, workers and expenses");
    }

    private static async Task<IResult> ListAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farms = await OwnedResources.Farms(db, context).AsNoTracking().OrderBy(farm => farm.CreatedAtUtc).ThenBy(farm => farm.Id).ToListAsync(ct);
        return TypedResults.Ok(farms.Select(FarmResponse.From));
    }

    private static async Task<IResult> GetAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).AsNoTracking().SingleOrDefaultAsync(farm => farm.Id == farmId, ct);
        return farm is null ? TypedResults.NotFound() : TypedResults.Ok(FarmResponse.From(farm));
    }

    private static async Task<IResult> CreateAsync(FarmRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var errors = AgricultureValidation.Farm(request);
        var homePoint = AgricultureValidation.HomePoint(request.HomePoint, errors);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var farm = new Farm { OwnerUserId = OwnedResources.UserId(context), Name = request.Name!.Trim(),
            Description = AgricultureValidation.Optional(request.Description), Location = AgricultureValidation.Optional(request.Location), HomePoint = homePoint };
        db.Farms.Add(farm);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/farms/{farm.Id}", FarmResponse.From(farm));
    }

    private static async Task<IResult> UpdateAsync(Guid farmId, FarmRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).SingleOrDefaultAsync(farm => farm.Id == farmId, ct);
        if (farm is null) { return TypedResults.NotFound(); }
        var errors = AgricultureValidation.Farm(request);
        var homePoint = AgricultureValidation.HomePoint(request.HomePoint, errors);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        farm.Name = request.Name!.Trim();
        farm.Description = AgricultureValidation.Optional(request.Description);
        farm.Location = AgricultureValidation.Optional(request.Location);
        farm.HomePoint = homePoint;
        farm.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FarmResponse.From(farm));
    }

    private static async Task<IResult> RenameAsync(Guid farmId, FarmNameRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).SingleOrDefaultAsync(farm => farm.Id == farmId, ct);
        if (farm is null) { return TypedResults.NotFound(); }
        var errors = AgricultureValidation.NameAndDescription(request.Name, null);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        farm.Name = request.Name!.Trim();
        farm.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FarmResponse.From(farm));
    }

    /// <summary>
    /// Deletes the farm with everything that belongs only to it, in one transaction. Fields and plantings use RESTRICT
    /// keys, so they go first: plantings, then subfields, then top-level fields. Budget allocations, machinery, operator
    /// hours, workers with their shifts, and expenses cascade from the farm row. The crop catalog and solver data are
    /// shared and only referenced, so nothing outside this farm is touched.
    /// </summary>
    private static async Task<IResult> DeleteAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var userId = OwnedResources.UserId(context);
        // Same owner lock as field writes, then the farm and its fields: a concurrent field or planting write waits and
        // then fails on the missing parent instead of slipping in between the steps below.
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.users WHERE id = {userId} FOR UPDATE").ToListAsync(ct);
        var owned = await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM public.farms WHERE id = {farmId} AND owner_user_id = {userId} FOR UPDATE").ToListAsync(ct);
        if (owned.Count == 0) { return TypedResults.NotFound(); }
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.fields WHERE farm_id = {farmId} FOR UPDATE").ToListAsync(ct);
        await db.FieldPlantings.Where(planting => planting.Field.FarmId == farmId).ExecuteDeleteAsync(ct);
        await db.Fields.Where(field => field.FarmId == farmId && field.ParentFieldId != null).ExecuteDeleteAsync(ct);
        await db.Fields.Where(field => field.FarmId == farmId).ExecuteDeleteAsync(ct);
        await db.Farms.Where(farm => farm.Id == farmId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }
}
