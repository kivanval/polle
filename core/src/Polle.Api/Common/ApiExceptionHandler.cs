using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Common;

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var databaseError = exception as PostgresException ?? exception.InnerException as PostgresException;
        if (databaseError is { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: "ex_field_plantings_period" })
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Growing periods overlap",
                    Detail = "Only one crop can grow on a field at a time. Close the existing period or choose non-overlapping dates. The harvesting day is included in the period.",
                    Extensions = { ["code"] = "planting_period_overlap" }
                }
            });
        }
        if (databaseError is { SqlState: PostgresErrorCodes.ExclusionViolation, ConstraintName: "ex_farm_operator_shifts_overlap" })
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Operator shifts overlap",
                    Detail = "Another shift already covers this time in the selected month and weekday.",
                    Extensions = { ["code"] = "operator_shift_overlap" }
                }
            });
        }
        logger.LogError("Request {TraceId} failed ({ExceptionType}, SQLSTATE {SqlState})",
            context.TraceIdentifier, exception.GetType().Name, databaseError?.SqlState);
        var problem = Describe(exception);
        context.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }

    /// <summary>Codes in the problem body so the client can tell a database outage from a fault in this API.</summary>
    public const string DatabaseUnavailableCode = "database_unavailable";
    public const string ServerErrorCode = "server_error";

    /// <summary>
    /// 503 only when the database could not be reached or was overloaded (Npgsql marks those transient: no connection,
    /// timeouts, too many connections, a server shutting down). Any other database error, such as a column the deployed
    /// code expects but a migration hasn't added yet, is a fault of this API and answers 500, not "try again".
    /// </summary>
    internal static ProblemDetails Describe(Exception exception)
    {
        if (exception is BadHttpRequestException badRequest)
            return new ProblemDetails { Status = badRequest.StatusCode, Title = "Invalid request", Detail = "Check the request format and size, then try again." };
        if (exception is DbUpdateConcurrencyException || IsReferenceViolation(exception))
            return new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Resource conflict",
                Detail = "Remove child records before deleting their parent. A referenced resource may also have changed. Refresh and try again." };
        if (Database(exception) is { IsTransient: true })
            return new ProblemDetails { Status = StatusCodes.Status503ServiceUnavailable, Title = "Database temporarily unavailable",
                Detail = "Our server couldn’t reach its database. Please try again shortly.", Extensions = { ["code"] = DatabaseUnavailableCode } };
        return new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Something went wrong",
            Detail = "Something went wrong on our server. Please try again shortly.", Extensions = { ["code"] = ServerErrorCode } };
    }

    private static bool IsReferenceViolation(Exception exception) =>
        Database(exception) is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation }
        && exception is PostgresException or DbUpdateException;

    /// <summary>The Npgsql error itself or the one EF Core wrapped (DbUpdateException, InvalidOperationException).</summary>
    private static NpgsqlException? Database(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is NpgsqlException npgsql) return npgsql;
        return null;
    }
}
