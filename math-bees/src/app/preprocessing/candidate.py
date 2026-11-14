from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass, replace
from datetime import date

import numpy as np

from app.models.agronomy import AppliedFertilizer, CropProfile
from app.models.planning import ScheduledFertilizerApplication, ScheduledIrrigationApplication
from app.models.topology import Field
from app.models.types import (
    DayOffset,
    DeathNote,
    MonthlyNutrientDynamics,
    MonthlyWaterDynamics,
    Note,
    UsdCents,
    YieldMultiplier,
)
from app.models.year_month import YearMonth


@dataclass(slots=True, frozen=True)
class SimulationCandidate:
    field: Field
    profile: CropProfile
    plant_date: date

    min_temps: np.ndarray
    max_temps: np.ndarray
    mean_temps: np.ndarray
    precipitations: np.ndarray
    et0s: np.ndarray

    clearance_date: date | None
    harvest_events: tuple[date, ...]
    stage_offsets: tuple[DayOffset, ...]
    yield_multipliers: tuple[YieldMultiplier, ...]

    gross_revenue_cents: UsdCents
    base_opex_cents: UsdCents
    planting_cost_cents: UsdCents
    water_cost_cents: UsdCents
    fertilizer_cost_cents: UsdCents

    monthly_water: dict[YearMonth, MonthlyWaterDynamics]
    monthly_nutrients: dict[YearMonth, MonthlyNutrientDynamics]

    fertilizer: AppliedFertilizer | None
    fertilizer_application: ScheduledFertilizerApplication | None
    irrigation_application: ScheduledIrrigationApplication | None

    notes: tuple[Note, ...]

    _death_note: DeathNote | None = None

    @property
    def death_note(self) -> DeathNote | None:
        return self._death_note

    @property
    def is_dead(self) -> bool:
        return self._death_note is not None

    @property
    def expenses_cents(self) -> UsdCents:
        return UsdCents(
            self.base_opex_cents.real
            + self.planting_cost_cents.real
            + self.water_cost_cents.real
            + self.fertilizer_cost_cents.real
        )

    @property
    def total_utility_cents(self) -> int:
        return self.gross_revenue_cents.real - self.expenses_cents.real

    def killed(self, note: DeathNote) -> SimulationCandidate:
        return replace(self, _death_note=note)

    def with_risk(
        self,
        note: Note,
        fatal_if: bool = False,
        death_note: DeathNote | None = None,
    ) -> SimulationCandidate:
        cand = replace(self, notes=(*self.notes, note))
        if fatal_if and death_note:
            return cand.killed(death_note)
        return cand

    def with_risks(
        self,
        notes: Sequence[Note],
        fatal_if: bool = False,
        death_note: DeathNote | None = None,
    ) -> SimulationCandidate:
        cand = replace(self, notes=(*self.notes, *notes))
        if fatal_if and death_note:
            return cand.killed(death_note)
        return cand
