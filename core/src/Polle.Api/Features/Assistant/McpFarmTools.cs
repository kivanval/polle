using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polle.Api.Features.Solver;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Polle.Api.Features.Assistant;

// Every tool only reads the session user's data (no SaveChanges, no external calls), hence ReadOnly and closed-world.
// The annotations are client hints; authorization is enforced by the endpoint and the owner-filtered queries.
[McpServerToolType]
public sealed class McpFarmTools(FarmAssistantTools farms, SolverRequestBuilder solver, IHttpContextAccessor contextAccessor)
{
    // MCP's default options omit null properties. Keep them so tool output has the same keys as the HTTP solver
    // draft, where an explicit null marks an unavailable input.
    public static readonly JsonSerializerOptions SerializerOptions =
        new(McpJsonUtilities.DefaultOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private const string NotFound = "Farm not found or not accessible to the current user.";

    private System.Security.Claims.ClaimsPrincipal User =>
        contextAccessor.HttpContext?.User ?? throw new UnauthorizedAccessException("A valid session is required.");

    [McpServerTool(Name = "list_my_farms", ReadOnly = true, OpenWorld = false)]
    [Description("List the authenticated farmer's own farms, locations and recorded USD budgets. No arguments.")]
    public Task<AssistantFarm[]> ListMyFarms(CancellationToken ct) =>
        farms.ListMyFarmsAsync(User, ct);

    [McpServerTool(Name = "get_farm_summary", ReadOnly = true, OpenWorld = false)]
    [Description("Read one authenticated farmer's farm and fields. Ambiguous references return choices.")]
    public Task<ToolResolution<AssistantFarmSummary>> GetFarmSummary(
        [Description("UUID of an owned farm, or null to resolve by name.")] Guid? farmId,
        [Description("Exact farm name, or null when farmId is given.")] string? farmName, CancellationToken ct) =>
        farms.GetFarmSummaryAsync(User, farmId, farmName, null, ct);

    [McpServerTool(Name = "get_field_planting_history", ReadOnly = true, OpenWorld = false)]
    [Description("Read an authenticated farmer's field planting history. Ambiguous references return choices.")]
    public Task<ToolResolution<AssistantFieldHistory>> GetFieldPlantingHistory(
        [Description("UUID of an owned field, or null to resolve by name.")] Guid? fieldId,
        [Description("Exact field name, or null when fieldId is given.")] string? fieldName,
        [Description("Season year 1900–2100, or null for all years.")] int? year, CancellationToken ct) =>
        farms.GetFieldPlantingHistoryAsync(User, fieldId, fieldName, null, null, year, ct);

    [McpServerTool(Name = "prepare_solver_scenario", ReadOnly = true, OpenWorld = false)]
    [Description("Prepare a read-only draft of Ivan's provisional solver JSON for an owned farm. Requires farm ID, start month YYYY-MM and planning horizon in years. Returns ready/incomplete with missing inputs; does not run optimization.")]
    public async Task<SolverPreparationResult> PrepareSolverScenario(
        [Description("UUID of an owned farm, for example from list_my_farms.")] Guid farmId,
        [Description("First planning month as YYYY-MM with month 01–12, for example 2027-01. Seasons and quarters are not accepted.")]
        [RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] string startMonth,
        [Description("Whole planning years from 1 to 20. Months and fractional years are not accepted.")]
        [Range(1, 20)] int planningHorizonYears, CancellationToken ct)
    {
        try
        {
            return await solver.PrepareAsync(User, farmId, startMonth, planningHorizonYears, ct)
                ?? throw new McpException(NotFound);
        }
        // Only the builder's own argument checks are reported; their messages contain no internal details.
        catch (ArgumentException ex) when (ex.ParamName is nameof(farmId) or nameof(startMonth) or nameof(planningHorizonYears))
        {
            throw new McpException(ex.Message);
        }
    }

    // Reports argument problems against the tool's advertised input schema before the SDK binds them, because the
    // SDK turns binding failures into a generic error. Other exceptions keep the SDK's sanitized generic result.
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> ReportInputErrors(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (request, ct) =>
    {
        if (request.MatchedPrimitive is McpServerTool tool &&
            ArgumentProblem(tool.ProtocolTool.InputSchema, request.Params?.Arguments) is { } problem)
            throw new McpException(problem);
        try { return await next(request, ct); }
        catch (JsonException) { throw new McpException("The arguments do not match the tool's input schema."); }
        catch (UnauthorizedAccessException) { throw new McpException("A valid session is required."); }
    };

    private static string? ArgumentProblem(JsonElement schema, IDictionary<string, JsonElement>? arguments)
    {
        if (schema.TryGetProperty("required", out var required))
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()!))
                if (arguments is null || !arguments.ContainsKey(name)) return $"Missing required argument '{name}'.";
        if (arguments is null || !schema.TryGetProperty("properties", out var properties)) return null;

        foreach (var (name, value) in arguments)
        {
            // Unknown arguments are ignored by the SDK and never reach the tool.
            if (!properties.TryGetProperty(name, out var property) || !property.TryGetProperty("type", out var type)) continue;
            var types = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Select(item => item.GetString()).ToArray() : [type.GetString()];
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (!types.Contains("null")) return $"Argument '{name}' must not be null.";
                continue;
            }
            if (types.Contains("integer") && !(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)) &&
                !(value.ValueKind == JsonValueKind.String &&
                    int.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)))
                return $"Argument '{name}' must be a whole number.";
            if (types.Contains("string"))
            {
                if (value.ValueKind != JsonValueKind.String) return $"Argument '{name}' must be a string.";
                if (property.TryGetProperty("format", out var format) && format.GetString() == "uuid" &&
                    !Guid.TryParseExact(value.GetString(), "D", out _))
                    return $"Argument '{name}' must be a UUID such as 00000000-0000-0000-0000-000000000000.";
            }
        }
        return null;
    }
}
