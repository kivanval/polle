using Polle.Api.Features.Agriculture;
using Polle.Api.Features.Climate;
using Polle.Api.Features.Solver;
using Polle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Polle.Api.Features.Strategies;

/// <summary>
/// Reads what <see cref="PlanAudit"/> needs for one field: its settings and plantings, the catalog rows of their crop
/// profiles, prices, operating costs and rotation rules, and the field's climate normals. Each catalog lookup stands
/// alone: one that fails is logged and left empty, so the audit still answers with what it has (and says what's missing).
/// </summary>
public static class PlanAuditData
{
    public static async Task<StrategyResult?> AuditAsync(AppDbContext db, NasaPowerDailyClimate? climate, Guid fieldId, string startMonth, string endMonth,
        CancellationToken ct, ILogger? log = null)
    {
        var field = await db.Fields.AsNoTracking().Where(item => item.Id == fieldId)
            .Select(item => new { item.AreaHectares, item.DroughtLevel, item.InitialNKg, item.InitialPKg, item.InitialKKg, item.InitialDepletionMm,
                item.Boundary, Point = item.Boundary.InteriorPoint })
            .SingleOrDefaultAsync(ct);
        if (field is null) return null;
        var rows = await db.FieldPlantings.AsNoTracking().Where(planting => planting.FieldId == fieldId)
            .Select(planting => new { planting.CropProfileId, planting.PlantingDate, planting.HarvestingDate, planting.IrrigationMm,
                planting.FertilizerMaterialId, planting.FertilizerRateKgHa })
            .ToListAsync(ct);
        if (rows.Count == 0) return null;
        // The products the farmer chose, with their label content; one that can't be read leaves those crops unfertilized.
        var fertilizerIds = rows.Select(row => row.FertilizerMaterialId).OfType<long>().Distinct().ToArray();
        var fertilizers = await OrEmptyAsync("fertilizers", async () => (await FertilizerCatalog.ListAsync(db, fertilizerIds, ct)).ToDictionary(item => item.Id),
            new Dictionary<long, FertilizerResponse>(), log, ct);
        var plantings = rows.Select(row => new AuditPlanting(row.CropProfileId, row.PlantingDate, row.HarvestingDate, row.IrrigationMm ?? 0,
            row is { FertilizerMaterialId: { } product, FertilizerRateKgHa: > 0 } && fertilizers.GetValueOrDefault(product) is { } label
                ? new AuditFertilizer(product, row.FertilizerRateKgHa.Value, label.NitrogenPercent, label.PhosphatePercent, label.PotashPercent, label.Name) : null)).ToList();

        var profileIds = plantings.Select(planting => planting.CropProfileId).Distinct().ToArray();
        var profiles = await ProfilesAsync(db, profileIds, ct, log);
        var materialIds = profiles.Values.SelectMany(profile => new[] { profile.ProductId, profile.PlantingMaterialId }).OfType<long>()
            .Concat(fertilizerIds).Distinct().ToArray();
        var cropIds = profiles.Values.Select(profile => profile.CropId).Distinct().ToArray();
        var prices = await OrEmptyAsync("prices", async () => (await db.Database
                .SqlQuery<PriceRow>($"SELECT material_id::bigint AS material_id, year::int AS year, price_cents::bigint AS cents FROM public.material_prices WHERE material_id = ANY({materialIds})")
                .ToListAsync(ct)).GroupBy(row => row.MaterialId)
            .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<int, long>)group.ToDictionary(row => row.Year, row => row.Cents)),
            new Dictionary<long, IReadOnlyDictionary<int, long>>(), log, ct);
        var opex = await OrEmptyAsync("operating costs", async () => (await db.Database
                .SqlQuery<OpexRow>($"SELECT crop_profile_id::bigint AS profile_id, year::int AS year, opex_cents::bigint AS cents FROM public.crop_profile_opex WHERE crop_profile_id = ANY({profileIds})")
                .ToListAsync(ct)).GroupBy(row => row.ProfileId)
            .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<int, long>)group.ToDictionary(row => row.Year, row => row.Cents)),
            new Dictionary<long, IReadOnlyDictionary<int, long>>(), log, ct);
        var rotation = await OrEmptyAsync("rotation rules", async () => (await db.Database
                .SqlQuery<RotationRow>($"SELECT crop_id::bigint AS crop_id, predecessor_crop_id::bigint AS predecessor_crop_id, min_months_gap::int AS min_months_gap FROM public.crop_rotation_rules WHERE crop_id = ANY({cropIds}) AND predecessor_crop_id = ANY({cropIds})")
                .ToListAsync(ct)).ToDictionary(row => (row.CropId, row.PredecessorCropId), row => row.MinMonthsGap),
            new Dictionary<(long Crop, long Predecessor), int>(), log, ct);

        AuditMonthClimate[]? normals = null;
        if (climate is not null && SolverRequestBuilder.HasValidFieldPoint(field.Boundary, field.Point))
        {
            try { normals = PlanAudit.MonthlyClimate(await climate.GetAsync(field.Point!.Y, field.Point.X, ct), field.Point.Y); }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                log?.LogWarning(ex, "Climate normals for the plan audit of field {FieldId} are unavailable", fieldId);
            }
        }

        return PlanAudit.Build(new PlanAuditInput(startMonth, endMonth,
            new AuditField(field.AreaHectares, field.DroughtLevel, field.InitialNKg, field.InitialPKg, field.InitialKKg, field.InitialDepletionMm),
            plantings, profiles, prices, opex, rotation, normals));
    }

    private static async Task<Dictionary<long, AuditProfile>> ProfilesAsync(AppDbContext db, long[] ids, CancellationToken ct, ILogger? log)
    {
        var names = await db.CropProfiles.AsNoTracking().Where(profile => ids.Contains(profile.Id))
            .Select(profile => new { profile.Id, profile.CropId, profile.Crop.Name, profile.SystemName, profile.IsPerennial })
            .ToListAsync(ct);
        var figures = await OrEmptyAsync("crop profile figures", async () => (await db.Database.SqlQuery<ProfileRow>($"""
                SELECT id::bigint AS profile_id, potential_yield_tons_per_ha::float8 AS yield_tons, planting_rate_kg_per_ha::float8 AS planting_rate,
                       product_id::bigint AS product_id, planting_material_id::bigint AS planting_material_id,
                       n_removal_kg_per_unit::float8 AS n_removal, p_removal_kg_per_unit::float8 AS p_removal, k_removal_kg_per_unit::float8 AS k_removal,
                       n_fixation_kg_per_ha::float8 AS n_fixation
                FROM public.crop_profiles WHERE id = ANY({ids})
                """).ToListAsync(ct)).ToDictionary(row => row.ProfileId), new Dictionary<long, ProfileRow>(), log, ct);
        var stages = await OrEmptyAsync("crop stages", async () => (await db.Database.SqlQuery<StageRow>($"""
                SELECT crop_profile_id::bigint AS profile_id, sequence_order::int AS sequence_order, category::text AS category,
                       coalesce(min_days_since_planting, 0)::int AS min_days, coalesce(max_days_since_planting, 0)::int AS max_days,
                       coalesce(water_crop_coefficient, 0.8)::float8 AS kc, coalesce(water_yield_response_factor, 1)::float8 AS ky,
                       absolute_min_temp_c::float8 AS min_temp, absolute_max_temp_c::float8 AS max_temp
                FROM public.crop_profile_stages WHERE crop_profile_id = ANY({ids})
                """).ToListAsync(ct)).GroupBy(row => row.ProfileId)
            .ToDictionary(group => group.Key, group => group.OrderBy(row => row.SequenceOrder).ToArray()), new Dictionary<long, StageRow[]>(), log, ct);

        return names.ToDictionary(item => item.Id, item =>
        {
            var row = figures.GetValueOrDefault(item.Id);
            var removal = row is { NRemoval: { } n, PRemoval: { } p, KRemoval: { } k } ? (n, p, k) : ((double, double, double)?)null;
            return new AuditProfile(item.Id, item.CropId, item.Name, item.SystemName, item.IsPerennial, row?.YieldTons, row?.PlantingRate, row?.ProductId,
                row?.PlantingMaterialId, removal, row?.NFixation ?? 0,
                (stages.GetValueOrDefault(item.Id) ?? []).Select(stage => new AuditStage(stage.Category, stage.MinDays, stage.MaxDays, stage.Kc, stage.Ky,
                    stage.MinTemp, stage.MaxTemp)).ToArray());
        });
    }

    private static async Task<T> OrEmptyAsync<T>(string what, Func<Task<T>> query, T empty, ILogger? log, CancellationToken ct)
    {
        try { return await query(); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log?.LogError(ex, "Looking up {What} for the plan audit failed; the audit goes on without them", what);
            return empty;
        }
    }

    // Columns named as the snake_case convention maps these properties, or EF can't read them.
    private sealed record PriceRow(long MaterialId, int Year, long Cents);
    private sealed record OpexRow(long ProfileId, int Year, long Cents);
    private sealed record RotationRow(long CropId, long PredecessorCropId, int MinMonthsGap);
    private sealed record ProfileRow(long ProfileId, double? YieldTons, double? PlantingRate, long? ProductId, long? PlantingMaterialId,
        double? NRemoval, double? PRemoval, double? KRemoval, double? NFixation);
    private sealed record StageRow(long ProfileId, int SequenceOrder, string Category, int MinDays, int MaxDays, double Kc, double Ky, double? MinTemp, double? MaxTemp);
}
