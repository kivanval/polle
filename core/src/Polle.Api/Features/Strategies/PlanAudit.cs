using System.Globalization;
using Polle.Api.Features.Climate;
using Polle.Domain.Agriculture;

namespace Polle.Api.Features.Strategies;

/// <summary>One growth stage of a crop profile (public.crop_profile_stages), in sequence order.</summary>
/// <param name="MinDays">Earliest day after planting the stage ends (min_days_since_planting).</param>
/// <param name="MaxDays">Latest day after planting the stage ends (max_days_since_planting).</param>
/// <param name="Kc">FAO-56 crop coefficient (water_crop_coefficient).</param>
/// <param name="Ky">FAO-33 yield response to water stress (water_yield_response_factor).</param>
public sealed record AuditStage(string Category, int MinDays, int MaxDays, double Kc, double Ky, double? AbsoluteMinTempC, double? AbsoluteMaxTempC);

/// <summary>What the audit needs of one crop profile (public.crop_profiles and its stages).</summary>
/// <param name="YieldTonsPerHa">potential_yield_tons_per_ha; null when the catalog has none.</param>
/// <param name="PlantingRateKgPerHa">Seed or planting material per hectare.</param>
/// <param name="RemovalPerTon">N, P2O5 and K2O taken off per tonne of product (the catalog's Ukrainian norms).</param>
/// <param name="NitrogenFixedKgPerHa">Nitrogen a legume fixes per season.</param>
public sealed record AuditProfile(long Id, long CropId, string CropName, string SystemName, bool IsPerennial, double? YieldTonsPerHa,
    double? PlantingRateKgPerHa, long? ProductId, long? PlantingMaterialId, (double N, double P2O5, double K2O)? RemovalPerTon,
    double NitrogenFixedKgPerHa, IReadOnlyList<AuditStage> Stages);

/// <summary>One calendar month of the field's ten-year climate normals, January first.</summary>
/// <param name="Et0Mm">Reference evapotranspiration (Hargreaves) over the month.</param>
public sealed record AuditMonthClimate(double RainMm, double Et0Mm, double MinTempC, double MaxTempC);

/// <summary>The field's own settings the farmer recorded: soil reading and starting moisture.</summary>
public sealed record AuditField(double AreaHectares, string? DroughtLevel, double? InitialDepletionMm);

/// <param name="IrrigationMm">Irrigation the farmer plans over the season, spread evenly over its months; 0 is rain-fed.</param>
/// <param name="Fertilizer">The fertilizer the farmer plans for the season; null for none.</param>
public sealed record AuditPlanting(long CropProfileId, DateOnly PlantingDate, DateOnly? HarvestingDate, double IrrigationMm = 0,
    AuditFertilizer? Fertilizer = null);

/// <summary>A planting's fertilizer: the product, kilograms of it per hectare and its label content (N, P₂O₅, K₂O, percent).</summary>
public sealed record AuditFertilizer(long MaterialId, double RateKgHa, double NitrogenPercent, double PhosphatePercent, double PotashPercent, string? Name = null);

/// <param name="Plantings">Every planting of the field; those sown in the horizon are audited.</param>
/// <param name="Prices">US cents per tonne by material id, then year (public.material_prices).</param>
/// <param name="OpexCentsPerHa">US cents per hectare per season by profile id, then year (public.crop_profile_opex).</param>
/// <param name="Climate">Twelve months, January first; null when the climate baseline couldn't be read.</param>
public sealed record PlanAuditInput(string StartMonth, string EndMonth, AuditField Field, IReadOnlyList<AuditPlanting> Plantings,
    IReadOnlyDictionary<long, AuditProfile> Profiles, IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> Prices,
    IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> OpexCentsPerHa, IReadOnlyList<AuditMonthClimate>? Climate);

/// <summary>
/// A financial and water estimate of the farmer's own plan, worked out here from the same catalog the solver reads (crop
/// profiles and stages, prices, operating costs), the field's ten-year climate normals and its recorded soil settings. It
/// stands in for the solver's evaluation when that comes back without figures (missing crop data, a plan that breaks the
/// limits as entered), so "Your current plan" still has its summary, water balance and N-P-K, and it fills in the crops an
/// evaluation left out (<see cref="Complete"/>).
/// It raises no risks or warnings of its own: only felder judges a plan, so the cards show felder's risks and nothing else.
/// Its only notes are plain information (the fertilizer a crop takes off, water estimated by month).
/// The plan is audited with the water and fertilizer the farmer recorded for each crop (none: rain-fed and unfertilized).
/// </summary>
public static class PlanAudit
{
    /// <summary>Status of an audited (not solver-scored) result.</summary>
    public const string EstimatedStatus = "ESTIMATED";

