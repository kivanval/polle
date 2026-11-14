from __future__ import annotations

import logging
from collections.abc import Iterable
from typing import Any

from ortools.sat.python import cp_model

from app.models.planning import CroppingCanvas
from app.solver.base import Component, ComponentPhase, VariableStore

logger = logging.getLogger(__name__)


class ComponentRegistry:
    def __init__(self, components: Iterable[Component[Any]]) -> None:
        self._components: list[Component[Any]] = list(components)

    @property
    def all(self) -> list[Component[Any]]:
        return self._components


class Formulator:
    def __init__(self, registry: ComponentRegistry) -> None:
        self._registry = registry

    def formulate(self, canvas: CroppingCanvas) -> tuple[cp_model.CpModel, VariableStore]:
        model = cp_model.CpModel()
        var_store = VariableStore()

        components = self._registry.all

        logger.info("Phase 1: Registering variables...")
        for comp in [c for c in components if c.phase == ComponentPhase.VARIABLES]:
            comp.apply(model=model, canvas=canvas, var_store=var_store, payload=None)

        logger.info("Phase 2: Applying constraints...")
        for comp in [c for c in components if c.phase == ComponentPhase.CONSTRAINTS]:
            comp.apply(model=model, canvas=canvas, var_store=var_store, payload=None)

        logger.info("Phase 3: Setting objective...")
        for comp in [c for c in components if c.phase == ComponentPhase.OBJECTIVE]:
            comp.apply(model=model, canvas=canvas, var_store=var_store, payload=None)

        logger.info("Model formulation completed successfully.")
        return model, var_store
