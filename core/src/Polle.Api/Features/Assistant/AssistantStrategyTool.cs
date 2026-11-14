using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Fields;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Assistant;

/// <summary>
/// Requests the assistant answers without asking the model to classify them: a request about strategies for a field
/// (pick, compare, calculate, analyze), and a reply that only gives a period, which continues the tool used last.
/// </summary>
public static partial class AssistantRouting
{
    public const string AnalyzeStrategies = "analyze_field_strategies";
    public const string PrepareScenario = "prepare_solver_scenario";

    /// <summary>The action to run for this message, or null to let the model classify it.</summary>
    public static string? Preroute(string message, AssistantConversationContext? previous, DateOnly today)
    {
        var text = message.ToLowerInvariant();
        if (previous?.LastTool is AnalyzeStrategies or PrepareScenario && AssistantPeriod.IsPeriodOnly(message, today)) return previous.LastTool;
        // A draft scenario or its JSON stays with the scenario tool.
        if (ScenarioRegex().IsMatch(text)) return null;
        if (StrategyRegex().IsMatch(text)) return AnalyzeStrategies;
        if (ActionRegex().IsMatch(text) && SubjectRegex().IsMatch(text)) return AnalyzeStrategies;
        // "And for 10 years?" / "а якщо на 10 років?" after an analysis: the same analysis, another period.
        if (previous?.LastTool == AnalyzeStrategies && !AssistantPeriod.Read(message, today).IsEmpty && FollowUpRegex().IsMatch(text)) return AnalyzeStrategies;
        return null;
    }

    [GeneratedRegex(@"сценар|scenario|json|чернетк|draft")]
    private static partial Regex ScenarioRegex();

    [GeneratedRegex(@"стратегі|strateg|солвер|solver|оптимізац|оптимізу|optimi[sz]")]
    private static partial Regex StrategyRegex();

    [GeneratedRegex(@"проаналізу|аналіз|analy[sz]|порахуй|розрахуй|перерахуй|calculat|recalculat|compare|порівня|підбери|підібрати|pick|recommend|порекоменду|порад|що\s+(?:краще\s+)?(?:посіяти|сіяти|садити|вирощувати)|what\s+(?:should|to)\s+(?:i\s+)?(?:plant|grow|sow)")]
    private static partial Regex ActionRegex();

    [GeneratedRegex(@"пол[еяюіь]|field|план|plan|сівозмін|rotation|прибут|profit|посіяти|сіяти|садити|вирощувати|plant|grow|sow")]
    private static partial Regex SubjectRegex();

    [GeneratedRegex(@"^\s*(?:а|і|та|and|what|how)\b|якщо|if\b|\?\s*$")]
    private static partial Regex FollowUpRegex();
}

