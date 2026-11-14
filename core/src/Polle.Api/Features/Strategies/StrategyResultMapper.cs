using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Polle.Api.Features.Strategies;

/// <summary>Crop and growing-system names behind solver crop profile ids.</summary>
public sealed record CropProfileName(long CropId, string Crop, string System);

/// <summary>
/// Crop names behind solver crop profile ids, product names behind the fertilizer material ids felder picked, and the
/// base operating costs (OpEx) of each profile with the field's area to charge them on.
/// </summary>
/// <param name="opexCentsPerHa">US cents per hectare per growing season, by profile id then year (public.crop_profile_opex).</param>
/// <param name="areaHectares">The field's area; OpEx is per hectare, while felder's money figures are for the whole field.</param>
public sealed class CropProfileNames(IReadOnlyDictionary<long, CropProfileName> names, IReadOnlyDictionary<long, string>? materials = null,
    IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>>? opexCentsPerHa = null, double? areaHectares = null)
{
    public static CropProfileNames Empty { get; } = new(new Dictionary<long, CropProfileName>());
    public CropProfileName? Find(long profileId) => names.GetValueOrDefault(profileId);
    public string? Material(long materialId) => materials?.GetValueOrDefault(materialId);

    /// <summary>
    /// The profile's OpEx over the whole field for these growing seasons, in US dollars. A year without its own rate takes
    /// the nearest year's, as the Finance API does. Null when the profile has no OpEx rows or the field's area is unknown.
    /// </summary>
    public decimal? OperatingCostsUsd(long profileId, IEnumerable<int> seasonYears)
    {
        if (areaHectares is not { } area || area <= 0 || opexCentsPerHa?.GetValueOrDefault(profileId) is not { Count: > 0 } rates) return null;
        var cents = seasonYears.Sum(year => rates.TryGetValue(year, out var exact) ? exact
            : rates.MinBy(rate => (Math.Abs(rate.Key - year), rate.Key)).Value);
        return Math.Round(cents * (decimal)area / 100m, 2);
    }
}

/// <summary>One crop of a strategy, in farm units: US dollars, millimetres, months as YYYY-MM, days as YYYY-MM-DD.</summary>
/// <param name="Notes">The solver's informational notes for this crop: neither a risk nor a warning.</param>
/// <param name="Nutrients">Nitrogen, phosphorus and potassium added and taken off over the cycle, in kg/ha.</param>
/// <param name="Risks">Notes the solver marked as risks for this crop.</param>
/// <param name="Warnings">Notes the solver marked as warnings for this crop, e.g. missing data it worked around.</param>
/// <param name="MonthlyWater">Irrigation and crop water use per month; empty for results from before felder reported them.</param>
/// <param name="NoteDetails">Every note with its importance and category as the solver sent them, for categories this API does not know yet (e.g. soil_health).</param>
/// <param name="Fertilizer">The fertilizer product felder applied over the cycle and when; null when the crop got none.</param>
/// <param name="CostsUsd">Everything the crop costs: felder's expenses (seed, water, fertilizer) plus <paramref name="OperatingCostsUsd"/>.</param>
/// <param name="OperatingCostsUsd">Base operating costs (machinery, labour) of the profile per growing season over the field; null when unknown.</param>
/// <param name="CostBreakdown">What <paramref name="CostsUsd"/> is made of; null for results from before felder 2.4, which sent one expenses figure.</param>
/// <param name="Estimated">The current plan only: the solver left this crop out of its evaluation, so its figures are this API's audit (<see cref="PlanAudit.Complete"/>).</param>
/// <param name="PlantingDate">The sowing day, YYYY-MM-DD; null when only the month is known (results from before felder sent days).</param>
/// <param name="ClearanceDate">The day the field is cleared, YYYY-MM-DD; null when only the month is known.</param>
/// <param name="HarvestDates">The harvest days, YYYY-MM-DD; empty when only the months are known.</param>
public sealed record StrategyCycle(long CropProfileId, long CropId, string CropName, string SystemName, string PlantingMonth, string ClearanceMonth,
    string[] HarvestMonths, decimal RevenueUsd, decimal CostsUsd, double YieldPercent, double IrrigationMm, string[] Notes, CycleNutrients Nutrients,
    string[] Risks, string[] Warnings, MonthlyWater[] MonthlyWater, CycleNote[] NoteDetails, CycleFertilizer? Fertilizer = null, decimal? OperatingCostsUsd = null,
    CostBreakdown? CostBreakdown = null, bool Estimated = false, string? PlantingDate = null, string? ClearanceDate = null, string[]? HarvestDates = null);

