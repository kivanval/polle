using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Polle.Api.Common;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed record ExpenseRequest(
    [property: Required, Description("Day the money was spent or billed, YYYY-MM-DD; with allYear, any day of that year.")] DateOnly? SpentOn,
    [property: Required, Description("Budget category key: seeds, fertilizers, crop_protection, fuel, machinery, labor, irrigation, other.")] string? Category,
    [property: Required, MaxLength(200), Description("What was bought or paid, e.g. Diesel for spring sowing.")] string? Description,
    [property: Description("Total in USD with cents. Optional when quantity and unit price are both given: the API then stores quantity × unit price rounded to cents.")] decimal? AmountUsd = null,
    [property: Description("Amount bought, above 0, up to 3 decimals.")] decimal? Quantity = null,
    [property: MaxLength(20), Description("Unit of the quantity, e.g. l, kg, t, h.")] string? Unit = null,
    [property: Description("USD per unit, up to 4 decimals.")] decimal? UnitPriceUsd = null,
    [property: MaxLength(150)] string? Supplier = null,
    [property: MaxLength(60), Description("Invoice, receipt or payslip number.")] string? DocumentNumber = null,
    [property: Description("False for a bill not paid yet. Defaults to true.")] bool? IsPaid = null,
    [property: Description("Field of the same farm the cost belongs to.")] Guid? FieldId = null,
    [property: Description("Worker of the same farm the payment went to.")] int? WorkerId = null,
    [property: Description("Machinery line of the same farm the cost belongs to.")] int? EquipmentId = null,
    [property: MaxLength(2000)] string? Notes = null,
    [property: Description("True for a cost of the whole year of spentOn (a lease, insurance); it is stored on January 1.")] bool AllYear = false);

public sealed record ExpenseResponse(int Id, Guid FarmId, DateOnly SpentOn, string Category, string Description, decimal? Quantity, string? Unit,
    decimal? UnitPriceUsd, decimal AmountUsd, string? Supplier, string? DocumentNumber, bool IsPaid, Guid? FieldId, int? WorkerId, int? EquipmentId,
    string? Notes, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, bool AllYear = false)
{
    public static ExpenseResponse From(FarmExpense expense) => new(expense.Id, expense.FarmId, expense.SpentOn, expense.Category, expense.Description,
        expense.Quantity, expense.Unit, expense.UnitPriceUsd, expense.AmountUsd, expense.Supplier, expense.DocumentNumber, expense.IsPaid,
        expense.FieldId, expense.WorkerId, expense.EquipmentId, expense.Notes, expense.CreatedAtUtc, expense.UpdatedAtUtc, expense.CoversYear);
}

