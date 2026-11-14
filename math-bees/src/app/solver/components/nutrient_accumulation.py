from __future__ import annotations

from collections import defaultdict
from typing import Final

from ortools.sat.python import cp_model
from ortools.sat.python.cp_model import IntVar

from app.models.planning import CroppingCanvas, PlantingVariant
from app.models.topology import NutrientPool
from app.models.year_month import YearMonth
from app.solver.base import Component, ComponentPhase, VariableStore

PRECISION_SCALE: Final[int] = 100
RAW_NUTRIENT_BUFFER_SCALED: Final[int] = 5_000_000


def _get_net_nutrient_delta(variant: PlantingVariant, elem: str, ym: YearMonth) -> float:
    dyn = variant.monthly_nutrients.get(ym)
    if dyn is not None:
        if elem == "n":
            return float(
                dyn.n_applied_kg_ha.real + dyn.n_fixed_kg_ha.real - dyn.n_removed_kg_ha.real
            )
        if elem == "p":
            return float(dyn.p_applied_kg_ha.real - dyn.p_removed_kg_ha.real)
        if elem == "k":
            return float(dyn.k_applied_kg_ha.real - dyn.k_removed_kg_ha.real)
    elif variant.nutrient_balance is not None and ym == variant.clearance_month:
        if elem == "n":
            return float(variant.nutrient_balance.net_n_kg_ha.real)
        if elem == "p":
            return float(variant.nutrient_balance.net_p_kg_ha.real)
        if elem == "k":
            return float(variant.nutrient_balance.net_k_kg_ha.real)
    return 0.0


def _apply_planting_thresholds(
    model: cp_model.CpModel,
    var_store: VariableStore,
    field_variants: list[PlantingVariant],
    stocks: dict[str, dict[YearMonth, IntVar]],
) -> None:
    threshold_groups: dict[str, dict[tuple[YearMonth, int | None, int | None], list[IntVar]]] = {
        "n": defaultdict(list),
        "p": defaultdict(list),
        "k": defaultdict(list),
    }

    for variant in field_variants:
        var = var_store.get_variant_var(variant)
        profile = variant.profile
        ym = variant.plant_month

        reqs = (
            ("n", profile.min_planting_n_kg_ha, profile.max_planting_n_kg_ha),
            ("p", profile.min_planting_p_kg_ha, profile.max_planting_p_kg_ha),
            ("k", profile.min_planting_k_kg_ha, profile.max_planting_k_kg_ha),
        )

        for elem, min_req, max_req in reqs:
            min_scaled = (
                round(float(min_req.real) * PRECISION_SCALE) if min_req is not None else None
            )
            max_scaled = (
                round(float(max_req.real) * PRECISION_SCALE) if max_req is not None else None
            )

            if min_scaled is not None or max_scaled is not None:
                threshold_groups[elem][(ym, min_scaled, max_scaled)].append(var)

    for elem, groups in threshold_groups.items():
        for (ym, min_scaled, max_scaled), vars_list in groups.items():
            stock_var = stocks[elem][ym]
            unique_vars = list(set(vars_list))

            for indicator in unique_vars:
                if min_scaled is not None:
                    model.add(stock_var >= min_scaled).OnlyEnforceIf(indicator)
                if max_scaled is not None:
                    model.add(stock_var <= max_scaled).OnlyEnforceIf(indicator)


