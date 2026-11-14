using Polle.Api.Common;
using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Weather;

namespace Polle.Api.Features.Solver;

public sealed class SolverScenarioEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        OwnedResources.ProtectedGroup(app, "Solver preparation")
            .MapGet("/farms/{farmId:guid}/solver-draft", GetAsync)
            .RequireRateLimiting("weather")
            .Produces<SolverPreparationResult>()
            .WithSummary("Prepare an owned farm's solver draft with an optional explicit climate assumption");
    }

    private static async Task<IResult> GetAsync(Guid farmId, string? startMonth,
        int? planningHorizonYears, string? climateScenarioType, int? referenceStartYear,
        SolverRequestBuilder solver, HttpContext context, CancellationToken ct)
    {
        if (startMonth is null || planningHorizonYears is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["period"] = ["Provide startMonth as YYYY-MM and planningHorizonYears from 1 to 20."]
            });

        SolverClimateSelection? climateSelection = null;
        if (climateScenarioType is not null || referenceStartYear is not null)
        {
            if (climateScenarioType != ClimateScenarioService.ScenarioType || referenceStartYear is null)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["climate"] = [$"Use climateScenarioType={ClimateScenarioService.ScenarioType} with an explicit referenceStartYear, or omit both parameters."]
                });
            climateSelection = new(SolverClimateScenarioType.HistoricalTenYearMonthlyBaseline,
                referenceStartYear.Value);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(27));
        try
        {
            var result = await solver.PrepareAsync(context.User, farmId, startMonth,
                planningHorizonYears.Value, budget.Token, climateSelection);
            return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ex.ParamName ?? "request"] = [ex.Message]
            });
        }
        catch (WeatherUpstreamException ex)
        {
            return TypedResults.Problem(statusCode: 503, title: "Historical weather unavailable", detail: ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Problem(statusCode: 504, title: "Solver draft preparation timed out",
                detail: "Solver draft preparation exceeded the 27-second request budget.");
        }
    }
}
