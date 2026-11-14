from __future__ import annotations

from collections import defaultdict
from typing import Final

from ortools.sat.python import cp_model
from ortools.sat.python.cp_model import IntVar

from app.models.planning import CroppingCanvas, PlantingVariant
from app.models.types import CropId
from app.solver.base import Component, ComponentPhase, VariableStore

MONTHS_IN_YEAR: Final[int] = 12


class CropRotationComponent(Component[None]):
    name: str = "crop_rotation"
    phase: ComponentPhase = ComponentPhase.CONSTRAINTS

    def apply(
        self,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: None = None,
    ) -> None:
        intervals = canvas.rotation_intervals
        if not intervals or not canvas.months:
            return

        clearance_vars: dict[CropId, dict[int, list[IntVar]]] = defaultdict(
            lambda: defaultdict(list),
        )
        planting_vars: dict[CropId, dict[int, list[IntVar]]] = defaultdict(
            lambda: defaultdict(list),
        )
        variants_by_crop: dict[CropId, list[PlantingVariant]] = defaultdict(list)

        for v in canvas.variants:
            var = var_store.get_variant_var(v)
            c_id = v.profile.crop.id
            c_idx = v.clearance_month.year * MONTHS_IN_YEAR + v.clearance_month.month
            p_idx = v.plant_month.year * MONTHS_IN_YEAR + v.plant_month.month

            clearance_vars[c_id][c_idx].append(var)
            planting_vars[c_id][p_idx].append(var)
            variants_by_crop[c_id].append(v)

        horizon_len = len(canvas.months)

        for crop_id, p_vars_by_month in planting_vars.items():
            self_gap = intervals.get(crop_id, {}).get(crop_id)
            if self_gap is None or int(self_gap) <= 0:
                continue

            gap_months = int(self_gap)
            crop_vars = variants_by_crop.get(crop_id, [])
            min_duration = min(len(v.occupied_months) for v in crop_vars) if crop_vars else 1

            cycle_len = min_duration + gap_months
            max_possible_plantings = (horizon_len + gap_months) // cycle_len

            all_crop_vars = {v for vars_list in p_vars_by_month.values() for v in vars_list}
            model.add(cp_model.LinearExpr.Sum(list(all_crop_vars)) <= max_possible_plantings)

        for succ_crop_id, pred_rules in intervals.items():
            succ_plants = planting_vars.get(succ_crop_id)
            if not succ_plants:
                continue

            for pred_crop_id, gap in pred_rules.items():
                gap_int = int(gap)
                if gap_int <= 0:
                    continue

                pred_clears = clearance_vars.get(pred_crop_id)
                if not pred_clears:
                    continue

                for c_idx, clr_vars_list in pred_clears.items():
                    c_vars = list(set(clr_vars_list))

                    for m in range(c_idx + 1, c_idx + gap_int + 1):
                        plt_vars = succ_plants.get(m, [])
                        if plt_vars:
                            p_vars = list(set(plt_vars))
                            model.add_at_most_one(c_vars + p_vars)
