using System.Globalization;
using System.Text.Json;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Polle.Api.Features.Strategies;

/// <summary>
/// Looks up the crop names behind the profile ids, the fertilizer names behind the material ids, and the profiles'
/// operating costs, in solver results.
/// </summary>
public static class StrategyProfiles
{
    /// <param name="fieldId">The field the results are for: its area prices the operating costs. Without it they are left out.</param>
    /// <remarks>
    /// Each lookup stands alone: one that fails (a catalog table changed in the database) is logged and left empty, so a
    /// broken fertilizer or OpEx lookup never costs the cards their crop names.
    /// </remarks>
    public static async Task<CropProfileNames> LoadAsync(AppDbContext db, IEnumerable<string?> resultJsons, CancellationToken ct, Guid? fieldId = null,
        ILogger? log = null)
    {
        var results = resultJsons.ToArray();
        var ids = results.SelectMany(ProfileIds).Distinct().ToArray();
        var materialIds = results.SelectMany(FertilizerIds).Distinct().ToArray();
        if (ids.Length == 0 && materialIds.Length == 0) return CropProfileNames.Empty;
        var names = ids.Length == 0 ? new Dictionary<long, CropProfileName>() : await OrEmptyAsync("crop names", () => db.CropProfiles.AsNoTracking()
            .Where(profile => ids.Contains(profile.Id))
            .Select(profile => new { profile.Id, profile.CropId, profile.Crop.Name, profile.SystemName })
            .ToDictionaryAsync(profile => profile.Id, profile => new CropProfileName(profile.CropId, profile.Name, profile.SystemName), ct),
            new Dictionary<long, CropProfileName>(), log, ct);
        var materials = materialIds.Length == 0 ? new Dictionary<long, string>() : await OrEmptyAsync("fertilizer names", () => db.Database
            // name is jsonb ({"en": …, "uk": …}): the English name, like the rest of the catalog.
            .SqlQuery<MaterialName>($"SELECT id AS \"Id\", coalesce(name->>'en', name #>> '{{}}', id::text) AS \"Name\" FROM public.materials WHERE id = ANY({materialIds})")
            .ToDictionaryAsync(material => material.Id, material => material.Name, ct), new Dictionary<long, string>(), log, ct);
        if (fieldId is not { } field || ids.Length == 0) return new CropProfileNames(names, materials);
        var area = await OrEmptyAsync("field area", () => db.Fields.AsNoTracking().Where(candidate => candidate.Id == field)
            .Select(candidate => (double?)candidate.AreaHectares).SingleOrDefaultAsync(ct), null, log, ct);
        var opex = await OrEmptyAsync("operating costs", () => OpexAsync(db, ids, ct), new Dictionary<long, IReadOnlyDictionary<int, long>>(), log, ct);
        return new CropProfileNames(names, materials, opex, area);
    }

    private static async Task<T> OrEmptyAsync<T>(string what, Func<Task<T>> query, T empty, ILogger? log, CancellationToken ct)
    {
        try { return await query(); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.LogError(ex, "Looking up {What} for strategy cards failed; the cards go on without them", what);
            return empty;
        }
    }

    private sealed record MaterialName(long Id, string Name);
    private sealed record OpexRate(long ProfileId, int Year, long Cents);

    /// <summary>
    /// US cents per hectare per season of each profile, by year. Empty while public.crop_profile_opex doesn't exist
    /// (backend/crop-profile-opex-import.sql creates it), so the cards show felder's costs without operating costs.
    /// </summary>
    private static async Task<IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>>> OpexAsync(AppDbContext db, long[] profileIds, CancellationToken ct)
    {
        try
        {
            var rows = await db.Database.SqlQuery<OpexRate>(
                    // Columns named as the snake_case convention maps OpexRate's properties (profile_id, year, cents), or EF can't read them.
                    $"SELECT crop_profile_id AS profile_id, year, opex_cents::bigint AS cents FROM public.crop_profile_opex WHERE crop_profile_id = ANY({profileIds})")
                .ToListAsync(ct);
            return rows.GroupBy(row => row.ProfileId)
                .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<int, long>)group.ToDictionary(row => row.Year, row => row.Cents));
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return new Dictionary<long, IReadOnlyDictionary<int, long>>();
        }
    }

    internal static IEnumerable<long> ProfileIds(string? resultJson) => CycleIds(resultJson, "crop_profile_id");

    /// <summary>The fertilizer product of each cycle: fertilizer_application.material_id (felder 2.4), else fertilizer_material_id (2.3).</summary>
    internal static IEnumerable<long> FertilizerIds(string? resultJson) =>
        CycleIds(resultJson, "fertilizer_application", "material_id").Concat(CycleIds(resultJson, "fertilizer_material_id"));

    private static IEnumerable<long> CycleIds(string? resultJson, params string[] path)
    {
        // A result that isn't a JSON object (an HTML page from a proxy, null, an array) has no crops to name; it must
        // not throw, or every poll of the run fails and no card ever shows.
        if (string.IsNullOrEmpty(resultJson) || Parse(resultJson) is not { } json) yield break;
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("schedule", out var schedule) || schedule.ValueKind != JsonValueKind.Array) yield break;
            foreach (var cycle in schedule.EnumerateArray())
            {
                var id = cycle;
                foreach (var property in path)
                    if (id.ValueKind != JsonValueKind.Object || !id.TryGetProperty(property, out id)) { id = default; break; }
                if (id.ValueKind is JsonValueKind.String or JsonValueKind.Number && long.TryParse(id.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                    yield return value;
            }
        }
    }

    private static JsonDocument? Parse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { return null; }
    }
}
