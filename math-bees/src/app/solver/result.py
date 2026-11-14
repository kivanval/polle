from __future__ import annotations

from dataclasses import dataclass, field
from enum import StrEnum
from typing import TypeAlias

from app.models.planning import PlantingVariant
from app.models.types import Usd, UsdCents


class SolverStatus(StrEnum):
    OPTIMAL = "OPTIMAL"
    FEASIBLE = "FEASIBLE"
    INFEASIBLE = "INFEASIBLE"
    UNKNOWN = "UNKNOWN"


@dataclass(frozen=True)
class WallTime:
    seconds: float

    @property
    def milliseconds(self) -> float:
        return self.seconds * 1000

    def __str__(self) -> str:
        return f"{self.seconds:.3f}s"


@dataclass(frozen=True)
class ProfitObjectiveResult:
    value_usd: Usd


ObjectiveResult: TypeAlias = ProfitObjectiveResult


@dataclass(frozen=True)
class PlantingSchedule:
    variants: list[PlantingVariant] = field(default_factory=list)

    @property
    def is_empty(self) -> bool:
        return len(self.variants) == 0

    def chronological(self) -> list[PlantingVariant]:
        return sorted(
            self.variants,
            key=lambda v: (v.field_id, v.plant_month),
        )

    def __len__(self) -> int:
        return len(self.variants)


@dataclass(frozen=True)
class SolverResult:
    status: SolverStatus
    schedule: PlantingSchedule
    expected_revenue: UsdCents

    @property
    def is_optimal(self) -> bool:
        return self.status == SolverStatus.OPTIMAL

    @property
    def is_feasible(self) -> bool:
        return self.status in (SolverStatus.OPTIMAL, SolverStatus.FEASIBLE)
