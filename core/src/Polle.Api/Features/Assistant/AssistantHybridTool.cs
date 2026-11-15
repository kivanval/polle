using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Polle.Api.Features.Drought;
using Polle.Api.Features.Strategies;
using Polle.Domain.Agriculture;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Assistant;

/// <summary>
/// The assistant's hybrid tool: suggests hybrids and varieties from public.crop_hybrids for the crops named in the
/// message, or for the crops of a field's plan (planted, just suggested or saved: see <see cref="ChoosePlanProfiles"/>). With a field it reads the next twelve months of the climate trend
/// model at the field, and when the field looks dry or hot it lists drought- and heat-tolerant hybrids first. The answer is
/// built here, not by the model, so it names only hybrids that are in the database. Ownership is checked against the session user.
/// </summary>
public sealed partial class AssistantHybridTool(AppDbContext db, ClimateTrendStore trends, ILogger<AssistantHybridTool> log)
{
    private const int MaxPerCrop = 4;

    public async Task<AssistantTurnResult> RecommendAsync(ClaimsPrincipal user, string message, AssistantPageContext? page,
        AssistantConversationContext? previous, bool uk, DateOnly today, CancellationToken ct)
    {
        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("A valid session is required.");
        var hybrids = await db.CropHybrids.AsNoTracking().Include(hybrid => hybrid.Crop)
            .OrderBy(hybrid => hybrid.CropId).ThenBy(hybrid => hybrid.Id).ToListAsync(ct);
        var named = CropsNamed(message, hybrids.Select(hybrid => hybrid.Crop).DistinctBy(crop => crop.Id).ToArray());

        var fields = await db.Fields.AsNoTracking().Where(field => field.Farm.OwnerUserId == userId)
            .OrderBy(field => field.Name).ThenBy(field => field.Id)
            .Select(field => new AssistantStrategyField(field.Id, field.FarmId, field.Farm.Name, field.Name, field.AreaHectares, field.ParentFieldId))
            .ToListAsync(ct);
        var (status, field, choices) = AssistantStrategyTool.PickField(message, fields, page?.FarmId ?? previous?.FarmId, page?.FieldId, previous?.FieldId);
        // A crop named on its own ("sunflower hybrids?") needs no field; a guessed field would only add noise. A field still
        // counts when the message names it or the page or conversation has one open.
        var explicitField = field is not null && (named.Length == 0 || Mentions(message, field.Name) || field.Id == page?.FieldId || field.Id == previous?.FieldId);
        if (named.Length == 0 && field is null)
            return new(status == "ambiguous"
                ? uk ? $"Для якого поля або культури підібрати гібриди: {string.Join(", ", choices)}? Можна також назвати культуру, наприклад «гібриди соняшнику»."
                    : $"Which field or crop should I suggest hybrids for: {string.Join(", ", choices)}? You can also name a crop, e.g. \"sunflower hybrids\"."
                : Catalogue(hybrids, uk));
        if (!explicitField) field = null;

        Crop[] crops = named;
        PlanCropSource? source = null;
        if (named.Length == 0)
        {
            (source, crops) = await PlanCropsAsync(field!.Id, userId, previous?.RunId, PlanAsked(message), today, ct);
            if (crops.Length == 0) return new(uk
                ? $"У плані поля «{field.Name}» ще немає культур. Додайте посів або назвіть культуру, наприклад «гібриди кукурудзи для посухи»."
                : $"{field.Name}'s plan has no crops yet. Add a planting, or name a crop, e.g. \"drought-tolerant maize hybrids\".");
        }

        var dryness = field is null ? null : await DrynessAsync(field.Id, today, ct);
        var droughtAsked = DroughtRegex().IsMatch(message.ToLowerInvariant());
        var text = Text(crops, hybrids, field?.Name, dryness, droughtAsked, uk, source);
        log.LogInformation("Assistant suggested hybrids for {CropCount} crops{Field} from {Source}", crops.Length, field is null ? "" : " of a field",
            source?.ToString() ?? "the message");
        return new(text);
    }