class NutrientAccumulationComponent(Component[None]):
    name: str = "nutrient_accumulation"
    phase: ComponentPhase = ComponentPhase.CONSTRAINTS

    def apply(
        self,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: None = None,
    ) -> None:
        horizon_months = canvas.months
        if not horizon_months or not canvas.field or not canvas.variants:
            return

        agronomic_field = canvas.field
        base_field = agronomic_field.field
        soil = base_field.soil

        if soil is None or soil.nutrients is None:
            return

        f_id = base_field.id
        field_variants = canvas.variants
        end_horizon_ym = horizon_months[-1] + 1

        month_active_vars: dict[str, dict[YearMonth, list[tuple[IntVar, int]]]] = {
            "n": defaultdict(list),
            "p": defaultdict(list),
            "k": defaultdict(list),
        }

        for variant in field_variants:
            var = var_store.get_variant_var(variant)
            for occ_ym in variant.occupied_months:
                for elem in ("n", "p", "k"):
                    net_kg = _get_net_nutrient_delta(variant, elem, occ_ym)
                    bal_scaled = round(net_kg * PRECISION_SCALE)
                    month_active_vars[elem][occ_ym].append((var, bal_scaled))

        pools: tuple[tuple[str, NutrientPool], ...] = (
            ("n", soil.nutrients.nitrogen),
            ("p", soil.nutrients.phosphorus),
            ("k", soil.nutrients.potassium),
        )

        stocks: dict[str, dict[YearMonth, IntVar]] = {}

        for elem_name, pool in pools:
            min_scaled = round(float(pool.min_kg_ha.real) * PRECISION_SCALE)
            max_scaled = round(float(pool.max_kg_ha.real) * PRECISION_SCALE)
            init_scaled = round(float(pool.initial_kg_ha.real) * PRECISION_SCALE)

            elem_stock: dict[YearMonth, IntVar] = {}
            for ym in horizon_months:
                elem_stock[ym] = var_store.create_stock_var(
                    model,
                    f_id,
                    elem_name,
                    ym,
                    min_scaled,
                    max_scaled,
                )

            end_var = var_store.create_stock_var(
                model,
                f_id,
                elem_name,
                end_horizon_ym,
                min_scaled,
                max_scaled,
            )
            elem_stock[end_horizon_ym] = end_var

            first_month = horizon_months[0]
            clamped_init = max(min_scaled, min(max_scaled, init_scaled))
            model.add(elem_stock[first_month] == clamped_init)

            stocks[elem_name] = elem_stock

        _apply_planting_thresholds(
            model=model,
            var_store=var_store,
            field_variants=field_variants,
            stocks=stocks,
        )

        for elem_name, pool in pools:
            min_scaled = round(float(pool.min_kg_ha.real) * PRECISION_SCALE)
            max_scaled = round(float(pool.max_kg_ha.real) * PRECISION_SCALE)
            elem_stock = stocks[elem_name]
            elem_active = month_active_vars[elem_name]

            for i, ym in enumerate(horizon_months):
                current_stock = elem_stock[ym]
                next_stock = (
                    elem_stock[horizon_months[i + 1]]
                    if i + 1 < len(horizon_months)
                    else elem_stock[end_horizon_ym]
                )

                recovery_kg = agronomic_field.fallow_nutrient_recovery.get((elem_name, ym), 0.0)
                fallow_delta = round(recovery_kg * PRECISION_SCALE)

                active = elem_active.get(ym, [])

                if not active:
                    raw_next = current_stock + fallow_delta
                else:
                    vars_list = [v for v, _ in active]
                    diff_coeffs = [bal - fallow_delta for _, bal in active]
                    raw_next = (
                        current_stock
                        + fallow_delta
                        + cp_model.LinearExpr.WeightedSum(vars_list, diff_coeffs)
                    )

                bound_limit = max_scaled + RAW_NUTRIENT_BUFFER_SCALED
                raw_next_var = model.new_int_var(
                    -bound_limit,
                    bound_limit,
                    f"raw_nut_{f_id}_{elem_name}_{ym}",
                )
                model.add(raw_next_var == raw_next)

                temp_nut_capped = model.new_int_var(
                    -bound_limit,
                    max_scaled,
                    f"c_nut_{f_id}_{elem_name}_{ym}",
                )
                model.add_min_equality(temp_nut_capped, [raw_next_var, max_scaled])
                model.add_max_equality(next_stock, [temp_nut_capped, min_scaled])
