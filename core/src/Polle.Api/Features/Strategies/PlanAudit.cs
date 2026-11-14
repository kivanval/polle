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

/// <summary>The field's own settings the farmer recorded: soil reading, starting nutrients and moisture.</summary>
public sealed record AuditField(double AreaHectares, string? DroughtLevel, double? InitialNKg, double? InitialPKg, double? InitialKKg, double? InitialDepletionMm);

/// <param name="IrrigationMm">Irrigation the farmer plans over the season, spread evenly over its months; 0 is rain-fed.</param>
/// <param name="Fertilizer">The fertilizer the farmer plans for the season; null for none.</param>
public sealed record AuditPlanting(long CropProfileId, DateOnly PlantingDate, DateOnly? HarvestingDate, double IrrigationMm = 0,
    AuditFertilizer? Fertilizer = null);

/// <summary>A planting's fertilizer: the product, kilograms of it per hectare and its label content (N, P₂O₅, K₂O, percent).</summary>
public sealed record AuditFertilizer(long MaterialId, double RateKgHa, double NitrogenPercent, double PhosphatePercent, double PotashPercent, string? Name = null);

/// <param name="Plantings">Every planting of the field: those sown in the horizon are audited, earlier ones count as predecessors for rotation.</param>
/// <param name="Prices">US cents per tonne by material id, then year (public.material_prices).</param>
/// <param name="OpexCentsPerHa">US cents per hectare per season by profile id, then year (public.crop_profile_opex).</param>
/// <param name="RotationGaps">Minimum months between a predecessor crop and a crop (public.crop_rotation_rules), keyed (crop, predecessor).</param>
/// <param name="Climate">Twelve months, January first; null when the climate baseline couldn't be read.</param>
public sealed record PlanAuditInput(string StartMonth, string EndMonth, AuditField Field, IReadOnlyList<AuditPlanting> Plantings,
    IReadOnlyDictionary<long, AuditProfile> Profiles, IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> Prices,
    IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> OpexCentsPerHa, IReadOnlyDictionary<(long Crop, long Predecessor), int> RotationGaps,
    IReadOnlyList<AuditMonthClimate>? Climate);

/// <summary>
/// An agronomic and financial audit of the farmer's own plan, worked out here from the same catalog the solver reads (crop
/// profiles and stages, prices, operating costs, rotation rules), the field's ten-year climate normals and its recorded soil
/// settings. It stands in for the solver's evaluation when that comes back without figures (missing crop data, a plan that
/// breaks the limits as entered), so "Your current plan" always has its summary, risks, water balance and N-P-K, and it
/// fills in the crops an evaluation left out (<see cref="Complete"/>).
/// The plan is audited with the water and fertilizer the farmer recorded for each crop (none: rain-fed and unfertilized);
/// what the crops still need on top of that is reported.
/// </summary>
public static class PlanAudit
{
    /// <summary>Status of an audited (not solver-scored) result.</summary>
    public const string EstimatedStatus = "ESTIMATED";

    /// <summary>Category of the warning on a crop the solver left out of its evaluation (<see cref="Complete"/>).</summary>
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
        // Soil nutrients carried from crop to crop; null when the farmer has no soil test (the balance is still reported).
        double? soilN = input.Field.InitialNKg, soilP = input.Field.InitialPKg, soilK = input.Field.InitialKKg;
        var cycles = new List<StrategyCycle>();
        foreach (var planting in audited)
        {
            var profile = input.Profiles.GetValueOrDefault(planting.CropProfileId);
            cycles.Add(Cycle(input, planting, profile, all, area, ref soilN, ref soilP, ref soilK));
        }

