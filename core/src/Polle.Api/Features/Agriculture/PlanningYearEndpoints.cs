using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Polle.Api.Common;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Agriculture;

public sealed record PlanningYearRequest([property: Required, Description("Season year to plan ahead, from this year up to 20 years ahead.")] int? Year);

/// <summary>
/// Season years the user added to plan ahead on the dashboard. Years that already hold plantings need no row here:
/// the client lists them from the plantings. A stored year never changes or creates any planting.
/// </summary>
public sealed class PlanningYearEndpoints : IEndpoint
{
    private const int MaxYearsAhead = 20;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = OwnedResources.ProtectedGroup(app, "Planning years");
        group.MapGet("/planning-years", ListAsync).Produces<int[]>().WithSummary("List the season years the current user added for planning, oldest first");
        group.MapPost("/planning-years", AddAsync).Produces<int[]>(201)
            .WithSummary("Add a future season year to plan; returns 409 when the year is already added");
        group.MapDelete("/planning-years/{year:int}", RemoveAsync).Produces(204)
            .WithSummary("Remove an added season year; plantings in that year are not touched");
    }

    private static async Task<IResult> ListAsync(AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        return TypedResults.Ok(await Years(db, userId, ct));
    }

    private static async Task<IResult> AddAsync(PlanningYearRequest request, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var thisYear = DateTime.UtcNow.Year;
        if (request.Year is not { } year || year < thisYear || year > thisYear + MaxYearsAhead)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { ["year"] = [$"Choose a year from {thisYear} to {thisYear + MaxYearsAhead}."] });
        }
        var userId = OwnedResources.UserId(context);
        // The (user, year) key rejects duplicates atomically, also for two concurrent requests.
        var added = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.planning_years (user_id, year, created_at_utc) VALUES ({userId}, {year}, {DateTime.UtcNow})
            ON CONFLICT (user_id, year) DO NOTHING
            """, ct);
        if (added == 0)
        {
            return TypedResults.Problem(statusCode: 409, title: "Year already added", detail: $"{year} is already in your plan.");
        }
        return TypedResults.Created("/api/planning-years", await Years(db, userId, ct));
    }

    private static async Task<IResult> RemoveAsync(int year, AppDbContext db, HttpContext context, CancellationToken ct)
    {
        var userId = OwnedResources.UserId(context);
        var removed = await db.PlanningYears.Where(item => item.UserId == userId && item.Year == year).ExecuteDeleteAsync(ct);
        return removed == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static Task<List<int>> Years(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.PlanningYears.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.Year).Select(item => item.Year).ToListAsync(ct);
}
