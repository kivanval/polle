using System.Net;
using System.Net.Sockets;
using System.Text;
using Polle.Api.Common;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Tests;

/// <summary>
/// A database outage, a fault in this API and an unreachable strategy solver each answer differently, so the client
/// never reports one as another (a solver or schema problem shown as "your workspace is unavailable").
/// </summary>
public sealed class ErrorAnswerTests
{
    private static PostgresException Postgres(string sqlState) => new("error", "ERROR", "ERROR", sqlState);

    [Fact]
    public void AColumnTheDatabaseLacksIsAServerFaultNotAnOutage()
    {
        // What production answered before migration PlantingInputsAndYearlyExpenses reached its database.
        var problem = ApiExceptionHandler.Describe(Postgres(PostgresErrorCodes.UndefinedColumn));

        Assert.Equal(500, problem.Status);
        Assert.Equal(ApiExceptionHandler.ServerErrorCode, problem.Extensions["code"]);
    }

    [Fact]
    public void AFailedSaveOnAMissingColumnIsAServerFaultToo()
    {
        var problem = ApiExceptionHandler.Describe(new DbUpdateException("save failed", Postgres(PostgresErrorCodes.UndefinedColumn)));

        Assert.Equal(500, problem.Status);
    }

    [Fact]
    public void AnUnreachableDatabaseIsAnOutage()
    {
        var problem = ApiExceptionHandler.Describe(new NpgsqlException("Failed to connect", new SocketException((int)SocketError.ConnectionRefused)));

        Assert.Equal(503, problem.Status);
        Assert.Equal(ApiExceptionHandler.DatabaseUnavailableCode, problem.Extensions["code"]);
    }

    [Fact]
    public void AnOverloadedDatabaseIsAnOutage()
    {
        var problem = ApiExceptionHandler.Describe(new DbUpdateException("save failed", Postgres(PostgresErrorCodes.TooManyConnections)));

        Assert.Equal(503, problem.Status);
    }

    [Fact]
    public void AReferencedRecordIsStillAConflict()
    {
        var problem = ApiExceptionHandler.Describe(new DbUpdateException("save failed", Postgres(PostgresErrorCodes.ForeignKeyViolation)));

        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public void AnyOtherExceptionIsAServerFault()
    {
        var problem = ApiExceptionHandler.Describe(new InvalidOperationException("bug"));

        Assert.Equal(500, problem.Status);
        Assert.DoesNotContain("workspace", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnconfiguredSolverAnswersSolverUnavailable()
    {
        var result = Assert.IsType<ProblemHttpResult>(StrategyRunEndpoints.SolverUnavailable());

        Assert.Equal(503, result.StatusCode);
        Assert.Equal(StrategyRunEndpoints.SolverUnavailableCode, result.ProblemDetails.Extensions["code"]);
        Assert.Contains("solver", result.ProblemDetails.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("<html>down</html>", Encoding.UTF8, "text/html") });
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ASolverGatewayErrorLeavesTheCardWaitingForTheSolverInsteadOfFailingTheRequest(HttpStatusCode status)
    {
        var solver = new FelderClient(new HttpClient(new Handler(status)) { BaseAddress = new Uri("https://solver.test/") });
        var job = new StrategyJob { Key = "control", Kind = StrategyJobs.Optimization, State = StrategyJobs.Processing,
            SubmittedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        var run = new StrategyRun { FieldId = Guid.NewGuid(), StartMonth = "2026-12", EndMonth = "2030-12" };

        await StrategyRunEndpoints.SubmitAsync(job, run, null, solver, new SolverMaterials(null), CancellationToken.None);

        Assert.Equal(StrategyJobs.Processing, job.State);
        Assert.Null(job.TaskId);
        Assert.Equal($"The solver answered {(int)status}.", job.Error);
    }
}
