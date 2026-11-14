from __future__ import annotations

import logging
from typing import TYPE_CHECKING, Any

from ortools.sat.python import cp_model

from app.solver.base import Component, VariableStore
from app.solver.plan import ComponentConfig, FormulatedPlan, SolverPlan

if TYPE_CHECKING:
    from collections.abc import Iterable

    from app.models.planning import CroppingCanvas

logger = logging.getLogger(__name__)


class ComponentRegistry:
    def __init__(self, components: Iterable[Component[Any]]) -> None:
        self._components: dict[str, Component[Any]] = {comp.name: comp for comp in components}

    def get(self, name: str) -> Component[Any] | None:
        return self._components.get(name)

    def __contains__(self, name: str) -> bool:
        return name in self._components


class Formulator:
    def __init__(self, registry: ComponentRegistry) -> None:
        self._registry = registry

    def formulate(
        self,
        canvas: CroppingCanvas,
        plan: SolverPlan,
    ) -> FormulatedPlan:
        model = cp_model.CpModel()
        var_store = VariableStore()

        logger.info("Phase 1: Registering variables...")
        for cfg in plan.variables:
            self._apply_component(cfg, model, canvas, var_store)

        logger.info("Phase 2: Applying constraints...")
        for cfg in plan.constraints:
            self._apply_component(cfg, model, canvas, var_store)

        logger.info("Phase 3: Setting objective '%s'...", plan.objective.name)
        self._apply_component(plan.objective, model, canvas, var_store)

        logger.info("Model formulation completed successfully.")
        return FormulatedPlan(model=model, var_store=var_store, plan=plan)

    def _apply_component(
        self,
        cfg: ComponentConfig,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
    ) -> None:
        component = self._registry.get(cfg.name)
        if not component:
            logger.warning("No handler found for component: '%s'", cfg.name)
            return

        logger.info("Executing component '%s' at phase %s", component.name, component.phase.name)
        component.apply(model=model, canvas=canvas, var_store=var_store, payload=cfg.payload)