    /// <summary>
    /// The crops of the field's plan, in the order <see cref="ChoosePlanProfiles"/> sets. The run comes from the
    /// conversation, so it counts only when it belongs to this field and to the session user.
    /// </summary>
    private async Task<(PlanCropSource? Source, Crop[] Crops)> PlanCropsAsync(Guid fieldId, Guid userId, Guid? runId, bool planAsked, DateOnly today,
        CancellationToken ct)
    {
        var plantings = await db.FieldPlantings.AsNoTracking()
            .Where(planting => planting.FieldId == fieldId && planting.Field.Farm.OwnerUserId == userId)
            .Select(planting => new { planting.SeasonYear, planting.PlantingDate, planting.HarvestingDate, planting.CropProfileId })
            .ToListAsync(ct);
        long[] current = [.. plantings.Where(planting => planting.PlantingDate >= today || planting.HarvestingDate is null || planting.HarvestingDate >= today)
            .OrderBy(planting => planting.PlantingDate).Select(planting => planting.CropProfileId)];
        var latest = plantings.Count == 0 ? 0 : plantings.Max(planting => planting.SeasonYear);
        long[] lastSeason = [.. plantings.Where(planting => planting.SeasonYear == latest)
            .OrderBy(planting => planting.PlantingDate).Select(planting => planting.CropProfileId)];

        // Suggestions and saved plans are read only when the field has nothing planted from today on, or the farmer asked about a plan.
        string?[] suggested = [];
        string? saved = null;
        if (current.Length == 0 || planAsked)
        {
            if (runId is { } run)
                suggested = [.. await db.StrategyRuns.AsNoTracking()
                    .Where(candidate => candidate.Id == run && candidate.FieldId == fieldId && candidate.Field.Farm.OwnerUserId == userId)
                    .SelectMany(candidate => candidate.Jobs)
                    .Where(job => job.State == StrategyJobs.Ready && job.Key != StrategyCatalog.BaselineKey)
                    .OrderBy(job => job.Key).Select(job => job.ResultJson).ToListAsync(ct)];
            saved = await db.FieldStrategies.AsNoTracking()
                .Where(strategy => strategy.FieldId == fieldId && strategy.Field.Farm.OwnerUserId == userId)
                .OrderByDescending(strategy => strategy.SavedAtUtc).Select(strategy => strategy.ResultJson).FirstOrDefaultAsync(ct);
        }

        if (ChoosePlanProfiles(current, suggested, saved, lastSeason, planAsked) is not { } choice) return (null, []);
        var profiles = await db.CropProfiles.AsNoTracking().Where(profile => choice.ProfileIds.Contains(profile.Id))
            .Select(profile => new { profile.Id, profile.Crop }).ToDictionaryAsync(profile => profile.Id, profile => profile.Crop, ct);
        // A suggestion can outlive a crop removed from the catalog; its profile is skipped.
        Crop[] crops = [.. choice.ProfileIds.Select(id => profiles.GetValueOrDefault(id)).OfType<Crop>().DistinctBy(crop => crop.Id)];
        return (crops.Length == 0 ? null : choice.Source, crops);
    }

    /// <summary>
    /// Which plan the hybrids follow when the message names no crop, first match wins: plantings still growing or sown
    /// from today on; the ready suggestions of the run the conversation just read or started (all their crops, the
    /// farmer's own plan left out); the plan last saved for the field; the latest past season, so a field planned only
    /// in the past still gets an answer. When the message asks about a plan ("hybrids for this plan"), the suggestions
    /// and the saved plan come before the plantings. Returns crop profile ids in plan order, or null when there is nothing.
    /// </summary>
    public static PlanCropChoice? ChoosePlanProfiles(IReadOnlyList<long> current, IReadOnlyList<string?> suggested, string? saved, IReadOnlyList<long> lastSeason,
        bool planAsked = false)
    {
        if (!planAsked && current.Count > 0) return new(PlanCropSource.CurrentPlan, [.. current.Distinct()]);
        if (ResultProfiles(suggested) is { Length: > 0 } fromSuggestions) return new(PlanCropSource.SuggestedPlans, fromSuggestions);
        if (ResultProfiles([saved]) is { Length: > 0 } fromSaved) return new(PlanCropSource.SavedPlan, fromSaved);
        if (current.Count > 0) return new(PlanCropSource.CurrentPlan, [.. current.Distinct()]);
        return lastSeason.Count > 0 ? new(PlanCropSource.LastSeason, [.. lastSeason.Distinct()]) : null;
    }