/// <summary>Costs over the whole field, US dollars, as felder 2.4 itemises them.</summary>
/// <param name="OperatingUsd">Machinery, fuel and labour (base OpEx).</param>
/// <param name="PlantingUsd">Seed or planting material.</param>
/// <param name="WaterUsd">Irrigation water.</param>
/// <param name="FertilizerUsd">Fertilizer product.</param>
public sealed record CostBreakdown(decimal OperatingUsd, decimal PlantingUsd, decimal WaterUsd, decimal FertilizerUsd)
{
    public static CostBreakdown? Sum(IEnumerable<CostBreakdown?> parts)
    {
        var known = parts.OfType<CostBreakdown>().ToArray();
        return known.Length == 0 ? null : new CostBreakdown(known.Sum(part => part.OperatingUsd), known.Sum(part => part.PlantingUsd),
            known.Sum(part => part.WaterUsd), known.Sum(part => part.FertilizerUsd));
    }
}

/// <param name="MaterialId">public.materials id of the product felder chose; null when only the amounts are known (an older result).</param>
/// <param name="Name">The product's name from public.materials; null when the id is unknown there.</param>
/// <param name="Applications">The months with fertilizer, in order, with the N, P and K they add, kg/ha.</param>
/// <param name="RateKgHa">Kilograms of the product per hectare over the cycle; null for results from before felder reported it.</param>
public sealed record CycleFertilizer(string? MaterialId, string? Name, FertilizerApplication[] Applications, double? RateKgHa = null);

/// <param name="Month">YYYY-MM.</param>
public sealed record FertilizerApplication(string Month, double NitrogenKgHa, double PhosphorusKgHa, double PotassiumKgHa);

/// <param name="NitrogenFixedKgHa">Nitrogen legumes fix from the air; added on top of the fertilizer.</param>
public sealed record CycleNutrients(double NitrogenAppliedKgHa, double NitrogenFixedKgHa, double NitrogenRemovedKgHa,
    double PhosphorusAppliedKgHa, double PhosphorusRemovedKgHa, double PotassiumAppliedKgHa, double PotassiumRemovedKgHa);

/// <param name="Month">YYYY-MM.</param>
/// <param name="WaterUseMm">What the crop used (actual evapotranspiration, ETc).</param>
public sealed record MonthlyWater(string Month, double IrrigationMm, double WaterUseMm);

/// <param name="Importance">risk, warn or info.</param>
/// <param name="Category">The solver's category in lower case, e.g. agronomic, data_missing, soil_health.</param>
public sealed record CycleNote(string Message, string Importance, string Category);

/// <param name="Status">Solver status: OPTIMAL, FEASIBLE, INFEASIBLE or UNKNOWN.</param>
/// <param name="NitrogenBalanceKgHa">Applied plus fixed minus removed nitrogen over all crops.</param>
/// <param name="Risks">Notes marked as risks, without the solver's [TAG] prefix.</param>
/// <param name="DataGaps">Notes in the data_missing category: where information was missing.</param>
/// <param name="Warnings">Notes marked as warnings.</param>
/// <param name="WaterUseMm">Crop water use over all crops; null for results from before felder reported it.</param>
/// <param name="ProfitUsd">felder's expected profit less the operating costs; null when felder found no plan.</param>
/// <param name="CostsUsd">All costs, operating costs included.</param>
/// <param name="OperatingCostsUsd">Operating costs over all crops; null when none of them has OpEx figures.</param>
/// <param name="CostBreakdown">The crops' itemised costs added up; null when no crop has them (results from before felder 2.4).</param>
public sealed record StrategyResult(string Status, decimal? ProfitUsd, decimal RevenueUsd, decimal CostsUsd, double IrrigationMm,
    double NitrogenBalanceKgHa, StrategyCycle[] Cycles, string[] Risks, string[] DataGaps, string[] Warnings, double? WaterUseMm,
    decimal? OperatingCostsUsd = null, CostBreakdown? CostBreakdown = null);

/// <summary>
/// Turns a stored felder OptimizeFieldResponse into what the cards show. Results with financials (felder 2.4, 2026-11-12)
/// carry OpEx in their costs already. For older results, which have expenses_cents without OpEx, the profiles' operating
/// costs are added here, to every crop's costs, and taken off the profit.
/// </summary>
public static partial class StrategyResultMapper
{
    public const string Risk = "risk", Warning = "warn", Info = "info";