/// <summary>The ledger of what each farm actually spent, compared on the resources page with the planned budget split.</summary>
public sealed class ExpenseEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Expenses");
        group.MapGet("/expenses", ListAsync).Produces<ExpenseResponse[]>()
            .WithSummary("List the current user's expenses, newest first, optionally for one farm and an inclusive date range");
        group.MapPost("/farms/{farmId:guid}/expenses", CreateAsync).Produces<ExpenseResponse>(201).WithSummary("Record an expense of an owned farm");
        group.MapPut("/expenses/{expenseId:int}", UpdateAsync).Produces<ExpenseResponse>().WithSummary("Replace an expense; its farm does not change");
        group.MapDelete("/expenses/{expenseId:int}", DeleteAsync).Produces(204).WithSummary("Remove an expense");
    }

    private static async Task<IResult> ListAsync(Guid? farmId, DateOnly? from, DateOnly? to, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var expenses = await OwnedResources.Expenses(db, context).AsNoTracking()
            .Where(expense => (farmId == null || expense.FarmId == farmId) && (from == null || expense.SpentOn >= from) && (to == null || expense.SpentOn <= to))
            .OrderByDescending(expense => expense.SpentOn).ThenByDescending(expense => expense.Id).ToListAsync(ct);
        return TypedResults.Ok(expenses.Select(ExpenseResponse.From));
    }

    private static async Task<IResult> CreateAsync(Guid farmId, ExpenseRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        if (!await OwnedResources.Farms(db, context).AnyAsync(farm => farm.Id == farmId, ct)) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(farmId, request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        var expense = new FarmExpense { FarmId = farmId, Category = "", Description = "" };
        Apply(expense, request);
        db.FarmExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/expenses/{expense.Id}", ExpenseResponse.From(expense));
    }

    private static async Task<IResult> UpdateAsync(int expenseId, ExpenseRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var expense = await OwnedResources.Expenses(db, context).SingleOrDefaultAsync(expense => expense.Id == expenseId, ct);
        if (expense is null) { return TypedResults.NotFound(); }
        var errors = await ValidateAsync(expense.FarmId, request, db, ct);
        if (errors.Count > 0) { return TypedResults.ValidationProblem(errors); }
        Apply(expense, request);
        expense.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ExpenseResponse.From(expense));
    }

    private static async Task<IResult> DeleteAsync(int expenseId, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var deleted = await OwnedResources.Expenses(db, context).Where(expense => expense.Id == expenseId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private const decimal Limit = 1_000_000_000_000m;

    private static async Task<Dictionary<string, string[]>> ValidateAsync(Guid farmId, ExpenseRequest request, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.SpentOn is not { } day || day.Year < 1900 || day.Year > 2200) { errors["spentOn"] = ["Enter the date of the expense, between 1900 and 2200."]; }
        if (request.Category is null || !FarmBudgetAllocation.Categories.Contains(request.Category)) { errors["category"] = ["Choose one of the listed budget categories."]; }
        if (string.IsNullOrWhiteSpace(request.Description)) { errors["description"] = ["Describe what was bought or paid."]; }
        else if (request.Description.Trim().Length > 200) { errors["description"] = ["Maximum length is 200 characters."]; }
        if (request.Quantity is { } quantity && (quantity <= 0 || quantity >= 100_000_000_000m || decimal.Round(quantity, 3) != quantity))
        { errors["quantity"] = ["Quantity must be above 0, below 100 billion, with 3 decimals at most."]; }
        if (request.UnitPriceUsd is { } price && (price < 0 || price >= 10_000_000_000m || decimal.Round(price, 4) != price))
        { errors["unitPriceUsd"] = ["Unit price must be 0 or more, below 10 billion USD, with 4 decimals at most."]; }
        if (request.AmountUsd is { } amount && (amount < 0 || amount >= Limit || decimal.Round(amount, 2) != amount))
        { errors["amountUsd"] = ["Amount must be 0 or more, below 1 trillion USD, with cents at most."]; }
        if (request.AmountUsd is null && (request.Quantity is null || request.UnitPriceUsd is null))
        { errors["amountUsd"] = ["Enter the amount, or both a quantity and a unit price."]; }
        if (Computed(request) is >= Limit) { errors["amountUsd"] = ["Quantity × unit price must stay below 1 trillion USD."]; }
        if (request.Unit?.Trim().Length > 20) { errors["unit"] = ["Maximum length is 20 characters."]; }
        if (request.Supplier?.Trim().Length > 150) { errors["supplier"] = ["Maximum length is 150 characters."]; }
        if (request.DocumentNumber?.Trim().Length > 60) { errors["documentNumber"] = ["Maximum length is 60 characters."]; }
        if (request.Notes?.Length > 2000) { errors["notes"] = ["Maximum length is 2000 characters."]; }
        if (request.FieldId is { } fieldId && !await db.Fields.AnyAsync(field => field.Id == fieldId && field.FarmId == farmId, ct))
        { errors["fieldId"] = ["Choose a field of this farm."]; }
        if (request.WorkerId is { } workerId && !await db.FarmWorkers.AnyAsync(worker => worker.Id == workerId && worker.FarmId == farmId, ct))
        { errors["workerId"] = ["Choose a worker of this farm."]; }
        if (request.EquipmentId is { } equipmentId && !await db.FarmEquipment.AnyAsync(item => item.Id == equipmentId && item.FarmId == farmId, ct))
        { errors["equipmentId"] = ["Choose machinery of this farm."]; }
        return errors;
    }

    private static decimal? Computed(ExpenseRequest request) =>
        request.Quantity is { } quantity && request.UnitPriceUsd is { } price ? decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero) : null;

    private static void Apply(FarmExpense expense, ExpenseRequest request)
    {
        expense.CoversYear = request.AllYear;
        expense.SpentOn = request.AllYear ? new DateOnly(request.SpentOn!.Value.Year, 1, 1) : request.SpentOn!.Value;
        expense.Category = request.Category!;
        expense.Description = request.Description!.Trim();
        expense.Quantity = request.Quantity;
        expense.Unit = AgricultureValidation.Optional(request.Unit);
        expense.UnitPriceUsd = request.UnitPriceUsd;
        expense.AmountUsd = Computed(request) ?? request.AmountUsd!.Value;
        expense.Supplier = AgricultureValidation.Optional(request.Supplier);
        expense.DocumentNumber = AgricultureValidation.Optional(request.DocumentNumber);
        expense.IsPaid = request.IsPaid ?? true;
        expense.FieldId = request.FieldId;
        expense.WorkerId = request.WorkerId;
        expense.EquipmentId = request.EquipmentId;
        expense.Notes = AgricultureValidation.Optional(request.Notes);
    }
}