    /// <summary>Whether the message talks about a plan or strategy, in English or Ukrainian.</summary>
    public static bool PlanAsked(string message) => PlanRegex().IsMatch(message.ToLowerInvariant());

    // Read through the same mapper the plan cards use; an unreadable result simply adds no crops.
    private static long[] ResultProfiles(IEnumerable<string?> results) =>
        [.. results.SelectMany(json => StrategyResultMapper.Map(json, CropProfileNames.Empty)?.Cycles ?? [])
            .Select(cycle => cycle.CropProfileId).Where(id => id > 0).Distinct()];

    private async Task<FieldDryness?> DrynessAsync(Guid fieldId, DateOnly today, CancellationToken ct)
    {
        var centre = await db.Fields.AsNoTracking().Where(field => field.Id == fieldId).Select(field => field.Boundary.Centroid).SingleAsync(ct);
        try
        {
            var model = await trends.GetModelAsync(ct);
            if (model.CellRow(centre.Y, centre.X) is not { } row) return null;
            var first = model.MonthIndex(today.ToString("yyyy-MM", CultureInfo.InvariantCulture)) ?? Math.Max(0, model.MonthCount - 12);
            var months = Enumerable.Range(first, Math.Min(12, model.MonthCount - first)).Select(month => model.Cell(month, row)).ToArray();
            return FieldDryness.From(months);
        }
        catch (DroughtDataUnavailableException)
        {
            // Without the climate model the hybrids are still listed, only not ranked for drought.
            log.LogWarning("Climate trend model unavailable; hybrids are listed without the field's drought signal");
            return null;
        }
    }

    /// <summary>
    /// The answer, in the small Markdown the chat renders: a heading, which plan the crops come from, the field's climate,
    /// then each crop's hybrids.
    /// </summary>
    /// <param name="source">The plan the crops were taken from; null when the message named them.</param>
    public static string Text(IReadOnlyList<Crop> crops, IReadOnlyList<CropHybrid> hybrids, string? fieldName, FieldDryness? dryness, bool droughtAsked, bool uk,
        PlanCropSource? source = null)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append('\n');
        var prioritise = droughtAsked || dryness?.Level is CropHybrid.High or CropHybrid.Medium;
        var hot = dryness?.Hot == true;