    private static readonly JsonSerializerOptions Options = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString };

    public static StrategyResult? Map(string? resultJson, CropProfileNames profiles)
    {
        if (string.IsNullOrEmpty(resultJson)) return null;
        OptimizeFieldResponse? response;
        try { response = JsonSerializer.Deserialize<OptimizeFieldResponse>(resultJson, Options); }
        catch (JsonException) { return null; }
        if (response is null) return null;

        var schedule = response.Schedule ?? [];
        var cycles = schedule.Select(cycle => Cycle(cycle, profiles)).ToArray();
        var notes = cycles.SelectMany(cycle => cycle.NoteDetails).ToArray();
        string[] Messages(Func<CycleNote, bool> keep) => notes.Where(keep).Select(note => note.Message).Distinct().ToArray();
        var reportsWaterUse = schedule.Any(cycle => cycle.MonthlyDynamics is not null);
        var operating = cycles.Any(cycle => cycle.OperatingCostsUsd is not null) ? cycles.Sum(cycle => cycle.OperatingCostsUsd ?? 0) : (decimal?)null;
        // felder 2.4 sends no profit, only each cycle's money with OpEx already in its costs.
        var hasFinancials = schedule.Any(cycle => cycle.Financials is not null);
        var profitUsd = hasFinancials ? cycles.Sum(cycle => cycle.RevenueUsd - cycle.CostsUsd)
            : response.ExpectedProfitCents is { } profit ? Usd(profit) - (operating ?? 0) : (decimal?)null;
        return new StrategyResult(response.Status, profitUsd,
            cycles.Sum(cycle => cycle.RevenueUsd), cycles.Sum(cycle => cycle.CostsUsd), cycles.Sum(cycle => cycle.IrrigationMm),
            cycles.Sum(cycle => cycle.Nutrients.NitrogenAppliedKgHa + cycle.Nutrients.NitrogenFixedKgHa - cycle.Nutrients.NitrogenRemovedKgHa), cycles,
            Messages(note => note.Importance == Risk), Messages(note => note.Category == "data_missing"), Messages(note => note.Importance == Warning),
            reportsWaterUse ? cycles.Sum(cycle => cycle.MonthlyWater.Sum(month => month.WaterUseMm)) : null, operating,
            CostBreakdown.Sum(cycles.Select(cycle => cycle.CostBreakdown)));
    }

    /// <summary>The years a crop is charged OpEx for: each year it is harvested in, or the year it is cleared when it isn't.</summary>
    internal static IReadOnlyList<int> SeasonYears(CropProfileCycle cycle)
    {
        var harvests = cycle.HarvestMonths.Select(Year).OfType<int>().Distinct().ToArray();
        return harvests.Length > 0 ? harvests : Year(cycle.ClearanceMonth) is { } cleared ? [cleared] : [];
    }

    private static int? Year(string? month) =>
        month is { Length: >= 4 } && int.TryParse(month[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;

    private static StrategyCycle Cycle(CropProfileCycle cycle, CropProfileNames profiles)
    {
        var id = long.TryParse(cycle.CropProfileId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var name = profiles.Find(id);
        var notes = Notes(cycle).ToArray();
        string[] Messages(string importance) => notes.Where(note => note.Importance == importance).Select(note => note.Message).Distinct().ToArray();
        // felder 2.3 sends monthly dynamics instead of the cycle totals; results stored before that keep the totals.
        var dynamics = cycle.MonthlyDynamics;
        var monthly = dynamics is null ? [] : MonthlyWaterOf(dynamics);
        var nutrients = dynamics is null
            ? new CycleNutrients(cycle.NitrogenAppliedKgHa, cycle.NitrogenFixedKgHa, cycle.NitrogenRemovedKgHa,
                cycle.PhosphorusAppliedKgHa, cycle.PhosphorusRemovedKgHa, cycle.PotassiumAppliedKgHa, cycle.PotassiumRemovedKgHa)
            : new CycleNutrients(Sum(dynamics.NitrogenAppliedKgHa), Sum(dynamics.NitrogenFixedKgHa), Sum(dynamics.NitrogenRemovedKgHa),
                Sum(dynamics.PhosphorusAppliedKgHa), Sum(dynamics.PhosphorusRemovedKgHa), Sum(dynamics.PotassiumAppliedKgHa), Sum(dynamics.PotassiumRemovedKgHa));
        // felder 2.4 charges OpEx itself (financials.base_opex_cents); only older results get the catalog rates added here.
        var money = cycle.Financials;
        var operating = money is not null ? Usd(money.BaseOpexCents) : profiles.OperatingCostsUsd(id, SeasonYears(cycle));
        var revenue = money is not null ? Usd(money.GrossRevenueCents) : Usd(cycle.ExpectedRevenueCents);
        var costs = money is not null ? Usd(money.CostsCents) : Usd(cycle.ExpensesCents) + (operating ?? 0);
        return new StrategyCycle(id, name?.CropId ?? 0, name?.Crop ?? "Unknown crop", name?.System ?? "", cycle.PlantingMonth, cycle.ClearanceMonth,
            cycle.HarvestMonths, revenue, costs, Math.Round(cycle.YieldMultiplier * 100, 1),
            dynamics is null ? cycle.IrrigationAppliedMm : Sum(dynamics.Irrigation), Messages(Info), nutrients,
            Messages(Risk), Messages(Warning), monthly, notes, FertilizerOf(cycle, profiles), operating,
            money is null ? null : new CostBreakdown(Usd(money.BaseOpexCents), Usd(money.PlantingCostCents), Usd(money.WaterCostCents), Usd(money.FertilizerCostCents)),
            PlantingDate: cycle.PlantingDay, ClearanceDate: cycle.ClearanceDay, HarvestDates: cycle.HarvestDays);
    }

    /// <summary>
    /// The product and the months it went on. Null when the cycle got no fertilizer: no product id and no N/P/K applied
    /// in any month.
    /// </summary>
    private static CycleFertilizer? FertilizerOf(CropProfileCycle cycle, CropProfileNames profiles)
    {
        var dynamics = cycle.MonthlyDynamics;
        var n = dynamics?.NitrogenAppliedKgHa ?? [];
        var p = dynamics?.PhosphorusAppliedKgHa ?? [];
        var k = dynamics?.PotassiumAppliedKgHa ?? [];
        var applications = n.Keys.Union(p.Keys).Union(k.Keys).Order(StringComparer.Ordinal)
            .Select(month => new FertilizerApplication(month, Math.Round(n.GetValueOrDefault(month), 2), Math.Round(p.GetValueOrDefault(month), 2),
                Math.Round(k.GetValueOrDefault(month), 2)))
            .Where(application => application.NitrogenKgHa > 0 || application.PhosphorusKgHa > 0 || application.PotassiumKgHa > 0)
            .ToArray();
        var given = cycle.FertilizerApplication?.MaterialId ?? cycle.FertilizerMaterialId;
        var materialId = string.IsNullOrWhiteSpace(given) ? null : given.Trim();
        if (materialId is null && applications.Length == 0) return null;
        var name = long.TryParse(materialId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? profiles.Material(id) : null;
        var rate = cycle.FertilizerApplication?.RateKgHa is { } kg && kg > 0 ? Math.Round(kg, 1) : (double?)null;
        return new CycleFertilizer(materialId, name, applications, rate);
    }

    private static MonthlyWater[] MonthlyWaterOf(CropProfileCycleMonthlyDynamics dynamics)
    {
        var irrigation = dynamics.Irrigation ?? [];
        var use = dynamics.WaterRemovedMm ?? [];
        return irrigation.Keys.Union(use.Keys).Order(StringComparer.Ordinal)
            .Select(month => new MonthlyWater(month, Math.Round(irrigation.GetValueOrDefault(month), 2), Math.Round(use.GetValueOrDefault(month), 2))).ToArray();
    }

    private static double Sum(Dictionary<string, double>? months) => months is null ? 0 : Math.Round(months.Values.Sum(), 2);

    /// <summary>
    /// A cycle's notes from either felder shape: notes (objects with importance and category, or tagged strings) or the
    /// older risk_notes (tagged strings). An object without a known importance counts as a risk, the cautious reading;
    /// a tagged [DATA] string is a data_missing warning, any other tag an agronomic risk.
    /// </summary>
    private static IEnumerable<CycleNote> Notes(CropProfileCycle cycle) =>
        cycle.Notes is { } notes ? notes.Select(FromElement).OfType<CycleNote>() : (cycle.RiskNotes ?? []).Select(FromTagged);

    private static CycleNote? FromElement(JsonElement note)
    {
        if (note.ValueKind == JsonValueKind.String) return FromTagged(note.GetString() ?? "");
        if (note.ValueKind != JsonValueKind.Object) return null;
        var message = Text(note, "message")?.Trim();
        if (string.IsNullOrEmpty(message)) return null;
        var category = Text(note, "category")?.Trim().ToLowerInvariant() is { Length: > 0 } given ? given : "agronomic";
        var importance = Text(note, "importance")?.Trim().ToLowerInvariant() switch
        {
            Info => Info,
            Warning or "warning" => Warning,
            Risk => Risk,
            // Before felder sent importance, data_missing notes were warnings and everything else a risk.
            _ => category == "data_missing" ? Warning : Risk,
        };
        return new CycleNote(message, importance, category);
    }

    private static CycleNote FromTagged(string note)
    {
        var tag = TagPrefix().Match(note);
        var isData = tag.Success && tag.Groups["tag"].Value.StartsWith("DATA", StringComparison.OrdinalIgnoreCase);
        var category = isData ? "data_missing" : tag.Success ? tag.Groups["tag"].Value.ToLowerInvariant() : "agronomic";
        return new CycleNote(TagPrefix().Replace(note, "").Trim(), isData ? Warning : Risk, category);
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal Usd(long cents) => cents / 100m;

    [GeneratedRegex(@"^\s*\[(?<tag>[A-Z_]+)\]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex TagPrefix();
}