    /// <summary>Category of the note on a crop whose water by month this audit estimated (<see cref="Complete"/>).</summary>
    public const string UnscoredCategory = "estimated";

    private const double P2O5ToP = 0.436, K2OToK = 0.830;
    private const double DefaultCycleDays = 120;

    public static StrategyResult? Build(PlanAuditInput input)
    {
        var first = Month(input.StartMonth);
        var last = Month(input.EndMonth);
        var all = input.Plantings.OrderBy(planting => planting.PlantingDate).ToArray();
        var audited = all.Where(planting => MonthOf(planting.PlantingDate) >= first && MonthOf(planting.PlantingDate) <= last).ToArray();
        if (audited.Length == 0) return null;

        var area = input.Field.AreaHectares > 0 ? input.Field.AreaHectares : 0;
        var cycles = audited.Select(planting => Cycle(input, planting, input.Profiles.GetValueOrDefault(planting.CropProfileId), area)).ToArray();

        var revenue = cycles.Sum(cycle => cycle.RevenueUsd);
        var costs = cycles.Sum(cycle => cycle.CostsUsd);
        var operating = cycles.Any(cycle => cycle.OperatingCostsUsd is not null) ? cycles.Sum(cycle => cycle.OperatingCostsUsd ?? 0) : (decimal?)null;
        return new StrategyResult(EstimatedStatus, revenue - costs, revenue, costs, 0,
            Math.Round(cycles.Sum(cycle => cycle.Nutrients.NitrogenAppliedKgHa + cycle.Nutrients.NitrogenFixedKgHa - cycle.Nutrients.NitrogenRemovedKgHa), 1),
            cycles, [], [], [],
            input.Climate is null ? null : Math.Round(cycles.Sum(cycle => cycle.MonthlyWater.Sum(month => month.WaterUseMm)), 1),
            operating, CostBreakdown.Sum(cycles.Select(cycle => cycle.CostBreakdown)));
    }

    /// <summary>
    /// The solver's evaluation of the farmer's plan with the crops it left out filled in from this audit. felder before
    /// 2026-11-13 dropped a crop it couldn't simulate (missing figures, a profile it doesn't know, a sowing month the crop
    /// can't survive) and still reported the rest as FEASIBLE; since then it returns every crop with its own notes, so this
    /// mostly fills in results stored before. Each crop filled in here is marked Estimated (the card says so) and adds no
    /// risks or warnings: the result's risks stay the solver's. Crops are matched on profile and planting month first, then
    /// on profile alone. The scored result comes back unchanged when no crop is missing.
    /// </summary>
    public static StrategyResult Complete(StrategyResult scored, StrategyResult audit)
    {
        var unmatched = scored.Cycles.ToList();
        // Each scored crop with the audit's own estimate of it, for the water the solver couldn't simulate.
        var twins = new Dictionary<StrategyCycle, StrategyCycle>(ReferenceEqualityComparer.Instance);
        bool Take(StrategyCycle audited, Func<StrategyCycle, bool> match)
        {
            var hit = unmatched.FirstOrDefault(match);
            if (hit is null || !unmatched.Remove(hit)) return false;
            twins[hit] = audited;
            return true;
        }
        var pending = new List<StrategyCycle>();
        foreach (var cycle in audit.Cycles)
            if (!Take(cycle, other => other.CropProfileId == cycle.CropProfileId && other.PlantingMonth == cycle.PlantingMonth)) pending.Add(cycle);
        var missing = new List<StrategyCycle>();
        foreach (var cycle in pending)
            if (!Take(cycle, other => other.CropProfileId == cycle.CropProfileId)) missing.Add(cycle with { Estimated = true });
        scored = WithEstimatedWater(scored, twins);
        if (missing.Count == 0) return scored;

        var cycles = scored.Cycles.Concat(missing).OrderBy(cycle => cycle.PlantingDate ?? cycle.PlantingMonth, StringComparer.Ordinal).ToArray();
        var revenue = missing.Sum(cycle => cycle.RevenueUsd);
        var costs = missing.Sum(cycle => cycle.CostsUsd);
        var addedWater = missing.Sum(cycle => cycle.MonthlyWater.Sum(month => month.WaterUseMm));
        return scored with
        {
            ProfitUsd = scored.ProfitUsd + revenue - costs,
            RevenueUsd = scored.RevenueUsd + revenue,
            CostsUsd = scored.CostsUsd + costs,
            NitrogenBalanceKgHa = Math.Round(scored.NitrogenBalanceKgHa
                + missing.Sum(cycle => cycle.Nutrients.NitrogenAppliedKgHa + cycle.Nutrients.NitrogenFixedKgHa - cycle.Nutrients.NitrogenRemovedKgHa), 1),
            Cycles = cycles,
            WaterUseMm = scored.WaterUseMm is null && addedWater == 0 ? null : Math.Round((scored.WaterUseMm ?? 0) + addedWater, 1),
            OperatingCostsUsd = cycles.Any(cycle => cycle.OperatingCostsUsd is not null) ? cycles.Sum(cycle => cycle.OperatingCostsUsd ?? 0) : null,
            CostBreakdown = CostBreakdown.Sum(cycles.Select(cycle => cycle.CostBreakdown)),
        };
    }

