from __future__ import annotations

from collections import defaultdict
from typing import TYPE_CHECKING

from app.solver.base import Component, ComponentPhase, VariableStore

if TYPE_CHECKING:
    from ortools.sat.python.cp_model import CpModel, IntVar

    from app.models.planning import CroppingCanvas
    from app.models.year_month import YearMonth


class FieldOccupancyComponent(Component[None]):
    name: str = "field_occupancy"
    phase: ComponentPhase = ComponentPhase.VARIABLES

    def apply(
        self,
        model: CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: None = None,
    ) -> None:
        horizon_months = set(canvas.months)
        variants_by_month: dict[YearMonth, list[IntVar]] = defaultdict(list)

        for variant in canvas.variants:
            var = var_store.create_variant_var(model, variant)
            for ym in variant.occupied_months:
                if ym in horizon_months:
                    variants_by_month[ym].append(var)

        for active_vars in variants_by_month.values():
            if len(active_vars) > 1:
                model.add_at_most_one(active_vars)
