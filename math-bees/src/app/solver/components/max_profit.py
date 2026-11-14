from __future__ import annotations

from ortools.sat.python import cp_model

from app.models.planning import CroppingCanvas
from app.solver.base import Component, ComponentPhase, VariableStore


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
        objective_terms = []

        for variant in canvas.variants:
            profit_cents = int(variant.expected_revenue.real)
            if profit_cents != 0:
                var = var_store.get_variant_var(variant)
                objective_terms.append(profit_cents * var)

        if not canvas.risk.allow_soil_depletion:
            end_ym = canvas.months[-1] + 1
            area_ha = float(canvas.field.field.area_ha.real)

            element_salvage_cents_per_kg = {
                "n": 35,
                "p": 45,
                "k": 25,
            }

            for elem, price_per_kg in element_salvage_cents_per_kg.items():
                end_stock_var = var_store.get_stock_var(canvas.field.field.id, elem, end_ym)
                if end_stock_var is not None:
                    raw_coeff = (price_per_kg * area_ha) / 100.0
                    coeff = max(1, round(raw_coeff)) if raw_coeff > 0 else 0
                    if coeff > 0:
                        objective_terms.append(coeff * end_stock_var)

        if objective_terms:
            model.maximize(cp_model.LinearExpr.sum(objective_terms))
