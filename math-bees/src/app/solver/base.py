from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum
from typing import TYPE_CHECKING, Any, Protocol, TypeVar

from app.models.planning import CroppingCanvas, PlantingVariant, VariantId

if TYPE_CHECKING:
    from ortools.sat.python import cp_model
    from ortools.sat.python.cp_model import CpModel, IntVar

    from app.models.types import FieldId
    from app.models.year_month import YearMonth

PayloadT = TypeVar("PayloadT", contravariant=True)


class ComponentPhase(IntEnum):
    VARIABLES = 1
    CONSTRAINTS = 2
    OBJECTIVE = 3


@dataclass
class VariableStore:
    variant_variables: dict[VariantId, IntVar] = field(default_factory=dict)
    depletion_variables: dict[tuple[FieldId, YearMonth], IntVar] = field(default_factory=dict)
    fallow_variables: dict[tuple[FieldId, YearMonth], IntVar] = field(default_factory=dict)
    stock_variables: dict[tuple[FieldId, str, YearMonth], IntVar] = field(default_factory=dict)
    irrigation_variables: dict[tuple[FieldId, YearMonth], IntVar] = field(default_factory=dict)
    irrigation_cost_exprs: list[Any] = field(default_factory=list)

    def create_variant_var(self, model: CpModel, variant: PlantingVariant) -> IntVar:
        var_name = f"x_{variant.id}"
        var = model.new_bool_var(var_name)
        self.variant_variables[variant.id] = var
        return var

    def get_variant_var(self, variant: PlantingVariant | VariantId) -> IntVar:
        var_id = variant.id if isinstance(variant, PlantingVariant) else variant
        var = self.variant_variables.get(var_id)
        if var is None:
            msg = f"Variable for variant '{var_id}' not found."
            raise KeyError(msg)
        return var

    def get_variant_var_by_id(self, variant_id: VariantId) -> IntVar | None:
        return self.variant_variables.get(variant_id)

    def create_depletion_var(
        self, model: CpModel, field_id: FieldId, ym: YearMonth, max_val: int,
    ) -> IntVar:
        var_name = f"depletion_{field_id}_{ym}"
        var = model.new_int_var(0, max_val, var_name)
        self.depletion_variables[(field_id, ym)] = var
        return var

    def get_depletion_var(self, field_id: FieldId, ym: YearMonth) -> IntVar | None:
        return self.depletion_variables.get((field_id, ym))

    def add_fallow_var(self, field_id: FieldId, ym: YearMonth, var: IntVar) -> None:
        self.fallow_variables[(field_id, ym)] = var

    def get_fallow_var(self, field_id: FieldId, ym: YearMonth) -> IntVar | None:
        return self.fallow_variables.get((field_id, ym))

    def create_stock_var(
        self,
        model: CpModel,
        field_id: FieldId,
        element: str,
        ym: YearMonth,
        min_val: int,
        max_val: int,
    ) -> IntVar:
        var_name = f"stock_{field_id}_{element}_{ym}"
        var = model.new_int_var(min_val, max_val, var_name)
        self.stock_variables[(field_id, element, ym)] = var
        return var

    def get_stock_var(self, field_id: FieldId, element: str, ym: YearMonth) -> IntVar | None:
        return self.stock_variables.get((field_id, element, ym))

    def create_irrigation_var(
        self, model: CpModel, field_id: FieldId, ym: YearMonth, max_val: int,
    ) -> IntVar:
        var_name = f"irr_{field_id}_{ym}"
        var = model.new_int_var(0, max_val, var_name)
        self.irrigation_variables[(field_id, ym)] = var
        return var

    def get_irrigation_var(self, field_id: FieldId, ym: YearMonth) -> IntVar | None:
        return self.irrigation_variables.get((field_id, ym))


class Component(Protocol[PayloadT]):
    name: str
    phase: ComponentPhase

    def apply(
        self,
        model: cp_model.CpModel,
        canvas: CroppingCanvas,
        var_store: VariableStore,
        payload: PayloadT,
    ) -> None: ...