    /// <summary>
    /// The solver skips the water balance of a crop whose catalog profile lacks water figures (it notes "Missing moisture
    /// depletion fraction") and sends its months empty. Such a crop takes the audit's monthly water instead, with a note
    /// saying so, so the current plan's water chart reads by month like a strategy's.
    /// </summary>
    private static StrategyResult WithEstimatedWater(StrategyResult scored, IReadOnlyDictionary<StrategyCycle, StrategyCycle> twins)
    {
        var filled = 0.0;
        var cycles = scored.Cycles.Select(cycle =>
        {
            if (cycle.MonthlyWater.Length > 0 || !twins.TryGetValue(cycle, out var twin) || twin.MonthlyWater.Length == 0) return cycle;
            var note = new CycleNote($"Water by month is estimated: the planner had no water figures for the {cycle.CropName}, so its water use comes "
                + "from the crop catalog and the field's average weather.", StrategyResultMapper.Info, UnscoredCategory);
            filled += twin.MonthlyWater.Sum(month => month.WaterUseMm);
            return cycle with { MonthlyWater = twin.MonthlyWater.Select(month => month with { IrrigationMm = 0 }).ToArray(),
                Notes = [.. cycle.Notes, note.Message], NoteDetails = [.. cycle.NoteDetails, note] };
        }).ToArray();
        return filled == 0 ? scored : scored with { Cycles = cycles, WaterUseMm = Math.Round((scored.WaterUseMm ?? 0) + filled, 1) };
    }

