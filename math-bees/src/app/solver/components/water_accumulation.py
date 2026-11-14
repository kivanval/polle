from __future__ import annotations

from collections import defaultdict
from typing import Final

from ortools.sat.python import cp_model
from ortools.sat.python.cp_model import IntVar

from app.models.planning import AgronomicField, CroppingCanvas, PlantingVariant
from app.models.types import FieldId
from app.models.year_month import YearMonth
from app.solver.base import Component, ComponentPhase, VariableStore

PRECISION_SCALE: Final[int] = 100
TAW_BUFFER_MULTIPLIER: Final[int] = 5
MAX_MONTHLY_OVERFLOW_MM: Final[int] = 50_000


def _build_monthly_transitions(
    model: cp_model.CpModel,
    var_store: VariableStore,
    agronomic_field: AgronomicField,
    field_variants: list[PlantingVariant],
    horizon_months: list[YearMonth],
    depletion_vars: dict[YearMonth, IntVar],
    taw_scaled: int,
    bound_limit: int,
) -> None:
    month_active_vars: dict[YearMonth, list[tuple[IntVar, int]]] = defaultdict(list)
    horizon_months_set = set(horizon_months)

    for variant in field_variants:
        var = var_store.get_variant_var(variant)
        for occ_ym in variant.occupied_months:
            if occ_ym in horizon_months_set:
                water_dyn = variant.monthly_water.get(occ_ym)
                net_bal = float(water_dyn.net_balance_mm.real) if water_dyn is not None else 0.0
                bal = round(net_bal * PRECISION_SCALE)
                month_active_vars[occ_ym].append((var, bal))

    end_horizon_ym = horizon_months[-1] + 1
    fallow_balances = agronomic_field.fallow_water_balance

    for i, ym in enumerate(horizon_months):
        current_depletion = depletion_vars[ym]
        next_depletion = (
            depletion_vars[horizon_months[i + 1]]
            if i + 1 < len(horizon_months)
            else depletion_vars[end_horizon_ym]
        )

        active = month_active_vars.get(ym, [])
        fallow_balance = round(fallow_balances.get(ym, 0.0) * PRECISION_SCALE)

        if not active:
            raw_next = current_depletion + fallow_balance
        else:
            vars_list = [v for v, _ in active]
            diff_coeffs = [bal - fallow_balance for _, bal in active]
            raw_next = (
                current_depletion
                + fallow_balance
                + cp_model.LinearExpr.WeightedSum(vars_list, diff_coeffs)
            )

        raw_next_var = model.new_int_var(
            -bound_limit * 2,
            bound_limit * 2,
            f"raw_dep_{agronomic_field.id}_{ym}",
        )
        model.add(raw_next_var == raw_next)

        temp_capped = model.new_int_var(
            -bound_limit * 2,
            taw_scaled,
            f"c_{agronomic_field.id}_{ym}",
        )
        model.add_min_equality(temp_capped, [raw_next_var, taw_scaled])
        model.add_max_equality(next_depletion, [temp_capped, 0])


def _apply_planting_thresholds(
    model: cp_model.CpModel,
    var_store: VariableStore,
    field_variants: list[PlantingVariant],
    depletion_vars: dict[YearMonth, IntVar],
) -> None:
    threshold_groups: dict[tuple[YearMonth, int | None, int | None], list[IntVar]] = defaultdict(
        list,
    )

    for variant in field_variants:
        var = var_store.get_variant_var(variant)
        ym = variant.plant_month

        min_entry = (
            round(variant.profile.min_planting_depletion_mm.real * PRECISION_SCALE)
            if variant.profile.min_planting_depletion_mm is not None
            else None
        )
        max_entry = (
            round(variant.profile.max_planting_depletion_mm.real * PRECISION_SCALE)
            if variant.profile.max_planting_depletion_mm is not None
            else None
        )

        if min_entry is not None or max_entry is not None:
            threshold_groups[(ym, min_entry, max_entry)].append(var)

    for (ym, min_entry, max_entry), vars_list in threshold_groups.items():
        current_depletion = depletion_vars[ym]
        unique_vars = list(set(vars_list))

        is_active_var = model.new_bool_var(f"req_active_water_{ym}_{min_entry}_{max_entry}")
        model.add_bool_or(unique_vars).OnlyEnforceIf(is_active_var)
        for v in unique_vars:
            model.add_implication(v, is_active_var)

        if max_entry is not None:
            model.add(current_depletion <= max_entry).OnlyEnforceIf(is_active_var)
        if min_entry is not None:
            model.add(current_depletion >= min_entry).OnlyEnforceIf(is_active_var)


def _setup_depletion_variables(
    model: cp_model.CpModel,
    var_store: VariableStore,
    field_id: FieldId,
    horizon_months: list[YearMonth],
    taw_scaled: int,
    initial_depletion_mm: float,
) -> dict[YearMonth, IntVar]:
    depletion: dict[YearMonth, IntVar] = {
        ym: var_store.create_depletion_var(model, field_id, ym, taw_scaled) for ym in horizon_months
    }

    first_month = horizon_months[0]
    raw_init = initial_depletion_mm * PRECISION_SCALE
    init_val = max(0, min(taw_scaled, round(raw_init)))
    model.add(depletion[first_month] == init_val)

    end_horizon_ym = horizon_months[-1] + 1
    depletion[end_horizon_ym] = var_store.create_depletion_var(
        model,
        field_id,
        end_horizon_ym,
        taw_scaled,
    )

    return depletion


def _apply_for_field(
    model: cp_model.CpModel,
    agronomic_field: AgronomicField,
    field_variants: list[PlantingVariant],
    horizon_months: list[YearMonth],
    var_store: VariableStore,
) -> None:
    soil = agronomic_field.field.soil
    if soil is None or soil.hydraulics is None:
        return

    taw_scaled = round(soil.hydraulics.total_available_water_mm * PRECISION_SCALE)
    bound_limit = max(
        taw_scaled * TAW_BUFFER_MULTIPLIER,
        MAX_MONTHLY_OVERFLOW_MM * PRECISION_SCALE,
    )

    depletion_vars = _setup_depletion_variables(
        model=model,
        var_store=var_store,
        field_id=agronomic_field.id,
        horizon_months=horizon_months,
        taw_scaled=taw_scaled,
        initial_depletion_mm=(
            float(soil.initial_depletion_mm.real) if soil.initial_depletion_mm is not None else 0.0
        ),
    )

    _apply_planting_thresholds(
        model=model,
        var_store=var_store,
        field_variants=field_variants,
        depletion_vars=depletion_vars,
    )

    _build_monthly_transitions(
        model=model,
        var_store=var_store,
        agronomic_field=agronomic_field,
        field_variants=field_variants,
        horizon_months=horizon_months,
        depletion_vars=depletion_vars,
        taw_scaled=taw_scaled,
        bound_limit=bound_limit,
    )


class WaterAccumulationComponent(Component[None]):
    name: str = "water_accumulation"
    phase: ComponentPhase = ComponentPhase.CONSTRAINTS

    def apply(
        self,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: None = None,
    ) -> None:
        if not canvas.months or not canvas.field or not canvas.variants:
            return

        _apply_for_field(
            model=model,
            agronomic_field=canvas.field,
            field_variants=canvas.variants,
            horizon_months=canvas.months,
            var_store=var_store,
        )