/// <summary>
/// The assistant's strategy tool: finds the farmer's field, takes the period from the conversation (or the field's last
/// run, or the defaults), reads the field's strategy run for that period or starts one with the solver, waits a little
/// for it, and answers with the strategies' numbers and a recommendation. Ownership is checked against the session user.
/// </summary>
public sealed partial class AssistantStrategyTool(AppDbContext db, FelderClient solver, SolverMaterials materials, NasaPowerDailyClimate climate,
    ILogger<AssistantStrategyTool> log)
{
    /// <summary>How long one chat turn waits for a new run's strategies before answering with those ready.</summary>
    internal static TimeSpan WaitForResults { get; set; } = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(4);

    public async Task<AssistantTurnResult> AnalyzeAsync(ClaimsPrincipal user, string message, IReadOnlyList<AssistantHistoryItem> history,
        AssistantPageContext? page, AssistantConversationContext? previous, bool uk, DateOnly today, CancellationToken ct)
    {
        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("A valid session is required.");
        var fields = await db.Fields.AsNoTracking().Where(field => field.Farm.OwnerUserId == userId)
            .OrderBy(field => field.Name).ThenBy(field => field.Id)
            .Select(field => new AssistantStrategyField(field.Id, field.FarmId, field.Farm.Name, field.Name, field.AreaHectares, field.ParentFieldId))
            .ToListAsync(ct);
        var (status, picked, choices) = PickField(message, fields, page?.FarmId ?? previous?.FarmId, page?.FieldId, previous?.FieldId);
        if (picked is null) return new(status == "ambiguous"
            ? uk ? $"Для якого поля рахувати стратегії: {string.Join(", ", choices)}?" : $"Which field should I calculate strategies for: {string.Join(", ", choices)}?"
            : uk ? "Не знайшов вашого поля. Додайте поле на мапі або відкрийте сторінку господарства з потрібним полем." : "I couldn't find a field of yours. Draw one on the map or open the farm with that field.");

        var latest = await db.StrategyRuns.Include(run => run.Jobs).Where(run => run.FieldId == picked.Id)
            .OrderByDescending(run => run.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var fallback = latest is null ? null : new PeriodParts(latest.StartMonth, YearsOf(latest.StartMonth, latest.EndMonth));
        var period = AssistantPeriod.Resolve(message, history, previous, today, fallback);
        var fresh = RecalculateRegex().IsMatch(message.ToLowerInvariant());

        var run = !fresh && latest is not null && latest.StartMonth == period.StartMonth && latest.EndMonth == period.EndMonth ? latest : null;
        var started = false;
        if (run is null)
        {
            if (!solver.Configured) return new(uk ? "Розрахунок стратегій зараз недоступний: солвер не налаштований на сервері." : "Strategy calculations are unavailable right now: the solver isn't configured on the server.");
            var boundary = await db.Fields.AsNoTracking().Where(field => field.Id == picked.Id).Select(field => field.Boundary).SingleAsync(ct);
            if (!FieldSoil.Available(boundary, LandMask.Default)) return new(uk
                ? $"Для поля «{picked.Name}» немає даних про ґрунт у цьому місці, тож стратегії не змоделювати."
                : $"There's no soil data for {picked.Name}'s location, so strategies can't be modelled.");
            if (StrategyJobs.HorizonErrors(period.StartMonth, period.EndMonth, today).Count > 0)
                return new(uk ? "Не вдалося скласти період розрахунку. Напишіть, наприклад: «з жовтня 2027 на 10 років»." : "I couldn't build a planning period. Try e.g. \"from October 2027 for 10 years\".");
            run = await StrategyRunEndpoints.CreateAsync(picked.Id, period.StartMonth, period.EndMonth, StrategyChoices.Picks(null).Picks, db, solver, materials, ct);
            started = true;
            log.LogInformation("Assistant started strategy run {RunId} for field {FieldId}", run.Id, picked.Id);
        }

        var deadline = DateTime.UtcNow + (started ? WaitForResults : TimeSpan.Zero);
        await StrategyRunEndpoints.AdvanceAsync(run, db, solver, materials, ct, log);
        while (run.Jobs.Any(job => job.State == StrategyJobs.Processing) && DateTime.UtcNow + PollEvery < deadline)
        {
            await Task.Delay(PollEvery, ct);
            await StrategyRunEndpoints.AdvanceAsync(run, db, solver, materials, ct, log);
        }
        var response = await StrategyRunEndpoints.ResponseAsync(db, run, climate, ct, log);
        var text = StrategyRunDigest.Text(response, picked.Name, picked.AreaHectares, period, uk, started,
            started ? null : DateOnly.FromDateTime(run.CreatedAtUtc));
        return new(text, new(AssistantRouting.AnalyzeStrategies, picked.FarmId, period.StartMonth, period.Years, picked.Id, run.Id),
            StrategyRunId: run.Id, FieldId: picked.Id);
    }

    /// <summary>
    /// The field to plan: one named in the message (the longest name that matches), else the field open on the page, else
    /// the field of the conversation, else the only field (of the page's farm when one is open). Several candidates are
    /// listed for the farmer to choose.
    /// </summary>
    public static (string Status, AssistantStrategyField? Field, string[] Choices) PickField(string message, IReadOnlyList<AssistantStrategyField> fields,
        Guid? farmId, Guid? pageFieldId, Guid? previousFieldId)
    {
        var named = fields.Where(field => Named(message, field.Name)).ToArray();
        named = named.Where(field => !named.Any(other => other.Name.Length > field.Name.Length && other.Name.Contains(field.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (named.Length > 1 && farmId is { } farm && named.Count(field => field.FarmId == farm) == 1) named = named.Where(field => field.FarmId == farm).ToArray();
        if (named.Length == 1) return ("found", named[0], []);
        if (named.Length > 1) return ("ambiguous", null, named.Select(field => $"{field.Name} ({field.FarmName})").ToArray());
        if (fields.FirstOrDefault(field => field.Id == pageFieldId) is { } onPage) return ("found", onPage, []);
        if (fields.FirstOrDefault(field => field.Id == previousFieldId) is { } before) return ("found", before, []);
        var candidates = fields.Where(field => field.ParentFieldId is null && (farmId is null || field.FarmId == farmId)).ToArray();
        if (candidates.Length == 0 && farmId is not null) candidates = fields.Where(field => field.ParentFieldId is null).ToArray();
        return candidates.Length switch
        {
            0 => ("not_found", null, []),
            1 => ("found", candidates[0], []),
            _ => ("ambiguous", null, candidates.Select(field => $"{field.Name} ({field.FarmName})").ToArray()),
        };
    }

    private static bool Named(string text, string name)
    {
        name = name.Trim();
        if (name.Length < 2) return false;
        for (var start = text.IndexOf(name, StringComparison.OrdinalIgnoreCase); start >= 0; start = text.IndexOf(name, start + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = start + name.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
        }
        return false;
    }

    private static int YearsOf(string start, string end)
    {
        var months = (int.Parse(end[..4], CultureInfo.InvariantCulture) - int.Parse(start[..4], CultureInfo.InvariantCulture)) * 12
            + int.Parse(end[5..7], CultureInfo.InvariantCulture) - int.Parse(start[5..7], CultureInfo.InvariantCulture) + 1;
        return Math.Max(1, (int)Math.Round(months / 12.0));
    }

    [GeneratedRegex(@"перерахуй|заново|ще\s+раз|онов|recalculat|again|re-?run|fresh")]
    private static partial Regex RecalculateRegex();
}

public sealed record AssistantStrategyField(Guid Id, Guid FarmId, string FarmName, string Name, double AreaHectares, Guid? ParentFieldId);

/// <summary>A strategy run as the assistant's answer: what was calculated, each strategy's figures, and which to choose.</summary>
public static class StrategyRunDigest
{
    private const int MaxListed = 5, MaxRisks = 3;

    public static string Text(StrategyRunResponse run, string fieldName, double areaHectares, ResolvedPeriod period, bool uk, bool started, DateOnly? computedOn = null)
    {
        var text = new StringBuilder();
        var span = uk ? $"з {period.StartMonth} на {period.Years} {UkYears(period.Years)} (до {period.EndMonth})"
            : $"from {period.StartMonth} for {period.Years} {(period.Years == 1 ? "year" : "years")} (to {period.EndMonth})";
        text.Append(uk ? $"Рахую стратегії для поля «{fieldName}» {span}." : $"Strategies for field {fieldName} {span}.");
        if (period.AssumedText(uk) is { } assumed) text.Append(' ').Append(assumed);
        var baseline = run.Jobs.FirstOrDefault(job => job.Key == StrategyCatalog.BaselineKey);
        var strategyCount = run.Jobs.Count(job => job.Key != StrategyCatalog.BaselineKey);
        if (computedOn is { } day) text.Append(uk ? $" Беру розрахунок від {day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}; напишіть «перерахуй», щоб порахувати заново."
            : $" Using the calculation from {day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}; say \"recalculate\" for a fresh one.");
        else if (started) text.Append(uk
            ? $" Запустив солвер: {(baseline is null ? "" : "ваш поточний план і ")}{strategyCount} {(strategyCount == 1 ? "стратегія" : strategyCount < 5 ? "стратегії" : "стратегій")}."
            : $" I started the solver: {(baseline is null ? "" : "your current plan and ")}{strategyCount} {(strategyCount == 1 ? "strategy" : "strategies")}.");
        if (baseline is null) text.Append(uk ? " У вашому плані немає посівів у цьому періоді, тож порівнюю лише стратегії." : " Your plan has no crops sown in this period, so only the strategies are compared.");

        var mine = baseline is { State: StrategyJobs.Ready, Result: { Cycles.Length: > 0 } scored } ? scored : null;
        var usable = run.Jobs.Where(job => job.Key != StrategyCatalog.BaselineKey && job is { State: StrategyJobs.Ready, Result: { Cycles.Length: > 0 } result }
            && result.Status is "OPTIMAL" or "FEASIBLE").OrderByDescending(job => job.Result!.ProfitUsd ?? decimal.MinValue).ToArray();
        var pending = run.Jobs.Where(job => job.State == StrategyJobs.Processing).Select(job => job.Name).ToArray();
        var failed = run.Jobs.Where(job => job.Key != StrategyCatalog.BaselineKey && job.State == StrategyJobs.Failed).Select(job => job.Name).ToArray();
        var noPlan = run.Jobs.Where(job => job.Key != StrategyCatalog.BaselineKey && job.State == StrategyJobs.Ready && !usable.Contains(job)).Select(job => job.Name).ToArray();
        var total = run.Jobs.Length;
        text.Append('\n').Append(uk ? $"Готово {total - pending.Length} з {total}." : $"{total - pending.Length} of {total} ready.");

        if (mine is not null)
            text.Append('\n').Append(uk ? $"• Ваш поточний план{(baseline!.Estimated ? " (оцінка за довідником культур і кліматом поля)" : "")}: прибуток {Money(mine.ProfitUsd)}, витрати {Money(mine.CostsUsd)}, ризиків {mine.Risks.Length}."
                : $"• Your current plan{(baseline!.Estimated ? " (estimated from the crop catalog and the field's climate)" : "")}: profit {Money(mine.ProfitUsd)}, costs {Money(mine.CostsUsd)}, {mine.Risks.Length} risks.");
        foreach (var job in usable.Take(MaxListed))
        {
            var result = job.Result!;
            var versus = mine?.ProfitUsd is { } own && result.ProfitUsd is { } profit
                ? uk ? $", {Signed(profit - own)} до поточного плану" : $", {Signed(profit - own)} vs your plan" : "";
            var water = $"{Number(result.IrrigationMm)} {(uk ? "мм" : "mm")} ({Number(result.IrrigationMm * 10 * areaHectares)} {(uk ? "м³" : "m³")})";
            text.Append('\n').Append(uk
                ? $"• {job.Name}: прибуток {Money(result.ProfitUsd)}{versus}; витрати {Money(result.CostsUsd)}; полив {water}; ризиків {result.Risks.Length}; культури: {Crops(result)}."
                : $"• {job.Name}: profit {Money(result.ProfitUsd)}{versus}; costs {Money(result.CostsUsd)}; irrigation {water}; {result.Risks.Length} risks; crops: {Crops(result)}.");
        }

        if (usable.Length > 0)
        {
            var best = usable[0];
            var safest = usable.OrderBy(job => job.Result!.Risks.Length).ThenByDescending(job => job.Result!.ProfitUsd ?? decimal.MinValue).First();
            var driest = usable.OrderBy(job => job.Result!.IrrigationMm).ThenByDescending(job => job.Result!.ProfitUsd ?? decimal.MinValue).First();
            text.Append('\n').Append(uk ? $"Рекомендую «{best.Name}»: найвищий прибуток {Money(best.Result!.ProfitUsd)}." : $"I'd choose {best.Name}: the highest profit, {Money(best.Result!.ProfitUsd)}.");
            if (safest != best && safest.Result!.Risks.Length < best.Result.Risks.Length)
                text.Append(' ').Append(uk ? $"Якщо важливіше менше ризиків — «{safest.Name}» ({safest.Result.Risks.Length} ризиків, прибуток {Money(safest.Result.ProfitUsd)})."
                    : $"For fewer risks, {safest.Name} ({safest.Result.Risks.Length} risks, profit {Money(safest.Result.ProfitUsd)}).");
            if (driest != best && driest.Result!.IrrigationMm < best.Result.IrrigationMm)
                text.Append(' ').Append(uk ? $"Найменше води — «{driest.Name}» ({Number(driest.Result.IrrigationMm)} мм)." : $"Least water: {driest.Name} ({Number(driest.Result.IrrigationMm)} mm).");
            var risks = best.Result.Risks.Take(MaxRisks).ToArray();
            if (risks.Length > 0) text.Append('\n').Append(uk ? $"Головні ризики «{best.Name}»: " : $"Main risks of {best.Name}: ").Append(string.Join(" · ", risks)).Append('.');
        }
        else if (pending.Length == 0)
            text.Append('\n').Append(uk ? "Жодна стратегія не знайшла плану в цих межах для поля." : "No strategy found a plan within these limits for the field.");
        if (mine is not null && usable.Length == 0 && mine.Risks.Length > 0)
            text.Append('\n').Append(uk ? "Ризики поточного плану: " : "Risks of your current plan: ").Append(string.Join(" · ", mine.Risks.Take(MaxRisks))).Append('.');

        if (noPlan.Length > 0) text.Append('\n').Append(uk ? "Без плану в цих межах: " : "No plan within the limits: ").Append(string.Join(", ", noPlan)).Append('.');
        if (failed.Length > 0) text.Append('\n').Append(uk ? "Не вдалося підготувати: " : "Couldn't be prepared: ").Append(string.Join(", ", failed)).Append('.');
        if (pending.Length > 0) text.Append('\n').Append(uk
            ? $"Ще рахуються: {string.Join(", ", pending)}. Спитайте мене ще раз за хвилину або відкрийте «Plan & strategies» поля — картки з’являться там."
            : $"Still calculating: {string.Join(", ", pending)}. Ask me again in a minute, or open the field's Plan & strategies to watch the cards fill in.");
        return text.ToString();
    }

    private static string Crops(StrategyResult result) =>
        string.Join(" → ", result.Cycles.Take(6).Select(cycle => cycle.CropName)) + (result.Cycles.Length > 6 ? " …" : "");

    private static string Money(decimal? value) => value is { } amount
        ? $"{(amount < 0 ? "−" : "")}${Math.Abs(amount).ToString("N0", CultureInfo.InvariantCulture)}" : "—";

    private static string Number(double value) => Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

    private static string Signed(decimal value) => $"{(value > 0 ? "+" : value < 0 ? "−" : "±")}${Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture)}";

    private static string UkYears(int years) => (years % 100) is >= 11 and <= 14 ? "років" : (years % 10) switch { 1 => "рік", 2 or 3 or 4 => "роки", _ => "років" };
}
