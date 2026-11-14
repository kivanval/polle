using System.Security.Claims;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;

namespace Polle.Api.Features.Agriculture;

internal static class OwnedResources
{
    public static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static IQueryable<Farm> Farms(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.Farms.Where(farm => farm.OwnerUserId == userId);
    }
    public static IQueryable<Field> Fields(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.Fields.Where(field => field.Farm.OwnerUserId == userId);
    }
    public static IQueryable<FieldPlanting> Plantings(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.FieldPlantings.Where(planting => planting.Field.Farm.OwnerUserId == userId);
    }
    public static IQueryable<FarmEquipment> Fleet(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.FarmEquipment.Where(item => item.Farm.OwnerUserId == userId);
    }
    public static IQueryable<FarmOperatorShift> OperatorShifts(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.FarmOperatorShifts.Where(shift => shift.Farm.OwnerUserId == userId);
    }
    public static IQueryable<FarmWorker> Workers(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.FarmWorkers.Where(worker => worker.Farm.OwnerUserId == userId);
    }
    public static IQueryable<FarmExpense> Expenses(AppDbContext db, HttpContext context)
    {
        var userId = UserId(context);
        return db.FarmExpenses.Where(expense => expense.Farm.OwnerUserId == userId);
    }
    public static RouteGroupBuilder ProtectedGroup(IEndpointRouteBuilder app, string tag) =>
        app.MapGroup("").WithTags(tag).RequireAuthorization()
            .ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503).ProducesValidationProblem();
}