        Line(fieldName is not null ? source switch
            {
                PlanCropSource.SuggestedPlans => uk ? $"### Гібриди для запропонованого плану ({fieldName})" : $"### Hybrids for suggested plan ({fieldName})",
                PlanCropSource.SavedPlan => uk ? $"### Гібриди для збереженого плану ({fieldName})" : $"### Hybrids for saved plan ({fieldName})",
                _ => uk ? $"### Гібриди для поля «{fieldName}»" : $"### Hybrids for {fieldName}",
            }
            : crops.Count == 1 ? uk ? $"### Гібриди: {CropName(crops[0], uk)}" : $"### {CropName(crops[0], uk)} hybrids"
            : uk ? "### Гібриди" : "### Hybrids");
        if (source is { } from) Line(from switch
        {
            PlanCropSource.CurrentPlan => uk ? "*За поточним планом поля.*" : "*Based on your current field plan.*",
            PlanCropSource.SuggestedPlans => uk ? "*За запропонованими планами.*" : "*Based on the suggested plans.*",
            PlanCropSource.SavedPlan => uk ? "*За збереженим планом.*" : "*Based on your saved plan.*",
            _ => uk ? "*За останнім сезоном поля.*" : "*Based on the field's last season.*",
        });
        if (dryness is not null)
        {
            var level = uk ? dryness.Level switch { CropHybrid.High => "високий", CropHybrid.Medium => "помірний", _ => "низький" } : dryness.Level;
            var balance = Math.Round(dryness.WarmBalanceMm).ToString("N0", CultureInfo.InvariantCulture).Replace("-", "−", StringComparison.Ordinal);
            Line(uk
                ? $"**Клімат поля на 12 місяців:** ризик посухи {level}; місяців з помірною посухою чи гірше: {dryness.DroughtMonths}; водний баланс теплих місяців {balance} мм."
                : $"**Field climate, next 12 months:** {level} drought risk; {dryness.DroughtMonths} {(dryness.DroughtMonths == 1 ? "month" : "months")} of moderate drought or worse; warm-season water balance {balance} mm.");
        }
        if (prioritise) Line(uk ? $"*Спершу посухостійкі гібриди{(hot ? ", далі — жаростійкі" : "")}.*"
            : $"*Drought-tolerant hybrids come first{(hot ? ", then heat-tolerant ones" : "")}.*");

        foreach (var crop in crops)
        {
            Line();
            Line($"**{CropName(crop, uk)}**");
            var ofCrop = Rank(hybrids.Where(hybrid => hybrid.CropId == crop.Id), prioritise, hot).Take(MaxPerCrop).ToArray();
            if (ofCrop.Length == 0)
            {
                Line(uk ? "- Гібридів для цієї культури в базі поки немає." : "- No hybrids recorded for this crop yet.");
                continue;
            }
            foreach (var hybrid in ofCrop)
            {
                Line(uk
                    ? $"- **{hybrid.Name}** · {hybrid.MaturityGroup} · посухостійкість: {Tolerance(hybrid.DroughtTolerance, uk)} · жаростійкість: {Tolerance(hybrid.HeatTolerance, uk)}"
                    : $"- **{hybrid.Name}** · {hybrid.MaturityGroup} · drought tolerance: {hybrid.DroughtTolerance} · heat tolerance: {hybrid.HeatTolerance}");
                Line($"  - {hybrid.KeyTraits}");
                Line(uk ? $"  - Умови: {hybrid.RecommendedConditions}" : $"  - Suits: {hybrid.RecommendedConditions}");
            }
        }
        Line();
        Line(uk
            ? "*Оцінки — від селекціонерів і залежать від регіону та сезону. Перед купівлею насіння перевірте локальну адаптацію з агрономом або дилером.*"
            : "*Ratings come from the breeders and vary by region and season. Check local adaptation with your agronomist or seed dealer before you buy.*");
        return text.ToString().TrimEnd();
    }

    /// <summary>Hybrids in catalog order, or with drought tolerance first (then heat when the field is hot) when it matters.</summary>
    public static IEnumerable<CropHybrid> Rank(IEnumerable<CropHybrid> hybrids, bool prioritise, bool hot) => !prioritise ? hybrids
        : hybrids.OrderByDescending(hybrid => Score(hybrid.DroughtTolerance)).ThenByDescending(hybrid => hot ? Score(hybrid.HeatTolerance) : 0);

    /// <summary>
    /// Catalog crops the message names, by their English or Ukrainian name or a common word for them ("corn", "соняшнику").
    /// </summary>
    public static Crop[] CropsNamed(string message, IReadOnlyList<Crop> crops)
    {
        var text = message.ToLowerInvariant();
        return [.. crops.Where(crop =>
            (crop.ScientificName is { } latin && Aliases.TryGetValue(latin, out var pattern) && Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
            || Mentions(text, crop.Name) || (crop.NameUk is { } uk && Mentions(text, uk)))];
    }

    // Stems in both languages, so inflected Ukrainian ("кукурудзи", "пшениці") and US/UK names ("corn", "maize") match.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Zea mays"] = @"\b(?:maize|corn)\b|кукурудз",
        ["Helianthus annuus"] = @"\bsunflowers?\b|соняшник",
        ["Glycine max"] = @"\bsoy(?:a|beans?)?\b|\bсо[їяю]\b",
        ["Triticum aestivum"] = @"\bwheat\b|пшениц",
        ["Sorghum bicolor"] = @"\bsorghum\b|сорго",
        ["Hordeum vulgare"] = @"\bbarley\b|ячм[еі]н",
        ["Brassica napus"] = @"\b(?:rapeseed|canola|oilseed rape)\b|ріпак",
        ["Solanum tuberosum"] = @"\bpotato(?:es)?\b|картопл",
        ["Pisum sativum"] = @"\bpeas?\b|горох",
    };

    private static bool Mentions(string text, string name)
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

    // With nothing to go on, the answer says which crops have hybrids, so the next question can name one.
    private static string Catalogue(IReadOnlyList<CropHybrid> hybrids, bool uk)
    {
        var names = hybrids.Select(hybrid => hybrid.Crop).DistinctBy(crop => crop.Id).Select(crop => CropName(crop, uk)).ToArray();
        if (names.Length == 0) return uk ? "У базі ще немає гібридів." : "There are no hybrids in the database yet.";
        return uk
            ? $"Назвіть культуру або відкрийте поле, і я підберу гібриди. Є гібриди для: {string.Join(", ", names)}."
            : $"Name a crop or open a field and I'll suggest hybrids. I have hybrids for: {string.Join(", ", names)}.";
    }

    private static string CropName(Crop crop, bool uk) => uk ? crop.NameUk ?? crop.Name : crop.Name;

    private static string Tolerance(string value, bool uk) => !uk ? value
        : value switch { CropHybrid.High => "висока", CropHybrid.Medium => "середня", _ => "низька" };

    private static int Score(string value) => value switch { CropHybrid.High => 2, CropHybrid.Medium => 1, _ => 0 };

    [GeneratedRegex(@"посух|засух|посушл|спек|жар[уиа]|drought|dry|heat|hot\b|arid")]
    private static partial Regex DroughtRegex();

    // "план", "плану", "планів"…, but not "плантація"; "стратег" covers every form of стратегія.
    [GeneratedRegex(@"\b(?:plans?|strateg(?:y|ies))\b|\bплан(?:и|у|а|і|ів|ом|ах|ами)?\b|стратег")]
    private static partial Regex PlanRegex();
}

