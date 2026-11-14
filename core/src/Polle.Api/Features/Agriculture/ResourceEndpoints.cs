using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

/// <summary>What a farm can spend and operate: its budget and machinery (public.farm_fleet), read by the planning solver.</summary>
public sealed class ResourceEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Farm resources");
        group.MapGet("/equipment-categories", ListCategoriesAsync).Produces<EquipmentCategoryResponse[]>()
            .WithSummary("List machinery types (public.equipment_categories), sorted by id");
        group.MapPut("/farms/{farmId:guid}/budget", UpdateBudgetAsync).Produces<FarmResponse>()
            .WithSummary("Set or clear the farm's total budget in USD");
        group.MapGet("/labor-categories", ListLaborCategoriesAsync).Produces<LaborCategoryResponse[]>()
            .WithSummary("List labour types (public.labor_categories), sorted by id");
        group.MapGet("/fertilizers", ListFertilizersAsync).Produces<FertilizerResponse[]>()
            .WithSummary("List the fertilizer products a planting can use (public.fertilizers), by name");
        group.MapGet("/farms/{farmId:guid}/budget-allocations", ListAllocationsAsync).Produces<BudgetAllocationsResponse>()
            .WithSummary("Planned spending per category within the farm budget");
        group.MapPut("/farms/{farmId:guid}/budget-allocations/{category}", SetAllocationAsync).Produces<BudgetAllocationsResponse>()
            .WithSummary("Set or remove one category's planned amount; the farm's total budget is not changed")
            .WithDescription("Allocations are planning data. The solver keeps using budgetUsd as the farm total, and allocations may exceed it; the response reports the remainder.");
        group.MapGet("/resources/overview", OverviewAsync).Produces<ResourceOverviewResponse>()
            .WithSummary("Budgets, allocations, machinery and field area of every farm of the current user");
        group.MapGet("/farms/{farmId:guid}/fleet", ListFleetAsync).Produces<FleetItemResponse[]>()
            .WithSummary("List an owned farm's machinery, oldest first");
        group.MapPost("/farms/{farmId:guid}/fleet", CreateFleetItemAsync).Produces<FleetItemResponse>(201)
            .WithSummary("Add a machinery category/model; quantity and historical monthly limits are optional");
        group.MapPut("/fleet/{itemId:int}", UpdateFleetItemAsync).Produces<FleetItemResponse>().WithSummary("Replace an owned machinery line");
        group.MapPut("/fleet/{itemId:int}/details", UpdateFleetDetailsAsync).Produces<FleetItemResponse>()
            .WithSummary("Update category/model without changing stored quantity or monthly limits");
        group.MapDelete("/fleet/{itemId:int}", DeleteFleetItemAsync).Produces(204).WithSummary("Remove an owned machinery line");
    }

    private static async Task<IResult> ListCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.EquipmentCategories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(ct);
        return TypedResults.Ok(categories.Select(EquipmentCategoryResponse.From));
    }

    private static async Task<IResult> ListLaborCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.LaborCategories.AsNoTracking().OrderBy(category => category.Id).ToListAsync(ct);
        return TypedResults.Ok(categories.Select(category => new LaborCategoryResponse(category.Id, category.Name)));
    }

    private static async Task<IResult> ListAllocationsAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).AsNoTracking().Where(farm => farm.Id == farmId)
            .Select(farm => new { farm.BudgetUsd }).SingleOrDefaultAsync(ct);
        if (farm is null) { return TypedResults.NotFound(); }
        var allocations = await db.FarmBudgetAllocations.AsNoTracking().Where(item => item.FarmId == farmId).ToListAsync(ct);
        return TypedResults.Ok(BudgetAllocationsResponse.From(farmId, farm.BudgetUsd, allocations));
    }

    private static async Task<IResult> SetAllocationAsync(Guid farmId, string category, BudgetAllocationRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).AsNoTracking().Where(farm => farm.Id == farmId)
            .Select(farm => new { farm.BudgetUsd }).SingleOrDefaultAsync(ct);
        if (farm is null) { return TypedResults.NotFound(); }
        if (!FarmBudgetAllocation.Categories.Contains(category))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["category"] = ["Choose one of the listed budget categories."] });
        }
        var errors = AgricultureValidation.Allocation(request);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        if (request.AmountUsd is null or 0)
        {
            await db.FarmBudgetAllocations.Where(item => item.FarmId == farmId && item.Category == category).ExecuteDeleteAsync(ct);
        }
        else
        {
            // One statement, so two saves of the same category cannot race into a unique-key error.
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.farm_budget_allocations (farm_id, category, amount_usd, created_at_utc, updated_at_utc)
                VALUES ({farmId}, {category}, {request.AmountUsd.Value}, {now}, {now})
                ON CONFLICT (farm_id, category) DO UPDATE SET amount_usd = excluded.amount_usd, updated_at_utc = excluded.updated_at_utc
                """, ct);
        }
        var allocations = await db.FarmBudgetAllocations.AsNoTracking().Where(item => item.FarmId == farmId).ToListAsync(ct);
        return TypedResults.Ok(BudgetAllocationsResponse.From(farmId, farm.BudgetUsd, allocations));
    }

    private static async Task<IResult> OverviewAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farms = await OwnedResources.Farms(db, context).AsNoTracking().OrderBy(farm => farm.CreatedAtUtc).ThenBy(farm => farm.Id)
            .Select(farm => new { farm.Id, farm.Name, farm.Location, farm.BudgetUsd }).ToListAsync(ct);
        var userId = OwnedResources.UserId(context);
        var allocations = (await db.FarmBudgetAllocations.AsNoTracking().Where(item => item.Farm.OwnerUserId == userId).ToListAsync(ct))
            .ToLookup(item => item.FarmId);
        var fleet = (await OwnedResources.Fleet(db, context).AsNoTracking().OrderBy(item => item.Id).ToListAsync(ct)).ToLookup(item => item.FarmId);
        // Subfields lie inside their parent, so only top-level fields add to the farm's area.
        var land = await OwnedResources.Fields(db, context).Where(field => field.ParentFieldId == null)
            .GroupBy(field => field.FarmId)
            .Select(group => new { FarmId = group.Key, Count = group.Count(), Area = group.Sum(field => field.AreaHectares) })
            .ToDictionaryAsync(group => group.FarmId, ct);
        return TypedResults.Ok(new ResourceOverviewResponse(farms.Select(farm =>
        {
            var budget = BudgetAllocationsResponse.From(farm.Id, farm.BudgetUsd, allocations[farm.Id]);
            land.TryGetValue(farm.Id, out var fields);
            return new FarmResourceSummary(farm.Id, farm.Name, farm.Location, farm.BudgetUsd, budget.Allocations, budget.AllocatedUsd,
                budget.UnallocatedUsd, fields?.Count ?? 0, fields?.Area ?? 0, fleet[farm.Id].Select(FleetItemResponse.From).ToArray());
        }).ToArray(), FarmBudgetAllocation.Categories));
    }

    private static async Task<IResult> UpdateBudgetAsync(Guid farmId, BudgetRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var farm = await OwnedResources.Farms(db, context).SingleOrDefaultAsync(farm => farm.Id == farmId, ct);
        if (farm is null) { return TypedResults.NotFound(); }
        var errors = AgricultureValidation.Budget(request);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        farm.BudgetUsd = request.BudgetUsd;
        farm.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FarmResponse.From(farm));
    }

    private static async Task<IResult> ListFleetAsync(Guid farmId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var fleet = await OwnedResources.Fleet(db, context).AsNoTracking().Where(item => item.FarmId == farmId).OrderBy(item => item.Id).ToListAsync(ct);
        return TypedResults.Ok(fleet.Select(FleetItemResponse.From));
    }

    private static async Task<IResult> CreateFleetItemAsync(Guid farmId, FleetItemRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var errors = await FleetErrorsAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var item = new FarmEquipment { FarmId = farmId };
        Apply(item, request);
        db.FarmEquipment.Add(item);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/fleet/{item.Id}", FleetItemResponse.From(item));
    }

    private static async Task<IResult> UpdateFleetItemAsync(int itemId, FleetItemRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var item = await OwnedResources.Fleet(db, context).SingleOrDefaultAsync(item => item.Id == itemId, ct);
        if (item is null) { return TypedResults.NotFound(); }
        var errors = await FleetErrorsAsync(request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        Apply(item, request);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FleetItemResponse.From(item));
    }

    private static async Task<IResult> DeleteFleetItemAsync(int itemId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var deleted = await OwnedResources.Fleet(db, context).Where(item => item.Id == itemId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<IResult> UpdateFleetDetailsAsync(int itemId, FleetDetailsRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var item = await OwnedResources.Fleet(db, context).SingleOrDefaultAsync(item => item.Id == itemId, ct);
        if (item is null) { return TypedResults.NotFound(); }
        var errors = await FleetErrorsAsync(new FleetItemRequest(request.EquipmentCategoryId, CustomName: request.CustomName), db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        item.EquipmentCategoryId = request.EquipmentCategoryId!.Value;
        item.CustomName = AgricultureValidation.Optional(request.CustomName);
        item.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(FleetItemResponse.From(item));
    }

    private static async Task<Dictionary<string, string[]>> FleetErrorsAsync(FleetItemRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = AgricultureValidation.FleetItem(request);
        if (request.EquipmentCategoryId is { } categoryId && !await db.EquipmentCategories.AnyAsync(category => category.Id == categoryId, ct))
        {
            errors["equipmentCategoryId"] = ["Choose a machinery type from the list."];
        }
        return errors;
    }

    private static void Apply(FarmEquipment item, FleetItemRequest request)
    {
        item.EquipmentCategoryId = request.EquipmentCategoryId!.Value;
        item.CustomName = AgricultureValidation.Optional(request.CustomName);
        item.UnitsCount = request.UnitsCount;
        item.MonthlyHoursPerUnit = request.MonthlyHoursPerUnit;
    }

    private static async Task<IResult> ListFertilizersAsync(AppDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await FertilizerCatalog.ListAsync(db, null, ct));
}
