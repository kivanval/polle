using System.ComponentModel;
using Polle.Domain.Agriculture;

namespace Polle.Api.Features.Strategies;

public sealed record StrategyRunRequest(
    [property: Description("First month of the horizon, YYYY-MM; at most five years before this month, so past seasons can be evaluated too.")] string? StartMonth,
    [property: Description("Last month of the horizon, YYYY-MM; less than thirty years after the start.")] string? EndMonth,
    [property: Description("The strategies to run: presets by key and custom settings. Omitted or empty runs the six presets.")] StrategyChoice[]? Strategies = null,
    [property: Description("True scores only the field's current plan, with no strategies; the plan needs crops sown inside the horizon. False runs only the strategies, without scoring the plan.")] bool PlanOnly = false);

/// <summary>A preset by key, or custom solver settings with every field given.</summary>
/// <param name="Irrigation">OFF, BINARY, TERNARY, QUATERNARY or CONTINUOUS.</param>
/// <param name="Fertilizer">OFF, BINARY, TERNARY, QUATERNARY or CONTINUOUS.</param>
public sealed record StrategyChoice(
    [property: Description("Key of a preset from GET /api/strategy-presets; leave the other fields out.")] string? Preset = null,
    bool? AllowMissingData = null, bool? AllowAgronomicRisks = null, bool? AllowSoilDepletion = null, string? Irrigation = null, string? Fertilizer = null,
    [property: Description("Custom strategies only: the farmer's name for it, up to 80 characters; blank uses one made from the settings.")] string? Name = null,
    [property: Description("Custom strategies only: a description, up to 400 characters; blank uses one made from the settings.")] string? Description = null);

/// <summary>One strategy a run plans: its key, and for a custom one the farmer's name and description (null: the catalog's wording).</summary>
public sealed record StrategyPick(string Key, string? Name = null, string? Summary = null);

public sealed record StrategyPresetResponse(string Key, string Name, string Summary, StrategySettings Settings);

public sealed record StrategyJobResponse(string Key, string Name, string Summary,
    [property: Description("processing, ready or failed")] string State, StrategyResult? Result, string? Error,
    [property: Description("What the solver was allowed to do for this strategy; null for the current plan, which is scored with each crop's own water and fertilizer.")] StrategySettings? Settings = null,
    [property: Description("The current plan only: true when the solver left it unscored and the result is this API's audit from the crop catalog, the field's climate and its soil settings.")] bool Estimated = false);

/// <summary>What the solver may do for one strategy, as sent to it.</summary>
/// <param name="Irrigation">The solver may water the crops (IrrigationLevel is not OFF).</param>
/// <param name="Fertilizer">The solver may apply fertilizer (FertilizerLevel is not OFF).</param>
/// <param name="AllowSoilDepletion">The solver may draw the soil's N/P/K down over the horizon.</param>
/// <param name="AllowMissingData">The solver may plan crops whose figures are missing with estimates.</param>
/// <param name="IrrigationLevel">OFF, BINARY, TERNARY, QUATERNARY or CONTINUOUS.</param>
/// <param name="FertilizerLevel">OFF, BINARY, TERNARY, QUATERNARY or CONTINUOUS.</param>
public sealed record StrategySettings(bool Irrigation, bool Fertilizer, bool AllowAgronomicRisks, bool AllowSoilDepletion, bool AllowMissingData,
    string IrrigationLevel = "OFF", string FertilizerLevel = "OFF")
{
    public static StrategySettings From(StrategyOptions options) => new(options.Irrigation != ReplenishmentLevel.Off, options.Fertilizer != ReplenishmentLevel.Off,
        options.AllowAgronomicRisks, options.AllowSoilDepletion, options.AllowMissingData, Level(options.Irrigation), Level(options.Fertilizer));

    /// <summary>
    /// A strategy's settings. The current plan has none: its water and fertilizer are what the farmer entered per crop,
    /// not a level the solver may use.
    /// </summary>
    public static StrategySettings? For(string key) => key == StrategyCatalog.BaselineKey ? null
        : StrategyCatalog.Find(key) is { } strategy ? From(strategy.Options) : null;

    private static string Level(ReplenishmentLevel level) => level.ToString().ToUpperInvariant();
}

/// <summary>Turns the farmer's choices into the job keys of a run.</summary>
public static class StrategyChoices
{
    private static readonly IReadOnlyList<StrategyPick> DefaultPicks = StrategyCatalog.DefaultKeys.Select(key => new StrategyPick(key)).ToArray();

