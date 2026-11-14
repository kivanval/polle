using System.Text.RegularExpressions;

namespace Polle.Api.Features.Strategies;

/// <summary>
/// How finely the solver may dose water or fertilizer, as felder's resolution enum. Off sends apply=false (with BINARY,
/// which felder ignores then). felder's levels: BINARY 0/100%, TERNARY 0/50/100%, QUATERNARY 0/50/80/100%, CONTINUOUS
/// 0-100% in 20% steps.
/// </summary>
public enum ReplenishmentLevel { Off, Binary, Ternary, Quaternary, Continuous }

/// <summary>What the solver may do for one strategy: its RiskSettings and ReplenishmentStrategy.</summary>
/// <param name="AllowMissingData">The solver may plan with estimates where a crop's figures are missing.</param>
/// <param name="AllowSoilDepletion">False makes the solver keep the soil's N/P/K balance up instead of mining it for this horizon.</param>
public sealed record StrategyOptions(bool AllowMissingData, bool AllowAgronomicRisks, bool AllowSoilDepletion, ReplenishmentLevel Irrigation, ReplenishmentLevel Fertilizer)
{
    public static string Resolution(ReplenishmentLevel level) => level == ReplenishmentLevel.Off ? "BINARY" : level.ToString().ToUpperInvariant();
}

public sealed record StrategyDefinition(string Key, string Name, string Summary, StrategyOptions Options)
{
    public bool Irrigation => Options.Irrigation != ReplenishmentLevel.Off;
    public bool Fertilizer => Options.Fertilizer != ReplenishmentLevel.Off;
}

/// <summary>
/// The solver configurations offered to the farmer. Six presets agreed with the solver author run by default; the farmer
/// may pick some of them and add custom ones, whose options are spelled out in the key (see <see cref="CustomKey"/>), so
/// runs and saved strategies keep only the key and, for a custom one, the farmer's name and description. The nine strategies of the earlier matrix still resolve, so strategies
/// saved with them keep their names.
/// </summary>
public static partial class StrategyCatalog
{
    /// <summary>Job key of the farmer's own plan, evaluated with the same weather and prices.</summary>
    public const string BaselineKey = "current-plan";
    public const string BaselineName = "Your current plan";
    public const string BaselineSummary = "The rotation you entered, with the water and fertilizer you gave each crop, checked against the same weather and prices.";

    /// <summary>Most strategies one run may ask the solver for.</summary>
    public const int MaxStrategiesPerRun = 12;

    private const ReplenishmentLevel Off = ReplenishmentLevel.Off, Ternary = ReplenishmentLevel.Ternary,
        Quaternary = ReplenishmentLevel.Quaternary, Continuous = ReplenishmentLevel.Continuous;

    /// <summary>
    /// The default presets, in the order Ivan listed them (2026-11-12). Agronomic risks are allowed in all of them, felder's
    /// default, since the preset list doesn't restrict them; a custom strategy can turn them off.
    /// </summary>
    public static IReadOnlyList<StrategyDefinition> Presets { get; } =
    [
        new("control", "Control", "Rain only, fertilizer in steps. Keeps the soil fed and plans only with complete figures.",
            new(false, true, false, Off, Quaternary)),
        new("rainfed-potential", "Rainfed potential", "Rain only, fertilizer in steps. May draw the soil down for higher yields.",
            new(false, true, true, Off, Quaternary)),
        new("medium", "Medium", "Some irrigation and fertilizer in steps. Keeps the soil fed.",
            new(false, true, false, Ternary, Quaternary)),
        new("full-inputs", "Full inputs", "Water and fertilizer dosed as finely as needed. Keeps the soil fed.",
            new(false, true, false, Continuous, Continuous)),
        new("full-inputs-estimates", "Full inputs with estimates", "Full inputs, and crops with missing figures are planned with estimates.",
            new(true, true, false, Continuous, Continuous)),
        new("cash-grab", "Cash grab", "Full inputs for the highest profit: estimates missing figures and may draw the soil down.",
            new(true, true, true, Continuous, Continuous)),
    ];

    /// <summary>The earlier three-by-three matrix, no longer run, kept so saved strategies and old runs still read.</summary>
    private static readonly StrategyDefinition[] Legacy =
    [
        Matrix("bold-irrigated", "Bold irrigated", true, true, true, true),
        Matrix("balanced-irrigated", "Balanced irrigated", true, true, true, false),
        Matrix("safe-irrigated", "Safe irrigated", true, true, false, false),
        Matrix("bold-fertilized", "Bold fertilized dryland", false, true, true, true),
        Matrix("balanced-fertilized", "Balanced fertilized dryland", false, true, true, false),
        Matrix("safe-fertilized", "Safe fertilized dryland", false, true, false, false),
        Matrix("bold-dryland", "Bold dryland", false, false, true, true),
        Matrix("balanced-dryland", "Balanced dryland", false, false, true, false),
        Matrix("safe-dryland", "Safe dryland", false, false, false, false),
    ];

