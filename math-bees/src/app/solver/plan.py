from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Generic, TypeVar

if TYPE_CHECKING:
    from ortools.sat.python import cp_model

    from app.solver.base import VariableStore

PayloadT = TypeVar("PayloadT")


@dataclass(frozen=True)
class ComponentConfig(Generic[PayloadT]):
    name: str
    payload: PayloadT | None = None


@dataclass(frozen=True)
class SolverPlan:
    variables: list[ComponentConfig]
    constraints: list[ComponentConfig]
    objective: ComponentConfig


@dataclass(frozen=True)
class FormulatedPlan:
    model: cp_model.CpModel
    var_store: VariableStore
    plan: SolverPlan