    private static StrategyCycle Cycle(PlanAuditInput input, AuditPlanting planting, AuditProfile? profile, double area)
    {
        var notes = new List<CycleNote>();
        var name = profile?.CropName ?? "Unknown crop";
        var plantingMonth = MonthOf(planting.PlantingDate);
        var stages = StageEnds(profile);
        var cycleDays = stages.Count > 0 ? stages[^1].End : DefaultCycleDays;
        var clearanceDate = planting.HarvestingDate
            ?? (profile?.IsPerennial == true ? DateOnly.ParseExact($"{input.EndMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : planting.PlantingDate.AddDays((int)Math.Round(cycleDays)));
        var clearanceMonth = Math.Max(plantingMonth, MonthOf(clearanceDate));
        var harvests = HarvestMonths(profile, planting.PlantingDate, plantingMonth, clearanceMonth, cycleDays);

        // Water: the crop's need (Kc × ET0) each month against the rain that reaches the roots plus what the soil holds.
        var monthly = new List<MonthlyWater>();
        double need = 0, deficit = 0, weightedKy = 0;
        var irrigation = Math.Max(0, planting.IrrigationMm);
        if (input.Climate is { Count: 12 } climate)
        {
            var irrigationPerMonth = irrigation / (clearanceMonth - plantingMonth + 1);
            var capacity = SoilBufferMm(input.Field.DroughtLevel);
            var store = Math.Max(0, capacity - (input.Field.InitialDepletionMm ?? 0));
            for (var month = plantingMonth; month <= clearanceMonth; month++)
            {
                var normals = climate[month % 12];
                var day = DaysSincePlanting(planting.PlantingDate, month);
                var (kc, stageKy) = Coefficients(stages, day, profile?.IsPerennial == true);
                var etc = Math.Round(kc * normals.Et0Mm, 1);
                var effective = EffectiveRain(normals.RainMm);
                store += effective + irrigationPerMonth - etc;
                var shortfall = store < 0 ? -store : 0;
                store = Math.Clamp(store, 0, capacity);
                need += etc; deficit += shortfall; weightedKy += stageKy * etc;
                monthly.Add(new MonthlyWater(Text(month), Math.Round(irrigationPerMonth, 2), etc));
            }
        }

        // Yield from the FAO-33 water response: Ya/Ym = 1 − Ky (1 − ETa/ETc).
        var ky = need > 0 ? weightedKy / need : 1;
        var waterShare = need > 0 ? Math.Clamp((need - deficit) / need, 0, 1) : 1;
        var yieldFactor = Math.Clamp(1 - (ky <= 0 ? 1 : ky) * (1 - waterShare), 0, 1);

        // Nutrients: what the harvests take off, against legume fixation and the farmer's fertilizer.
        var tonsPerHa = (profile?.YieldTonsPerHa ?? 0) * yieldFactor;
        var harvestCount = Math.Max(1, harvests.Length);
        var removal = profile?.RemovalPerTon;
        var removedN = removal is { } r1 ? Math.Round(r1.N * tonsPerHa * harvestCount, 1) : 0;
        var removedP = removal is { } r2 ? Math.Round(r2.P2O5 * P2O5ToP * tonsPerHa * harvestCount, 1) : 0;
        var removedK = removal is { } r3 ? Math.Round(r3.K2O * K2OToK * tonsPerHa * harvestCount, 1) : 0;
        var fixedN = Math.Round((profile?.NitrogenFixedKgPerHa ?? 0) * (profile?.IsPerennial == true ? harvestCount : 1), 1);
        var needN = Math.Max(0, removedN - fixedN);
        // The farmer's own fertilizer, in pure N, P and K kg/ha from the product's label content.
        var fertilizer = planting.Fertilizer;
        var appliedN = fertilizer is { } f1 ? Math.Round(f1.RateKgHa * f1.NitrogenPercent / 100, 1) : 0;
        var appliedP = fertilizer is { } f2 ? Math.Round(f2.RateKgHa * f2.PhosphatePercent / 100 * P2O5ToP, 1) : 0;
        var appliedK = fertilizer is { } f3 ? Math.Round(f3.RateKgHa * f3.PotashPercent / 100 * K2OToK, 1) : 0;
        if (fertilizer is not null && needN + removedP + removedK > 0)
        {
            var nitrogen = fixedN > 0 ? $"{needN:0} kg N (after the {fixedN:0} kg N it fixes from the air)" : $"{needN:0} kg N";
            var shortN = Math.Max(0, needN - appliedN); var shortP = Math.Max(0, removedP - appliedP); var shortK = Math.Max(0, removedK - appliedK);
            var gaps = new[] { (shortN, "N"), (shortP, "P"), (shortK, "K") }.Where(gap => gap.Item1 >= 1).Select(gap => $"{gap.Item1:0} kg {gap.Item2}").ToArray();
            notes.Add(new CycleNote($"Fertilizer: the plan's {fertilizer.RateKgHa:0} kg/ha of fertilizer adds about {appliedN:0} kg N, {appliedP:0} kg P and {appliedK:0} kg K per hectare. "
                + $"The expected {name} harvest ({tonsPerHa * harvestCount:0.#} t/ha) takes off about {nitrogen}, {removedP:0} kg P and {removedK:0} kg K, "
                + (gaps.Length > 0 ? $"so each hectare is still short of about {string.Join(", ", gaps)}." : "so the fertilizer puts it all back.")
                + " These are pure nutrients, as in the N-P-K balance.", StrategyResultMapper.Info, "nutrients"));
        }
        else if (needN + removedP + removedK > 0)
        {
            // Pure N, P and K, the units of the crop's N-P-K balance, so the two can be read side by side.
            var nitrogen = fixedN > 0 ? $"{needN:0} kg N (after the {fixedN:0} kg N it fixes from the air)" : $"{needN:0} kg N";
            notes.Add(new CycleNote($"Fertilizer need: the plan adds no fertilizer. The expected {name} harvest "
                + $"({tonsPerHa * harvestCount:0.#} t/ha) takes nutrients off the field. To put them back, each hectare would need about {nitrogen}, "
                + $"{removedP:0} kg P and {removedK:0} kg K. These are pure nutrients, as in the N-P-K balance, from the crop catalog's removal per tonne of harvest.",
                StrategyResultMapper.Info, "nutrients"));
        }

        // Money over the whole field: product sold at the catalog price, seed and base operating costs. A figure the catalog
        // lacks a price for is left out.
        decimal revenue = 0, seed = 0;
        if (profile is not null && area > 0)
        {
            if (profile.ProductId is { } product && tonsPerHa > 0)
            {
                foreach (var harvest in harvests.DefaultIfEmpty(clearanceMonth))
                {
                    if (Price(input.Prices, product, harvest / 12) is not { } cents) break;
                    revenue += (decimal)(tonsPerHa * area) * cents / 100m;
                }
            }
            if (profile.PlantingMaterialId is { } material && profile.PlantingRateKgPerHa is { } rate && rate > 0
                && Price(input.Prices, material, plantingMonth / 12) is { } seedCents)
                seed = (decimal)(rate * area / 1000) * seedCents / 100m;
        }
        decimal fertilizerCost = 0;
        if (fertilizer is not null && area > 0 && Price(input.Prices, fertilizer.MaterialId, plantingMonth / 12) is { } fertilizerCents)
            fertilizerCost = Math.Round((decimal)(fertilizer.RateKgHa * area / 1000) * fertilizerCents / 100m, 2);
        decimal? operating = null;
        if (profile is not null && area > 0 && input.OpexCentsPerHa.GetValueOrDefault(profile.Id) is { Count: > 0 } rates)
        {
            var seasons = harvests.Select(month => month / 12).DefaultIfEmpty(clearanceMonth / 12).Distinct();
            operating = Math.Round(seasons.Sum(year => (decimal)(rates.TryGetValue(year, out var exact) ? exact : rates.MinBy(rate => Math.Abs(rate.Key - year)).Value)) * (decimal)area / 100m, 2);
        }
        revenue = Math.Round(revenue, 2);
        seed = Math.Round(seed, 2);
        var costs = seed + fertilizerCost + (operating ?? 0);
        return new StrategyCycle(profile?.Id ?? planting.CropProfileId, profile?.CropId ?? 0, name, profile?.SystemName ?? "", Text(plantingMonth), Text(clearanceMonth),
            harvests.Select(Text).ToArray(), revenue, costs, Math.Round(yieldFactor * 100, 1), Math.Round(irrigation, 1), notes.Select(note => note.Message).Distinct().ToArray(),
            new CycleNutrients(appliedN, fixedN, removedN, appliedP, removedP, appliedK, removedK), [], [],
            [.. monthly], [.. notes], fertilizer is null ? null : new CycleFertilizer(fertilizer.MaterialId.ToString(CultureInfo.InvariantCulture), fertilizer.Name, [], fertilizer.RateKgHa),
            operating, new CostBreakdown(operating ?? 0, seed, 0, fertilizerCost),
            PlantingDate: Day(planting.PlantingDate), ClearanceDate: planting.HarvestingDate is { } harvested ? Day(harvested) : null);
    }

    /// <summary>Each stage's typical last day after planting (the middle of its window), never earlier than the stage before.</summary>
    internal static List<(AuditStage Stage, double End)> StageEnds(AuditProfile? profile)
    {
        var ends = new List<(AuditStage, double)>();
        var previous = 0.0;
        foreach (var stage in profile?.Stages ?? [])
        {
            var end = Math.Max(previous + 1, (stage.MinDays + Math.Max(stage.MinDays, stage.MaxDays)) / 2.0);
            ends.Add((stage, end));
            previous = end;
        }
        return ends;
    }

    private static (double Kc, double Ky) Coefficients(List<(AuditStage Stage, double End)> stages, double day, bool perennial)
    {
        if (stages.Count == 0) return (0.8, 1.0);
        foreach (var (stage, end) in stages)
            if (day <= end) return (stage.Kc, stage.Ky);
        // A perennial keeps growing after its first cycle: its established stages on average.
        if (perennial)
        {
            var grown = stages.Skip(1).DefaultIfEmpty(stages[^1]).ToArray();
            return (grown.Average(item => item.Stage.Kc), grown.Average(item => item.Stage.Ky));
        }
        return (stages[^1].Stage.Kc, stages[^1].Stage.Ky);
    }

    /// <summary>
    /// An annual is harvested in the month it is cleared; a perennial every year in the month its first cycle ends, up to
    /// clearance.
    /// </summary>
    private static int[] HarvestMonths(AuditProfile? profile, DateOnly planted, int plantingMonth, int clearanceMonth, double cycleDays)
    {
        if (profile?.IsPerennial != true) return [clearanceMonth];
        var firstHarvest = Math.Max(plantingMonth, MonthOf(planted.AddDays((int)Math.Round(cycleDays))));
        var months = new List<int>();
        for (var month = firstHarvest; month <= clearanceMonth; month += 12) months.Add(month);
        return months.Count > 0 ? [.. months] : [clearanceMonth];
    }

    /// <summary>Water the root zone holds for the crop between rains, mm, from the farmer's reading of how dry the field runs.</summary>
    internal static double SoilBufferMm(string? droughtLevel) => droughtLevel switch
    {
        FieldConditions.DroughtProne => 40,
        FieldConditions.DroughtLow => 110,
        _ => 75,
    };

    /// <summary>USDA SCS monthly effective rainfall: the part of a month's rain that reaches the roots.</summary>
    internal static double EffectiveRain(double rainMm) => rainMm > 75 ? 0.8 * rainMm - 25 : Math.Max(0, 0.6 * rainMm - 10);

    private static double DaysSincePlanting(DateOnly planted, int month)
    {
        var middle = new DateOnly(month / 12, month % 12 + 1, 15);
        return Math.Max(0, middle.DayNumber - planted.DayNumber);
    }

    private static long? Price(IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> prices, long material, int year) =>
        prices.GetValueOrDefault(material) is { Count: > 0 } rates
            ? rates.TryGetValue(year, out var exact) ? exact : rates.MinBy(rate => (Math.Abs(rate.Key - year), rate.Key)).Value
            : null;

    /// <summary>
    /// The twelve months of a field's climate normals: rain summed from the daily baseline, nights and days averaged, and
    /// FAO-56 Hargreaves reference evapotranspiration from the temperatures and the sun at the field's latitude.
    /// </summary>
    public static AuditMonthClimate[] MonthlyClimate(DailyClimateBaseline baseline, double latitude) =>
        Enumerable.Range(1, 12).Select(month =>
        {
            var days = Enumerable.Range(1, DateTime.DaysInMonth(2025, month)).Select(day => new DateOnly(2025, month, day)).ToArray();
            var normals = days.Select(baseline.For).ToArray();
            var et0 = days.Zip(normals).Sum(pair => Hargreaves(pair.Second, latitude, pair.First.DayOfYear));
            return new AuditMonthClimate(Math.Round(normals.Sum(day => day.PrecipitationMm), 1), Math.Round(et0, 1),
                Math.Round(normals.Average(day => day.MinTempC), 1), Math.Round(normals.Average(day => day.MaxTempC), 1));
        }).ToArray();

    internal static double Hargreaves(DailyClimateNormal day, double latitude, int dayOfYear)
    {
        var phi = latitude * Math.PI / 180;
        var dr = 1 + 0.033 * Math.Cos(2 * Math.PI * dayOfYear / 365);
        var delta = 0.409 * Math.Sin(2 * Math.PI * dayOfYear / 365 - 1.39);
        var ws = Math.Acos(Math.Clamp(-Math.Tan(phi) * Math.Tan(delta), -1, 1));
        var ra = 24 * 60 / Math.PI * 0.0820 * dr * (ws * Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Sin(ws));
        var range = Math.Max(0, day.MaxTempC - day.MinTempC);
        return Math.Max(0, 0.0023 * 0.408 * ra * (day.MeanTempC + 17.8) * Math.Sqrt(range));
    }

    private static int Month(string value) =>
        int.Parse(value[..4], CultureInfo.InvariantCulture) * 12 + int.Parse(value[5..7], CultureInfo.InvariantCulture) - 1;

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static int MonthOf(DateOnly date) => date.Year * 12 + date.Month - 1;
    private static string Text(int month) => $"{month / 12:D4}-{month % 12 + 1:D2}";
}