/// <summary>The plan a field's hybrid answer follows when the message names no crop; the answer says which.</summary>
public enum PlanCropSource { CurrentPlan, SuggestedPlans, SavedPlan, LastSeason }

/// <param name="ProfileIds">public.crop_profiles ids in plan order, without repeats.</param>
public sealed record PlanCropChoice(PlanCropSource Source, long[] ProfileIds);

/// <summary>
/// How dry a field's next months look in the climate trend model: months of moderate drought or worse (D1+), the
/// rain-minus-evaporation balance of the warm months (mean 10 °C and above), whether the cell is arid, and whether any
/// month's mean daily maximum reaches 30 °C.
/// </summary>
public sealed record FieldDryness(int DroughtMonths, double WarmBalanceMm, bool Arid, bool Hot, string Level)
{
    public static FieldDryness From(IReadOnlyList<TrendCell> months)
    {
        var droughtMonths = months.Count(month => month.DroughtClass >= 1);
        var balance = months.Where(month => month.TmeanC >= 10).Sum(month => month.WaterBalance30dMm ?? 0);
        var arid = months.Any(month => month.Status == "arid");
        var hot = months.Any(month => month.TmaxC >= 30);
        var level = arid || droughtMonths >= 3 || balance <= -300 ? CropHybrid.High
            : droughtMonths >= 1 || balance <= -150 ? CropHybrid.Medium : CropHybrid.Low;
        return new(droughtMonths, balance, arid, hot, level);
    }
}
