from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass, replace

from app.models.planning import AgronomicField, Horizon, PlantingVariant, RotationIntervalsMap
from app.models.topology import MAX_NUTRIENT_CARRYOVER_KG_HA, SoilProperties
from app.models.types import Note, NoteCategory, NoteImportance
from app.models.year_month import YearMonth


@dataclass(slots=True)
class _SoilTracker:
    current_depletion_mm: float
    taw_mm: float | None
    stocks: dict[str, float]


def _audit_sequence(
    variants: list[PlantingVariant],
    rotation_intervals: RotationIntervalsMap,
    variant_risks: dict[int, list[Note]],
) -> None:
    for i in range(1, len(variants)):
        curr = variants[i]
        pred_rules = rotation_intervals.get(curr.profile.crop.id, {})
        for prev in variants[:i]:
            if curr.plant_month <= prev.clearance_month:
                variant_risks[i].append(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"Occupancy conflict: overlap with '{prev.profile.crop.scientific_name}' (clears {prev.clearance_month})",
                    )
                )
                continue

            months_passed = curr.plant_month - prev.clearance_month
            required_gap = pred_rules.get(prev.profile.crop.id)
            if required_gap is not None and months_passed < int(required_gap):
                variant_risks[i].append(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"Rotation gap: {months_passed}m passed vs {int(required_gap)}m required after '{prev.profile.crop.scientific_name}'",
                    )
                )


def _init_soil_tracker(soil: SoilProperties) -> _SoilTracker:
    taw_mm = (
        float(soil.hydraulics.total_available_water_mm.real)
        if soil.hydraulics is not None
        else None
    )
    current_depletion_mm = (
        float(soil.initial_depletion_mm.real) if soil.initial_depletion_mm is not None else 0.0
    )
    if taw_mm is not None:
        current_depletion_mm = max(0.0, min(taw_mm, current_depletion_mm))
    else:
        current_depletion_mm = max(0.0, current_depletion_mm)

    stocks: dict[str, float] = {
        "n": float(soil.nutrients.nitrogen.initial_kg_ha.real),
        "p": float(soil.nutrients.phosphorus.initial_kg_ha.real),
        "k": float(soil.nutrients.potassium.initial_kg_ha.real),
    }
    return _SoilTracker(
        current_depletion_mm=current_depletion_mm,
        taw_mm=taw_mm,
        stocks=stocks,
    )


def _check_planting_thresholds(
    variant: PlantingVariant,
    soil_tracker: _SoilTracker,
    variant_risks: dict[int, list[Note]],
    variant_idx: int,
) -> None:
    prof = variant.profile
    if prof.max_planting_depletion_mm is not None:
        max_dep = float(prof.max_planting_depletion_mm.real)
        if soil_tracker.current_depletion_mm > max_dep:
            variant_risks[variant_idx].append(
                Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.WARN,
                    f"Carryover drought: depletion {soil_tracker.current_depletion_mm:.0f}mm > max {max_dep:.0f}mm",
                )
            )

    if prof.min_planting_depletion_mm is not None:
        min_dep = float(prof.min_planting_depletion_mm.real)
        if soil_tracker.current_depletion_mm < min_dep:
            variant_risks[variant_idx].append(
                Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.WARN,
                    f"Carryover waterlogging: depletion {soil_tracker.current_depletion_mm:.0f}mm < min {min_dep:.0f}mm",
                )
            )

    threshold_checks = (
        ("n", prof.min_planting_n_kg_ha, "N"),
        ("p", prof.min_planting_p_kg_ha, "P"),
        ("k", prof.min_planting_k_kg_ha, "K"),
    )
    for elem_key, min_rate, symbol in threshold_checks:
        if min_rate is not None and elem_key in soil_tracker.stocks:
            required_kg = float(min_rate.real)
            current_stock = soil_tracker.stocks[elem_key]
            if current_stock < required_kg:
                variant_risks[variant_idx].append(
                    Note(
                        NoteCategory.AGRONOMIC,
                        NoteImportance.WARN,
                        f"Carryover {symbol} deficit: {current_stock:.1f}/{required_kg:.1f} kg/ha",
                    )
                )