        var notes = cycles.SelectMany(cycle => cycle.NoteDetails).ToArray();
        string[] Messages(Func<CycleNote, bool> keep) => notes.Where(keep).Select(note => note.Message).Distinct().ToArray();
        var revenue = cycles.Sum(cycle => cycle.RevenueUsd);
        var costs = cycles.Sum(cycle => cycle.CostsUsd);
        var operating = cycles.Any(cycle => cycle.OperatingCostsUsd is not null) ? cycles.Sum(cycle => cycle.OperatingCostsUsd ?? 0) : (decimal?)null;
        return new StrategyResult(EstimatedStatus, revenue - costs, revenue, costs, 0,
            Math.Round(cycles.Sum(cycle => cycle.Nutrients.NitrogenAppliedKgHa + cycle.Nutrients.NitrogenFixedKgHa - cycle.Nutrients.NitrogenRemovedKgHa), 1),
            [.. cycles], Messages(note => note.Importance == StrategyResultMapper.Risk), Messages(note => note.Category == "data_missing"),
            Messages(note => note.Importance == StrategyResultMapper.Warning),
            input.Climate is null ? null : Math.Round(cycles.Sum(cycle => cycle.MonthlyWater.Sum(month => month.WaterUseMm)), 1),
            operating, CostBreakdown.Sum(cycles.Select(cycle => cycle.CostBreakdown)));
    }

    /// <summary>
    /// The solver's evaluation of the farmer's plan with the crops it left out filled in from this audit. felder before
    /// 2026-11-13 dropped a crop it couldn't simulate (missing figures, a profile it doesn't know, a sowing month the crop
    /// can't survive) and still reported the rest as FEASIBLE; since then it returns every crop with its own notes, so this
    /// mostly fills in results stored before. Each crop filled in here is marked Estimated and carries a warning saying so. Crops are
    /// matched on profile and planting month first, then on profile alone. The scored result comes back unchanged when no
    /// crop is missing.
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
            if (!Take(cycle, other => other.CropProfileId == cycle.CropProfileId)) missing.Add(Unscored(cycle));
        scored = WithEstimatedWater(scored, twins);
        if (missing.Count == 0) return scored;

        var cycles = scored.Cycles.Concat(missing).OrderBy(cycle => cycle.PlantingDate ?? cycle.PlantingMonth, StringComparer.Ordinal).ToArray();
        string[] Add(string[] existing, string importance) =>
            existing.Concat(missing.SelectMany(cycle => cycle.NoteDetails).Where(note => note.Importance == importance).Select(note => note.Message)).Distinct().ToArray();
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
            Risks = Add(scored.Risks, StrategyResultMapper.Risk),
            DataGaps = scored.DataGaps.Concat(missing.SelectMany(cycle => cycle.NoteDetails).Where(note => note.Category == "data_missing")
                .Select(note => note.Message)).Distinct().ToArray(),
            Warnings = Add(scored.Warnings, StrategyResultMapper.Warning),
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

    /// <summary>An audited crop standing in for one the solver couldn't score: marked Estimated, with a warning first.</summary>
    private static StrategyCycle Unscored(StrategyCycle cycle)
    {
        var note = new CycleNote($"Estimated: the planner couldn't score the {cycle.CropName} sown {(FelderDates.Parse(cycle.PlantingDate) is { } sown ? $"on {sown.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}" : $"in {Words(Month(cycle.PlantingMonth))}")}, so its figures "
            + "come from the crop catalog, the field's climate and your soil settings (with the water and fertilizer you entered for it).", StrategyResultMapper.Warning, UnscoredCategory);
        return cycle with { Warnings = [note.Message, .. cycle.Warnings], NoteDetails = [note, .. cycle.NoteDetails], Estimated = true };
    }

    private static StrategyCycle Cycle(PlanAuditInput input, AuditPlanting planting, AuditProfile? profile, AuditPlanting[] all, double area,
        ref double? soilN, ref double? soilP, ref double? soilK)
    {
        var notes = new List<CycleNote>();
        void Add(string importance, string category, string message) => notes.Add(new CycleNote(message, importance, category));
        var name = profile?.CropName ?? "Unknown crop";
        var plantingMonth = MonthOf(planting.PlantingDate);
        var stages = StageEnds(profile);
        var cycleDays = stages.Count > 0 ? stages[^1].End : DefaultCycleDays;
        var clearanceDate = planting.HarvestingDate
            ?? (profile?.IsPerennial == true ? DateOnly.ParseExact($"{input.EndMonth}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : planting.PlantingDate.AddDays((int)Math.Round(cycleDays)));
        var clearanceMonth = Math.Max(plantingMonth, MonthOf(clearanceDate));
        var harvests = HarvestMonths(profile, planting.PlantingDate, plantingMonth, clearanceMonth, cycleDays);
        if (profile is null) Add(StrategyResultMapper.Warning, "data_missing", "This crop profile isn't in the catalog, so its figures can't be estimated.");

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
        else Add(StrategyResultMapper.Warning, "data_missing", "The field's climate normals couldn't be read, so the water balance is left out.");

        // Yield from the FAO-33 water response: Ya/Ym = 1 − Ky (1 − ETa/ETc).
        var ky = need > 0 ? weightedKy / need : 1;
        var waterShare = need > 0 ? Math.Clamp((need - deficit) / need, 0, 1) : 1;
        var yieldFactor = Math.Clamp(1 - (ky <= 0 ? 1 : ky) * (1 - waterShare), 0, 1);
        var irrigated = irrigation > 0 ? $"With the {Mm(irrigation)} of irrigation in the plan, it is still {Mm(deficit)} short" : "The plan has no irrigation";
        if (need > 0 && 1 - waterShare >= 0.25)
            Add(StrategyResultMapper.Risk, "drought", $"Drought: {WaterBalance(name, plantingMonth, clearanceMonth, need, deficit, irrigation)} {irrigated}, so expect about {Percent(1 - yieldFactor)} less harvest.");
        else if (need > 0 && 1 - waterShare >= 0.08)
            Add(StrategyResultMapper.Warning, "drought", $"Moisture deficit: {WaterBalance(name, plantingMonth, clearanceMonth, need, deficit, irrigation)} {irrigated}, so the harvest may be about {Percent(1 - yieldFactor)} smaller.");
        if (input.Field.DroughtLevel == FieldConditions.DroughtProne && need > 0)
            Add(StrategyResultMapper.Warning, "drought", "You marked this field as drought-prone: its soil holds little water between rains.");

        // Sowing month against the frost and heat the first stage survives.
        if (input.Climate is { Count: 12 } normalsByMonth && profile?.Stages is { Count: > 0 } profileStages)
        {
            var sowing = normalsByMonth[plantingMonth % 12];
            var firstStage = profileStages[0];
            if (firstStage.AbsoluteMinTempC is { } coldest && sowing.MinTempC < coldest)
                Add(StrategyResultMapper.Risk, "sowing", $"Sowing month: nights in {Words(plantingMonth)} average {sowing.MinTempC:0.#} °C, colder than the {coldest:0.#} °C {name} seedlings survive.");
            if (firstStage.AbsoluteMaxTempC is { } hottest && sowing.MaxTempC > hottest)
                Add(StrategyResultMapper.Risk, "sowing", $"Sowing month: days in {Words(plantingMonth)} average {sowing.MaxTempC:0.#} °C, hotter than the {hottest:0.#} °C {name} seedlings stand.");
        }

        // Rotation: the crop sown too soon after itself or a crop that shares its diseases, or on a field still taken.
        if (profile is not null)
            foreach (var earlier in all.TakeWhile(other => !ReferenceEquals(other, planting)))
            {
                var previous = input.Profiles.GetValueOrDefault(earlier.CropProfileId);
                if (previous is null) continue;
                var previousEnd = earlier.HarvestingDate ?? earlier.PlantingDate.AddDays((int)Math.Round(StageEnds(previous) is { Count: > 0 } ends ? ends[^1].End : DefaultCycleDays));
                if (previousEnd >= planting.PlantingDate && earlier.HarvestingDate is not null)
                    Add(StrategyResultMapper.Risk, "rotation", $"Overlap: the {name} is sown on {planting.PlantingDate:yyyy-MM-dd}, before the {previous.CropName} is cleared on {previousEnd:yyyy-MM-dd}.");
                if (!input.RotationGaps.TryGetValue((profile.CropId, previous.CropId), out var minGap)) continue;
                var gap = plantingMonth - MonthOf(previousEnd);
                if (gap < minGap)
                    Add(StrategyResultMapper.Risk, "rotation", previous.CropId == profile.CropId
                        ? $"Crop rotation: {name} comes back after {Math.Max(gap, 0)} months. Leave at least {minGap} months before growing it again."
                        : $"Crop rotation: {name} follows {previous.CropName} after {Math.Max(gap, 0)} months. They share pests and diseases, so leave at least {minGap} months.");
            }

        // Nutrients: what the harvests take off, against legume fixation; nothing is applied in the plan as scored.
        var tonsPerHa = (profile?.YieldTonsPerHa ?? 0) * yieldFactor;
        var harvestCount = Math.Max(1, harvests.Length);
        var removal = profile?.RemovalPerTon;
        var removedN = removal is { } r1 ? Math.Round(r1.N * tonsPerHa * harvestCount, 1) : 0;
        var removedP = removal is { } r2 ? Math.Round(r2.P2O5 * P2O5ToP * tonsPerHa * harvestCount, 1) : 0;
        var removedK = removal is { } r3 ? Math.Round(r3.K2O * K2OToK * tonsPerHa * harvestCount, 1) : 0;
        var fixedN = Math.Round((profile?.NitrogenFixedKgPerHa ?? 0) * (profile?.IsPerennial == true ? harvestCount : 1), 1);
        if (profile is not null && removal is null)
            Add(StrategyResultMapper.Warning, "data_missing", $"The catalog has no nutrient removal figures for {name}.");
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
            Add(StrategyResultMapper.Info, "nutrients", $"Fertilizer: the plan's {fertilizer.RateKgHa:0} kg/ha of fertilizer adds about {appliedN:0} kg N, {appliedP:0} kg P and {appliedK:0} kg K per hectare. "
                + $"The expected {name} harvest ({tonsPerHa * harvestCount:0.#} t/ha) takes off about {nitrogen}, {removedP:0} kg P and {removedK:0} kg K, "
                + (gaps.Length > 0 ? $"so each hectare is still short of about {string.Join(", ", gaps)}." : "so the fertilizer puts it all back.")
                + " These are pure nutrients, as in the N-P-K balance.");
        }
        else if (needN + removedP + removedK > 0)
        {
            // Pure N, P and K, the units of the crop's N-P-K balance, so the two can be read side by side.
            var nitrogen = fixedN > 0 ? $"{needN:0} kg N (after the {fixedN:0} kg N it fixes from the air)" : $"{needN:0} kg N";
            Add(StrategyResultMapper.Info, "nutrients", $"Fertilizer need: the plan adds no fertilizer. The expected {name} harvest "
                + $"({tonsPerHa * harvestCount:0.#} t/ha) takes nutrients off the field. To put them back, each hectare would need about {nitrogen}, "
                + $"{removedP:0} kg P and {removedK:0} kg K. These are pure nutrients, as in the N-P-K balance, from the crop catalog's removal per tonne of harvest.");
        }
        Deplete(ref soilN, appliedN + fixedN - removedN, "nitrogen", "N", name, notes);
        Deplete(ref soilP, appliedP - removedP, "phosphorus", "P", name, notes);
        Deplete(ref soilK, appliedK - removedK, "potassium", "K", name, notes);

        // Money over the whole field: product sold at the catalog price, seed and base operating costs.
        decimal revenue = 0, seed = 0;
        if (profile is not null && area > 0)
        {
            if (profile.ProductId is { } product && tonsPerHa > 0)
            {
                foreach (var harvest in harvests.DefaultIfEmpty(clearanceMonth))
                {
                    if (Price(input.Prices, product, harvest / 12) is { } cents) revenue += (decimal)(tonsPerHa * area) * cents / 100m;
                    else { Add(StrategyResultMapper.Warning, "data_missing", $"No market price for the {name} harvest, so its revenue is left out."); break; }
                }
            }
            else if (profile.YieldTonsPerHa is null) Add(StrategyResultMapper.Warning, "data_missing", $"The catalog has no expected yield for {name}, so its revenue is left out.");
            else if (profile.ProductId is null) Add(StrategyResultMapper.Warning, "data_missing", $"The catalog has no product to sell for {name}, so its revenue is left out.");
            if (profile.PlantingMaterialId is { } material && profile.PlantingRateKgPerHa is { } rate && rate > 0)
            {
                if (Price(input.Prices, material, plantingMonth / 12) is { } cents) seed = (decimal)(rate * area / 1000) * cents / 100m;
                else Add(StrategyResultMapper.Warning, "data_missing", $"No price for {name} seed, so its cost is left out.");
            }
        }
        decimal fertilizerCost = 0;
        if (fertilizer is not null && area > 0)
        {
            if (Price(input.Prices, fertilizer.MaterialId, plantingMonth / 12) is { } cents) fertilizerCost = Math.Round((decimal)(fertilizer.RateKgHa * area / 1000) * cents / 100m, 2);
            else Add(StrategyResultMapper.Warning, "data_missing", $"No price for the fertilizer on the {name}, so its cost is left out.");
        }
        decimal? operating = null;
        if (profile is not null && area > 0 && input.OpexCentsPerHa.GetValueOrDefault(profile.Id) is { Count: > 0 } rates)
        {
            var seasons = harvests.Select(month => month / 12).DefaultIfEmpty(clearanceMonth / 12).Distinct();
            operating = Math.Round(seasons.Sum(year => (decimal)(rates.TryGetValue(year, out var exact) ? exact : rates.MinBy(rate => Math.Abs(rate.Key - year)).Value)) * (decimal)area / 100m, 2);
        }
        revenue = Math.Round(revenue, 2);
        seed = Math.Round(seed, 2);
        var costs = seed + fertilizerCost + (operating ?? 0);
        string[] Of(string importance) => notes.Where(note => note.Importance == importance).Select(note => note.Message).Distinct().ToArray();
        return new StrategyCycle(profile?.Id ?? planting.CropProfileId, profile?.CropId ?? 0, name, profile?.SystemName ?? "", Text(plantingMonth), Text(clearanceMonth),
            harvests.Select(Text).ToArray(), revenue, costs, Math.Round(yieldFactor * 100, 1), Math.Round(irrigation, 1), Of(StrategyResultMapper.Info),
            new CycleNutrients(appliedN, fixedN, removedN, appliedP, removedP, appliedK, removedK), Of(StrategyResultMapper.Risk), Of(StrategyResultMapper.Warning),
            [.. monthly], [.. notes], fertilizer is null ? null : new CycleFertilizer(fertilizer.MaterialId.ToString(CultureInfo.InvariantCulture), fertilizer.Name, [], fertilizer.RateKgHa),
            operating, new CostBreakdown(operating ?? 0, seed, 0, fertilizerCost),
            PlantingDate: Day(planting.PlantingDate), ClearanceDate: planting.HarvestingDate is { } harvested ? Day(harvested) : null);
    }

    /// <summary>Carries the soil's stock of one nutrient on; a risk once a soil test's stock runs out.</summary>
    private static void Deplete(ref double? stock, double change, string nutrient, string symbol, string crop, List<CycleNote> notes)
    {
        if (stock is not { } before) return;
        var after = before + change;
        if (before >= 0 && after < 0)
            notes.Add(new CycleNote($"Soil depletion: by the end of the {crop}, the plan's harvests have taken {-after:0} kg/ha more {nutrient} ({symbol}) than your soil test recorded, and no fertilizer puts it back.",
                StrategyResultMapper.Risk, "soil_health"));
        stock = after;
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
    private static string Words(int month) => new DateOnly(month / 12, month % 12 + 1, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture);
    private static string Mm(double value) => $"{Math.Round(value):0} mm";

    /// <summary>
    /// What a crop needs in water against what an average year gives it, and where both come from: the need is the crop's
    /// evaporation (its stage coefficient × the field's reference evaporation), the supply is the field's ten-year average
    /// rain that reaches the roots plus the water the soil stores.
    /// </summary>
    private static string WaterBalance(string name, int from, int to, double need, double deficit, double irrigation) =>
        $"from {Words(from)} to {Words(to)} the {name} needs about {Mm(need)} of water. In an average year (the field's ten-year average weather), "
        + $"rain plus the water stored in the soil covers about {Mm(Math.Max(0, need - deficit - irrigation))}"
        + (irrigation > 0 ? "." : $", leaving it {Mm(deficit)} short.");
    private static string Percent(double share) => $"{Math.Round(share * 100):0}%";
}