    /// <summary>
    /// Every chosen strategy in the order chosen. A preset repeated runs once; a custom strategy always runs as its own
    /// custom key, even when its settings match a preset, so a run plans as many strategies as the farmer chose. Two
    /// custom strategies with the same settings are refused, since they would plan the same thing. Errors are keyed
    /// strategies[i] for the choice at fault.
    /// </summary>
    public static (IReadOnlyList<StrategyPick> Picks, Dictionary<string, string[]> Errors) Picks(StrategyChoice[]? choices)
    {
        var errors = new Dictionary<string, string[]>();
        if (choices is null || choices.Length == 0) return (DefaultPicks, errors);
        if (choices.Length > StrategyCatalog.MaxStrategiesPerRun)
        {
            errors["strategies"] = [$"Choose at most {StrategyCatalog.MaxStrategiesPerRun} strategies at a time."];
            return ([], errors);
        }
        var picks = new List<StrategyPick>();
        for (var index = 0; index < choices.Length; index++)
        {
            var pick = PickOf(choices[index], out var error);
            if (error is null && pick!.Key.StartsWith(StrategyCatalog.CustomPrefix, StringComparison.Ordinal) && picks.Any(item => item.Key == pick.Key))
                error = "You already have a custom strategy with these settings.";
            if (error is not null) errors[$"strategies[{index}]"] = [error];
            else if (picks.All(item => item.Key != pick!.Key)) picks.Add(pick!);
        }
        return (picks, errors);
    }

    private static StrategyPick? PickOf(StrategyChoice? choice, out string? error)
    {
        error = null;
        if (choice is null) { error = "Choose a preset or give custom settings."; return null; }
        if (choice.Preset is { } preset)
        {
            if (StrategyCatalog.Presets.Any(candidate => candidate.Key == preset)) return new(preset);
            error = $"There is no preset called {preset}.";
            return null;
        }
        if (choice.AllowMissingData is not { } missing || choice.AllowAgronomicRisks is not { } risks || choice.AllowSoilDepletion is not { } depletion)
        {
            error = "Custom settings need allowMissingData, allowAgronomicRisks and allowSoilDepletion.";
            return null;
        }
        if (Level(choice.Irrigation) is not { } irrigation || Level(choice.Fertilizer) is not { } fertilizer)
        {
            error = "Irrigation and fertilizer must each be OFF, BINARY, TERNARY, QUATERNARY or CONTINUOUS.";
            return null;
        }
        var name = Text(choice.Name);
        var summary = Text(choice.Description);
        if (name?.Length > StrategyText.NameMaxLength) { error = $"Keep the name to {StrategyText.NameMaxLength} characters."; return null; }
        if (summary?.Length > StrategyText.SummaryMaxLength) { error = $"Keep the description to {StrategyText.SummaryMaxLength} characters."; return null; }
        return new(StrategyCatalog.CustomKey(new StrategyOptions(missing, risks, depletion, irrigation, fertilizer)), name, summary);
    }

    /// <summary>Trimmed, with runs of whitespace (line breaks too) as one space; null when blank.</summary>
    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static ReplenishmentLevel? Level(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "OFF" => ReplenishmentLevel.Off,
        "BINARY" => ReplenishmentLevel.Binary,
        "TERNARY" => ReplenishmentLevel.Ternary,
        "QUATERNARY" => ReplenishmentLevel.Quaternary,
        "CONTINUOUS" => ReplenishmentLevel.Continuous,
        _ => null,
    };
}

public sealed record StrategyRunResponse(Guid Id, Guid FieldId, string StartMonth, string EndMonth, DateTime CreatedAtUtc, StrategyJobResponse[] Jobs)
{
    public static StrategyRunResponse From(StrategyRun run, CropProfileNames profiles) => new(run.Id, run.FieldId, run.StartMonth, run.EndMonth, run.CreatedAtUtc,
        run.Jobs.OrderBy(job => Order(job.Key)).ThenBy(job => job.Key, StringComparer.Ordinal).Select(job => Job(job, profiles)).ToArray());

    public static StrategyJobResponse Job(StrategyJob job, CropProfileNames profiles)
    {
        var (name, summary) = Describe(job.Key, job.Name, job.Summary);
        // One result this API can't read must not fail the whole run: that card shows as failed and the others still answer.
        StrategyResult? result = null;
        var unreadable = false;
        if (job.State == StrategyJobs.Ready)
        {
            try { result = StrategyResultMapper.Map(job.ResultJson, profiles); }
            catch (Exception ex) when (ex is not OperationCanceledException) { unreadable = true; }
        }
        var state = unreadable ? StrategyJobs.Failed : job.State;
        // The solver's own failure text stays in the database for debugging; the farmer sees plain words.
        return new(job.Key, name, summary, state, result,
            state == StrategyJobs.Failed ? "This strategy couldn't be prepared." : null, StrategySettings.For(job.Key));
    }

    /// <summary>The farmer's own name and description where given, else the catalog's wording for the key.</summary>
    public static (string Name, string Summary) Describe(string key, string? ownName = null, string? ownSummary = null)
    {
        var (name, summary) = key == StrategyCatalog.BaselineKey
            ? (StrategyCatalog.BaselineName, StrategyCatalog.BaselineSummary)
            : StrategyCatalog.Find(key) is { } strategy ? (strategy.Name, strategy.Summary) : (key, "");
        return (ownName ?? name, ownSummary ?? summary);
    }

    private static int Order(string key) => key == StrategyCatalog.BaselineKey ? -1 : StrategyCatalog.Order(key);
}