def _advance_monthly_water(
    soil_tracker: _SoilTracker,
    ym: YearMonth,
    active_variants: list[PlantingVariant],
    fallow_water_balance: dict[YearMonth, float],
) -> None:
    if active_variants:
        for active_variant in active_variants:
            water_dyn = active_variant.monthly_water.get(ym)
            if water_dyn is not None:
                wb = float(water_dyn.net_balance_mm.real)
                soil_tracker.current_depletion_mm += wb
    else:
        soil_tracker.current_depletion_mm += fallow_water_balance.get(ym, 0.0)

    if soil_tracker.taw_mm is not None:
        soil_tracker.current_depletion_mm = max(
            0.0, min(soil_tracker.taw_mm, soil_tracker.current_depletion_mm)
        )


def _advance_monthly_nutrients(
    soil_tracker: _SoilTracker,
    ym: YearMonth,
    active_variants: list[PlantingVariant],
    fallow_nutrient_recovery: dict[tuple[str, YearMonth], float],
) -> None:
    if not active_variants:
        for elem in ("n", "p", "k"):
            soil_tracker.stocks[elem] += fallow_nutrient_recovery.get((elem, ym), 0.0)
    else:
        for active_variant in active_variants:
            nut_dyn = active_variant.monthly_nutrients.get(ym)
            if nut_dyn is not None:
                n_net = float(
                    nut_dyn.n_applied_kg_ha.real
                    + nut_dyn.n_fixed_kg_ha.real
                    - nut_dyn.n_removed_kg_ha.real
                )
                p_net = float(nut_dyn.p_applied_kg_ha.real - nut_dyn.p_removed_kg_ha.real)
                k_net = float(nut_dyn.k_applied_kg_ha.real - nut_dyn.k_removed_kg_ha.real)
                soil_tracker.stocks["n"] += n_net
                soil_tracker.stocks["p"] += p_net
                soil_tracker.stocks["k"] += k_net

    max_stock = float(MAX_NUTRIENT_CARRYOVER_KG_HA.real)
    for elem in soil_tracker.stocks:
        soil_tracker.stocks[elem] = max(0.0, min(max_stock, soil_tracker.stocks[elem]))


def evaluate_carryover_dynamics(
    agronomic_field: AgronomicField,
    horizon: Horizon,
    rotation_intervals: RotationIntervalsMap,
    variants: list[PlantingVariant],
) -> list[PlantingVariant]:
    if not variants:
        return []

    ordered_variants = sorted(
        variants,
        key=lambda item: (item.plant_date, item.clearance_date),
    )
    variant_risks: dict[int, list[Note]] = defaultdict(list)

    _audit_sequence(ordered_variants, rotation_intervals, variant_risks)

    soil_tracker = _init_soil_tracker(agronomic_field.field.soil)

    planting_by_month: dict[YearMonth, list[tuple[int, PlantingVariant]]] = defaultdict(list)
    active_by_month: dict[YearMonth, list[PlantingVariant]] = defaultdict(list)
    for idx, variant in enumerate(ordered_variants):
        planting_by_month[variant.plant_month].append((idx, variant))
        for ym in variant.occupied_months:
            active_by_month[ym].append(variant)

    for ym in horizon.months:
        for idx, variant in planting_by_month.get(ym, []):
            _check_planting_thresholds(variant, soil_tracker, variant_risks, idx)

        active_variants = active_by_month.get(ym, [])
        _advance_monthly_water(
            soil_tracker, ym, active_variants, agronomic_field.fallow_water_balance
        )
        _advance_monthly_nutrients(
            soil_tracker,
            ym,
            active_variants,
            agronomic_field.fallow_nutrient_recovery,
        )

    final_variants: list[PlantingVariant] = []
    for idx, variant in enumerate(ordered_variants):
        additional_risks = variant_risks.get(idx)
        if additional_risks:
            final_variants.append(replace(variant, notes=(*variant.notes, *additional_risks)))
        else:
            final_variants.append(variant)

    return final_variants