    private static StrategyDefinition Matrix(string key, string name, bool irrigation, bool fertilizer, bool risks, bool depletion) =>
        new(key, name, $"{(risks ? "Accepts agronomic risks." : "Avoids every flagged risk.")} {(irrigation ? "Irrigated" : "Rain only")}, {(fertilizer ? "with" : "no")} fertilizer.",
            new(false, risks, depletion, irrigation ? Quaternary : Off, fertilizer ? Quaternary : Off));

    public static IReadOnlyList<string> DefaultKeys { get; } = Presets.Select(preset => preset.Key).ToArray();

    /// <summary>A preset, a strategy of the earlier matrix, or a custom key; null for anything else.</summary>
    public static StrategyDefinition? Find(string key) =>
        Presets.FirstOrDefault(strategy => strategy.Key == key) ?? Legacy.FirstOrDefault(strategy => strategy.Key == key) ?? Custom(key);

    public const string CustomPrefix = "custom-";

    /// <summary>
    /// custom-{missing data}{agronomic risks}{soil depletion}-{irrigation}-{fertilizer}: flags as 0/1, levels as three
    /// letters, e.g. custom-010-ter-qua. Settings equal to a preset still get a custom key, so the farmer's strategy runs
    /// beside that preset instead of being folded into it.
    /// </summary>
    public static string CustomKey(StrategyOptions options) =>
        $"{CustomPrefix}{Bit(options.AllowMissingData)}{Bit(options.AllowAgronomicRisks)}{Bit(options.AllowSoilDepletion)}-{Code(options.Irrigation)}-{Code(options.Fertilizer)}";

    /// <summary>Position in the default list, then custom strategies after every preset.</summary>
    public static int Order(string key)
    {
        for (var index = 0; index < Presets.Count; index++)
            if (Presets[index].Key == key) return index;
        return Presets.Count;
    }

    public static string LevelText(ReplenishmentLevel level) => level switch
    {
        ReplenishmentLevel.Off => "off",
        ReplenishmentLevel.Binary => "on or off",
        ReplenishmentLevel.Ternary => "in 3 steps",
        ReplenishmentLevel.Quaternary => "in 4 steps",
        _ => "dosed finely",
    };

    private static StrategyDefinition? Custom(string key)
    {
        var match = CustomPattern().Match(key);
        if (!match.Success) return null;
        var options = new StrategyOptions(match.Groups["m"].Value == "1", match.Groups["r"].Value == "1", match.Groups["d"].Value == "1",
            Level(match.Groups["i"].Value), Level(match.Groups["f"].Value));
        var name = $"Custom: water {LevelText(options.Irrigation)}, fertilizer {LevelText(options.Fertilizer)}";
        var stance = new[]
        {
            options.AllowAgronomicRisks ? "Accepts agronomic risks" : "Avoids every flagged risk",
            options.AllowSoilDepletion ? "may draw the soil down" : "keeps the soil fed",
            options.AllowMissingData ? "estimates missing figures" : "plans only with complete figures",
        };
        var water = options.Irrigation == ReplenishmentLevel.Off ? "Rain only" : $"Water {LevelText(options.Irrigation)}";
        var feed = options.Fertilizer == ReplenishmentLevel.Off ? "no fertilizer" : $"fertilizer {LevelText(options.Fertilizer)}";
        return new(key, name, $"{water}, {feed}. {stance[0]}, {stance[1]} and {stance[2]}.", options);
    }

    private static char Bit(bool value) => value ? '1' : '0';

    private static string Code(ReplenishmentLevel level) => level switch
    {
        ReplenishmentLevel.Off => "off",
        ReplenishmentLevel.Binary => "bin",
        ReplenishmentLevel.Ternary => "ter",
        ReplenishmentLevel.Quaternary => "qua",
        _ => "con",
    };

    private static ReplenishmentLevel Level(string code) => code switch
    {
        "off" => ReplenishmentLevel.Off,
        "bin" => ReplenishmentLevel.Binary,
        "ter" => ReplenishmentLevel.Ternary,
        "qua" => ReplenishmentLevel.Quaternary,
        _ => ReplenishmentLevel.Continuous,
    };

    [GeneratedRegex("^custom-(?<m>[01])(?<r>[01])(?<d>[01])-(?<i>off|bin|ter|qua|con)-(?<f>off|bin|ter|qua|con)$")]
    private static partial Regex CustomPattern();
}
