using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Polle.Api.Features.Agriculture;

public sealed class FieldEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Fields");
        group.MapGet("/farms/{farmId:guid}/fields", ListAsync).Produces<FieldResponse[]>().WithSummary("List fields in an owned farm");
        group.MapPost("/farms/{farmId:guid}/fields", CreateAsync).Produces<FieldResponse>(201)
            .WithSummary("Create a field with a GeoJSON Polygon; hectares are computed by PostGIS")
            .WithDescription("With parentFieldId the field is a subfield: a part of a top-level field of the same farm, drawn inside it. Subfields may overlap only their parent.");
        group.MapGet("/fields/{fieldId:guid}", GetAsync).Produces<FieldResponse>().WithSummary("Get an owned field and its boundary");
        group.MapPut("/fields/{fieldId:guid}", UpdateAsync).Produces<FieldResponse>()
            .WithSummary("Replace field details, soil and climate conditions and boundary; recompute hectares");
        group.MapDelete("/fields/{fieldId:guid}", DeleteAsync).Produces(204)
            .WithSummary("Delete a field with its subfields, crop history and saved strategies; linked expenses keep no field");
    }

    private static async Task<IResult> ListAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var fields = await OwnedResources.Fields(db, context).AsNoTracking().Where(field => field.FarmId == farmId)
            .OrderBy(field => field.CreatedAtUtc).ThenBy(field => field.Id).ToListAsync(ct);
        return TypedResults.Ok(fields.Select(FieldResponse.From));
    }

    private static async Task<IResult> GetAsync(Guid fieldId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var field = await OwnedResources.Fields(db, context).AsNoTracking().SingleOrDefaultAsync(field => field.Id == fieldId, ct);
        return field is null ? TypedResults.NotFound() : TypedResults.Ok(FieldResponse.From(field));
    }

    private static async Task<IResult> CreateAsync(Guid farmId, FieldRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(db, context, ct);
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var errors = AgricultureValidation.NameAndDescription(request.Name, request.Description);
        var boundary = AgricultureValidation.Boundary(request.Boundary, errors);
        AgricultureValidation.FieldConditions(request, errors);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        if (request.ParentFieldId is { } parentId)
        {
            var parent = await OwnedResources.Fields(db, context).AsNoTracking()
                .Where(field => field.Id == parentId).Select(field => new { field.FarmId, field.ParentFieldId }).SingleOrDefaultAsync(ct);
            if (parent is null || parent.FarmId != farmId)
            { return Invalid("parentFieldId", "Choose a field of this farm as the parent."); }
            if (parent.ParentFieldId is not null)
            { return Invalid("parentFieldId", "A subfield cannot be divided further. Choose a top-level field as the parent."); }
        }
        if (await ContainmentErrorsAsync(db, boundary!, request.ParentFieldId, Guid.Empty, ct) is { } outside) { return outside; }
        if (await OverlapErrorsAsync(db, context, boundary!, Guid.Empty, request.ParentFieldId, ct) is { } overlap) { return overlap; }
        var field = new Field { FarmId = farmId, ParentFieldId = request.ParentFieldId, Name = request.Name!.Trim(),
            Description = AgricultureValidation.Optional(request.Description), Boundary = boundary! };
        ApplyConditions(field, request);
        db.Fields.Add(field);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/fields/{field.Id}", FieldResponse.From(field));
    }

    private static async Task<IResult> UpdateAsync(Guid fieldId, FieldRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(db, context, ct);
        var field = await OwnedResources.Fields(db, context).SingleOrDefaultAsync(field => field.Id == fieldId, ct);
        if (field is null) { return TypedResults.NotFound(); }
        var errors = AgricultureValidation.NameAndDescription(request.Name, request.Description);
        var boundary = AgricultureValidation.Boundary(request.Boundary, errors);
        AgricultureValidation.FieldConditions(request, errors);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        // The parent link is fixed at creation, so a field can never become its own ancestor.
        if (await ContainmentErrorsAsync(db, boundary!, field.ParentFieldId, field.Id, ct) is { } outside) { return outside; }
        if (await OverlapErrorsAsync(db, context, boundary!, field.Id, field.ParentFieldId, ct) is { } overlap) { return overlap; }
        field.Name = request.Name!.Trim();
        field.Description = AgricultureValidation.Optional(request.Description);
        field.Boundary = boundary!;
        ApplyConditions(field, request);
        field.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(FieldResponse.From(field));
    }

    private static async Task LockOwnerAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        // Serialize boundary checks across this owner's farms; the following query gets a fresh
        // READ COMMITTED snapshot after the preceding writer commits. Never locks another owner.
        var userId = OwnedResources.UserId(context);
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM public.users WHERE id = {userId} FOR UPDATE").ToListAsync(ct);
    }

    /// <summary>The farmer's soil and climate details; like the name and description, an update replaces them.</summary>
    private static void ApplyConditions(Field field, FieldRequest request)
    {
        field.DroughtLevel = AgricultureValidation.Optional(request.DroughtLevel);
        field.SoilDrainage = AgricultureValidation.Optional(request.SoilDrainage);
        field.SoilTexture = AgricultureValidation.Optional(request.SoilTexture);
        field.InitialNKg = request.InitialNKg;
        field.InitialPKg = request.InitialPKg;
        field.InitialKKg = request.InitialKKg;
        field.InitialDepletionMm = request.InitialDepletionMm;
    }

    private static IResult Invalid(string key, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    /// <summary>
    /// A subfield must lie inside its parent, and a parent's new boundary must still hold every subfield.
    /// Up to 1 m² outside is tolerated, as for overlaps.
    /// </summary>
    private static async Task<IResult?> ContainmentErrorsAsync(AppDbContext db, Polygon boundary, Guid? parentId, Guid fieldId, CancellationToken ct)
    {
        if (parentId is { } parent)
        {
            var outside = await db.Database.SqlQuery<double>($"""
                SELECT ST_Area(ST_Difference({boundary}, field.boundary)::geography) AS "Value"
                FROM public.fields AS field WHERE field.id = {parent}
                """).ToListAsync(ct);
            if (outside is [> 1.0]) { return Invalid("boundary", "A subfield must lie inside its parent field. Move the corners inside the parent’s boundary."); }
        }
        var escaped = await db.Database.SqlQuery<string>($"""
            SELECT child.name AS "Value"
            FROM public.fields AS child
            WHERE child.parent_field_id = {fieldId}
              AND ST_Area(ST_Difference(child.boundary, {boundary})::geography) > 1.0
            ORDER BY child.created_at_utc
            LIMIT 1
            """).ToListAsync(ct);
        return escaped is [var name]
            ? Invalid("boundary", $"Subfield “{name}” would fall outside this boundary. Keep the field around its subfields or reshape them first.")
            : null;
    }

    /// <summary>
    /// Fields of one owner must not overlap, except a subfield and its own parent. Shared edges are allowed: intersections up to 1 m² are treated as
    /// touching, which absorbs floating-point slivers from vertices snapped onto a neighbour's edge.
    /// </summary>
    private static async Task<IResult?> OverlapErrorsAsync(AppDbContext db, HttpContext context, Polygon boundary, Guid excludedFieldId, Guid? parentId, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        var parent = parentId ?? Guid.Empty;
        var overlapping = await db.Database.SqlQuery<string>($"""
            SELECT field.name AS "Value"
            FROM public.fields AS field
            JOIN public.farms AS farm ON farm.id = field.farm_id
            WHERE farm.owner_user_id = {userId} AND field.id <> {excludedFieldId} AND field.id <> {parent}
              AND field.parent_field_id IS DISTINCT FROM {excludedFieldId}
              AND ST_Intersects(field.boundary, {boundary})
              AND ST_Area(ST_Intersection(field.boundary, {boundary})::geography) > 1.0
            ORDER BY field.created_at_utc
            LIMIT 1
            """).ToListAsync(ct);
        return overlapping is [var name]
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["boundary"] = [$"This boundary overlaps “{name}”. Fields may share an edge but must not overlap."] })
            : null;
    }

    /// <summary>
    /// Deletes the field with everything that belongs to it: its subfields, the crop history of both, and (by the
    /// database's cascade) their saved strategies and strategy runs. Expenses booked to them stay, without a field.
    /// </summary>
    private static async Task<IResult> DeleteAsync(Guid fieldId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The owner lock of field writes and the row lock of planting writes (they lock the top-level field): a
        // concurrent subfield or crop added here waits, then fails on the missing parent instead of slipping in
        // between the deletes below.
        await LockOwnerAsync(db, context, ct);
        await db.Database.SqlQuery<Guid>(
            $"SELECT id AS \"Value\" FROM public.fields WHERE id = {fieldId} OR parent_field_id = {fieldId} FOR UPDATE").ToListAsync(ct);
        var ids = await OwnedResources.Fields(db, context)
            .Where(field => field.Id == fieldId || field.ParentFieldId == fieldId).Select(field => field.Id).ToListAsync(ct);
        if (!ids.Contains(fieldId)) { return TypedResults.NotFound(); }
        await db.FieldPlantings.Where(planting => ids.Contains(planting.FieldId)).ExecuteDeleteAsync(ct);
        await db.Fields.Where(field => field.ParentFieldId == fieldId).ExecuteDeleteAsync(ct);
        await db.Fields.Where(field => field.Id == fieldId).ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }
}
