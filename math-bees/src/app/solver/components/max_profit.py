from __future__ import annotations

from typing import TYPE_CHECKING

from ortools.sat.python import cp_model

from app.solver.base import Component, ComponentPhase, VariableStore

if TYPE_CHECKING:
    from app.models.planning import CroppingCanvas


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
        coeffs = []

        for variant in canvas.variants:
            revenue_cents = variant.expected_revenue.real
            if revenue_cents > 0:
                var = var_store.get_variant_var(variant)
                vars_list.append(var)
                coeffs.append(revenue_cents)

        if vars_list:
            model.maximize(cp_model.LinearExpr.weighted_sum(vars_list, coeffs))
