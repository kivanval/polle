from __future__ import annotations

from collections import defaultdict
from typing import Final

from ortools.sat.python import cp_model
from ortools.sat.python.cp_model import IntVar

from app.models.planning import CroppingCanvas
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
        if not intervals:
            return

        clearance_vars: dict[CropId, dict[int, list[IntVar]]] = defaultdict(
            lambda: defaultdict(list),
        )
        planting_vars: dict[CropId, dict[int, list[IntVar]]] = defaultdict(
            lambda: defaultdict(list),
        )

        for v in canvas.variants:
            var = var_store.get_variant_var(v)
            c_id = v.profile.crop.id
            c_idx = v.clearance_month.year * MONTHS_IN_YEAR + v.clearance_month.month
            p_idx = v.plant_month.year * MONTHS_IN_YEAR + v.plant_month.month

            clearance_vars[c_id][c_idx].append(var)
            planting_vars[c_id][p_idx].append(var)

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
                    conflicting_plants: list[IntVar] = []
                    for m in range(c_idx + 1, c_idx + gap_int + 1):
                        plt_vars = succ_plants.get(m, [])
                        conflicting_plants.extend(plt_vars)

                    if conflicting_plants:
                        for clr_var in clr_vars_list:
                            for plt_var in conflicting_plants:
                                if plt_var != clr_var:
                                    model.AddImplication(clr_var, plt_var.Not())
