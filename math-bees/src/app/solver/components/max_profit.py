from __future__ import annotations

from typing import Final

from ortools.sat.python import cp_model

from app.models.planning import CroppingCanvas
from app.solver.base import Component, ComponentPhase, VariableStore

PROFIT_SCALING_FACTOR: Final[int] = 1000


class MaxProfitComponent(Component[None]):
    name: str = "max_profit"
    phase: ComponentPhase = ComponentPhase.OBJECTIVE

    def apply(
        self,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: None = None,
    ) -> None:
        vars_list = []
        coeffs_list = []

        for variant in canvas.variants:
            profit_cents = int(variant.expected_revenue.real)
            profit_scaled = profit_cents // PROFIT_SCALING_FACTOR

            if profit_scaled != 0:
                var = var_store.get_variant_var(variant)
                vars_list.append(var)
                coeffs_list.append(profit_scaled)

        if vars_list:
            model.maximize(cp_model.LinearExpr.WeightedSum(vars_list, coeffs_list))
