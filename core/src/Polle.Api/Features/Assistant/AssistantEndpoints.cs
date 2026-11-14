using System.Text;
using Polle.Api.Common;

namespace Polle.Api.Features.Assistant;

// Mirrored in frontend AssistantChat.tsx. The worst case (2000 three-byte characters plus the history
// budget and JSON framing) stays below Kestrel's global 16 KB request body limit.
public static class AssistantLimits
{
    public const int MaxMessageChars = 2000;
    public const int MaxHistoryItems = 6;
    public const int MaxHistoryItemChars = 2000;
    public const int MaxHistoryBytes = 8000;
    // At most two sequential model calls per chat turn share the overall budget; the browser waits 110 s.
    public static readonly TimeSpan ModelRequestTimeout = TimeSpan.FromSeconds(75);
    public static readonly TimeSpan ExecutionBudget = TimeSpan.FromSeconds(100);
}

public sealed record AssistantHistoryItem(string Role, string Content);
public sealed record AssistantChatRequest(string? Message, AssistantHistoryItem[]? History,
    AssistantPageContext? PageContext, AssistantConversationContext? ConversationContext);
/// <param name="StrategyRunId">A strategy run the answer read or started for <paramref name="FieldId"/>; the browser opens it on that field's plan.</param>
public sealed record AssistantChatResponse(string Message, AssistantConversationContext? ConversationContext,
    string? ScenarioJson, Guid? StrategyRunId = null, Guid? FieldId = null);

public sealed class AssistantEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGroup("/assistant").WithTags("Assistant").RequireAuthorization()
            .MapPost("/chat", ChatAsync).RequireRateLimiting("assistant").Produces<AssistantChatResponse>()
            .ProducesProblem(502).ProducesProblem(503).ProducesProblem(504).ProducesValidationProblem();
    }

    private static async Task<IResult> ChatAsync(AssistantChatRequest request, HttpContext context,
        OllamaAssistant assistant, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > AssistantLimits.MaxMessageChars ||
            request.History is { Length: > AssistantLimits.MaxHistoryItems } ||
            request.PageContext?.PageType is { Length: > 40 } ||
            request.ConversationContext is { } previous && !ValidContext(previous) ||
            request.History?.Any(item => item.Role is not ("user" or "assistant") ||
                string.IsNullOrWhiteSpace(item.Content) || item.Content.Length > AssistantLimits.MaxHistoryItemChars) == true ||
            request.History?.Sum(item => Encoding.UTF8.GetByteCount(item.Content ?? "")) > AssistantLimits.MaxHistoryBytes)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["message"] = ["Send a message of 1–2000 characters and up to six valid history messages totalling at most 8000 UTF-8 bytes."]
            });

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(AssistantLimits.ExecutionBudget);
        try
        {
            var answer = await assistant.ChatAsync(request.Message.Trim(), request.History ?? [],
                request.PageContext, request.ConversationContext, context.User, budget.Token);
            return TypedResults.Ok(new AssistantChatResponse(answer.Message, answer.Context, answer.ScenarioJson, answer.StrategyRunId, answer.FieldId));
        }
        catch (HttpRequestException)
        {
            return TypedResults.Problem(statusCode: 503, title: "Assistant unavailable", detail: "The assistant model is not reachable. Try again shortly.");
        }
        // Covers both the per-call HttpClient timeout and the overall budget; a client disconnect propagates.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return TypedResults.Problem(statusCode: 504, title: "Assistant timed out", detail: "The model took too long. Try again.");
        }
        catch (OllamaUnavailableException ex)
        {
            return TypedResults.Problem(statusCode: 503, title: "Assistant unavailable", detail: ex.Message);
        }
        catch (AssistantGenerationException ex)
        {
            return TypedResults.Problem(statusCode: 502, title: "Assistant answer incomplete", detail: ex.Message);
        }
    }

    /// <summary>
    /// A context the browser sends back must be one this API produced: a known tool with a farm; a scenario or strategy
    /// analysis with its YYYY-MM start month and 1–20 years (and, for the analysis, its field); a farm summary without them.
    /// </summary>
    internal static bool ValidContext(AssistantConversationContext context)
    {
        if (context.FarmId == Guid.Empty) return false;
        var period = context.StartMonth?.Length == 7 &&
            DateOnly.TryParseExact(context.StartMonth + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) &&
            context.PlanningHorizonYears is >= 1 and <= AssistantPeriod.MaxYears;
        return context.LastTool switch
        {
            "prepare_solver_scenario" => period && context.FieldId is null && context.RunId is null,
            AssistantRouting.AnalyzeStrategies => period && context.FieldId is { } field && field != Guid.Empty,
            "get_farm_summary" => context.StartMonth is null && context.PlanningHorizonYears is null && context.FieldId is null && context.RunId is null,
            _ => false,
        };
    }
}
