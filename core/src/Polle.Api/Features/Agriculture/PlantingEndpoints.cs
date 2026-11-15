using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed class PlantingEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Plantings");
        group.MapGet("/farms/{farmId:guid}/plantings", ListFarmAsync).Produces<PlantingResponse[]>()
            .WithSummary("List planting history of every field in an owned farm, grouped by field, oldest planting date first");
        group.MapGet("/fields/{fieldId:guid}/plantings", ListAsync).Produces<PlantingResponse[]>().WithSummary("List an owned field's planting history, oldest planting date first")
            .WithDescription("status is planned, current or historical, derived from inclusive dates using today's UTC date. seasonYear is a grouping label, not a uniqueness key. History can be inserted or edited at any non-overlapping date.");
        group.MapPost("/fields/{fieldId:guid}/plantings", CreateAsync).Produces<PlantingResponse>(201)
            .WithSummary("Add a non-overlapping growing period")
            .WithDescription("plantingDate is required. Both dates are inclusive; null harvestingDate reserves the field indefinitely. Overlapping periods return 409 (planting_period_overlap), including concurrent writes. Sequential periods in the same year are allowed. cropProfileId must be a profile id from GET /api/crops.");
        group.MapGet("/plantings/{plantingId:guid}", GetAsync).Produces<PlantingResponse>().WithSummary("Get an owned planting record");
        group.MapPut("/plantings/{plantingId:guid}", UpdateAsync).Produces<PlantingResponse>().WithSummary("Replace an owned planting record; overlapping periods return 409");
        group.MapDelete("/plantings/{plantingId:guid}", DeleteAsync).Produces(204).WithSummary("Delete an owned planting record");
    }

    private static async Task<IResult> ListFarmAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var plantings = await OwnedResources.Plantings(db, context).AsNoTracking().Include(planting => planting.CropProfile).Where(planting => planting.Field.FarmId == farmId)
            .OrderBy(planting => planting.FieldId).ThenBy(planting => planting.PlantingDate).ThenBy(planting => planting.Id).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return TypedResults.Ok(plantings.Select(planting => PlantingResponse.From(planting, today)));
    }

    private static async Task<IResult> ListAsync(Guid fieldId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Fields(db, context).AnyAsync(field => field.Id == fieldId, ct)) { return TypedResults.NotFound(); }
        var plantings = await OwnedResources.Plantings(db, context).AsNoTracking().Include(planting => planting.CropProfile).Where(planting => planting.FieldId == fieldId)
            .OrderBy(planting => planting.PlantingDate)
            .ThenBy(planting => planting.Id).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return TypedResults.Ok(plantings.Select(planting => PlantingResponse.From(planting, today)));
    }

    private static async Task<IResult> GetAsync(Guid plantingId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var planting = await OwnedResources.Plantings(db, context).AsNoTracking().Include(planting => planting.CropProfile).SingleOrDefaultAsync(planting => planting.Id == plantingId, ct);
        return planting is null ? TypedResults.NotFound() : TypedResults.Ok(PlantingResponse.From(planting, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private static async Task<IResult> CreateAsync(Guid fieldId, PlantingRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Fields(db, context).AnyAsync(field => field.Id == fieldId, ct)) { return TypedResults.NotFound(); }
        var errors = await PlantingErrorsAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockFieldFamilyAsync(db, fieldId, ct);
        if (await RelatedOverlapAsync(db, fieldId, null, request, ct) is { } related) { return related; }
        var planting = new FieldPlanting { FieldId = fieldId, CropProfileId = request.CropProfileId!.Value, SeasonYear = request.SeasonYear,
            PlantingDate = request.PlantingDate!.Value, HarvestingDate = request.HarvestingDate, Notes = AgricultureValidation.Optional(request.Notes) };
        ApplyInputs(planting, request);
        db.FieldPlantings.Add(planting);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await db.Entry(planting).Reference(saved => saved.CropProfile).LoadAsync(ct);
        return TypedResults.Created($"/api/plantings/{planting.Id}", PlantingResponse.From(planting, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private static async Task<IResult> UpdateAsync(Guid plantingId, PlantingRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var planting = await OwnedResources.Plantings(db, context).SingleOrDefaultAsync(planting => planting.Id == plantingId, ct);
        if (planting is null) { return TypedResults.NotFound(); }
        var errors = await PlantingErrorsAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockFieldFamilyAsync(db, planting.FieldId, ct);
        if (await RelatedOverlapAsync(db, planting.FieldId, planting.Id, request, ct) is { } related) { return related; }
        planting.CropProfileId = request.CropProfileId!.Value;
        planting.SeasonYear = request.SeasonYear;
        planting.PlantingDate = request.PlantingDate!.Value;
        planting.HarvestingDate = request.HarvestingDate;
        planting.Notes = AgricultureValidation.Optional(request.Notes);
        ApplyInputs(planting, request);
        planting.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await db.Entry(planting).Reference(saved => saved.CropProfile).LoadAsync(ct);
        return TypedResults.Ok(PlantingResponse.From(planting, DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    /// <summary>
    /// The farmer's water and fertilizer for the season; no irrigation is stored as rain-fed (null). A target level is
    /// kept only with the input it belongs to.
    /// </summary>
    internal static void ApplyInputs(FieldPlanting planting, PlantingRequest request)
    {
        planting.IrrigationMm = request.IrrigationMm is > 0 ? request.IrrigationMm : null;
        planting.IrrigationTargetPercent = planting.IrrigationMm is null ? null : request.IrrigationTargetPercent;
        planting.FertilizerMaterialId = request.FertilizerMaterialId;
        planting.FertilizerRateKgHa = request.FertilizerMaterialId is null ? null : request.FertilizerRateKgHa;
        planting.FertilizerTargetPercent = request.FertilizerMaterialId is null ? null : request.FertilizerTargetPercent;
    }

    /// <summary>Shape and date checks, then the crop must exist in the catalog. Period overlaps are left to the exclusion constraint.</summary>
    private static async Task<Dictionary<string, string[]>> PlantingErrorsAsync(PlantingRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = AgricultureValidation.Planting(request);
        if (!errors.ContainsKey("cropProfileId") && !await db.CropProfiles.AnyAsync(profile => profile.Id == request.CropProfileId!.Value, ct))
        {
            errors["cropProfileId"] = ["Choose a crop from the catalog."];
        }
        if (request.FertilizerMaterialId is { } fertilizer && !errors.ContainsKey("fertilizerMaterialId")
            && (await FertilizerCatalog.ListAsync(db, [fertilizer], ct)).Length == 0)
        {
            errors["fertilizerMaterialId"] = ["Choose a fertilizer from the list."];
        }
        return errors;
    }

    /// <summary>
    /// Serializes planting writes within one field and its subfields by locking the top-level field's row, so two
    /// concurrent requests (one on the parent, one on a subfield) cannot both pass <see cref="RelatedOverlapAsync"/>.
    /// The following check then reads a fresh READ COMMITTED snapshot. Unrelated fields are never blocked.
    /// </summary>
    internal static async Task LockFieldFamilyAsync(AppDbContext db, Guid fieldId, CancellationToken ct) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT root.id AS "Value"
            FROM public.fields AS root
            WHERE root.id = (SELECT coalesce(field.parent_field_id, field.id) FROM public.fields AS field WHERE field.id = {fieldId})
            FOR UPDATE
            """).ToListAsync(ct);

    /// <summary>
    /// A subfield is part of its parent's land, so a parent and its subfields cannot grow crops in overlapping periods.
    /// Periods on one field are guarded by the exclusion constraint; this covers the parent/subfield pairs.
    /// </summary>
    internal static async Task<IResult?> RelatedOverlapAsync(AppDbContext db, Guid fieldId, Guid? plantingId, PlantingRequest request, CancellationToken ct)
    {
        var parentId = await db.Fields.Where(field => field.Id == fieldId).Select(field => field.ParentFieldId).SingleAsync(ct);
        var start = request.PlantingDate!.Value;
        var end = request.HarvestingDate;
        var conflict = await db.FieldPlantings.AsNoTracking()
            .Where(other => other.Id != plantingId
                && (parentId != null ? other.FieldId == parentId : other.Field.ParentFieldId == fieldId)
                && (other.HarvestingDate == null || other.HarvestingDate >= start)
                && (end == null || other.PlantingDate <= end))
            .Select(other => other.Field.Name).FirstOrDefaultAsync(ct);
        return conflict is null ? null : TypedResults.Problem(statusCode: 409, title: "Growing periods overlap",
            detail: parentId is null
                ? $"Subfield “{conflict}” already has a crop in this period. A field and its subfields share the same land, so their growing periods cannot overlap."
                : $"The parent field “{conflict}” already has a crop in this period. A field and its subfields share the same land, so their growing periods cannot overlap.",
            extensions: new Dictionary<string, object?> { ["code"] = "planting_period_overlap" });
    }

    private static async Task<IResult> DeleteAsync(Guid plantingId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var deleted = await OwnedResources.Plantings(db, context).Where(planting => planting.Id == plantingId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }
}
