from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Final

from app.models.planning import SolverContext
from app.models.types import CropId, DeathNote, Note, NoteCategory, NoteImportance
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

MONTHS_PER_YEAR: Final[int] = 12


@dataclass(slots=True, frozen=True)
class _HistoryRestriction:
    min_allowed_plant_month: int
    historical_crop_id: CropId
    gap_months: int
    hist_month: int


class HistoryRotationStep(SimulationStep):
    def __init__(self) -> None:
        self._cache: dict[tuple[int, Any, Any], tuple[int, tuple[_HistoryRestriction, ...]]] = {}

    def _get_restrictions(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> tuple[int, tuple[_HistoryRestriction, ...]]:
        intervals = context.rotation_intervals
        cache_key = (id(intervals), candidate.field.id, candidate.profile.crop.id)
        cached_rule = self._cache.get(cache_key)

        if cached_rule is None:
            succ_crop_id = candidate.profile.crop.id
            pred_rules = intervals.get(succ_crop_id)
            if not pred_rules:
                cached_rule = (0, ())
                self._cache[cache_key] = cached_rule
                return cached_rule

            build_restrictions: list[_HistoryRestriction] = []
            for historical in candidate.field.history:
                gap = pred_rules.get(historical.crop_id)
                if not gap:
                    continue

                gap_val = int(gap.real)
                if gap_val <= 0:
                    continue

                hist_m = (
                    historical.harvest_date.year * MONTHS_PER_YEAR + historical.harvest_date.month
                )
                build_restrictions.append(
                    _HistoryRestriction(
                        min_allowed_plant_month=hist_m + gap_val,
                        historical_crop_id=historical.crop_id,
                        gap_months=gap_val,
                        hist_month=hist_m,
                    ),
                )

            max_required_month = max(
                (r.min_allowed_plant_month for r in build_restrictions),
                default=0,
            )
            cached_rule = (max_required_month, tuple(build_restrictions))
            self._cache[cache_key] = cached_rule

        return cached_rule

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead or not candidate.field.history:
            return [candidate]

        plant_month_idx = candidate.plant_date.year * MONTHS_PER_YEAR + candidate.plant_date.month
        max_required_month, restrictions = self._get_restrictions(candidate, context)

        if not restrictions or plant_month_idx >= max_required_month:
            return [candidate]

        for restr in restrictions:
            if plant_month_idx < restr.min_allowed_plant_month:
                months_passed = plant_month_idx - restr.hist_month
                msg = (
                    f"This crop requires a {restr.gap_months}-month gap "
                    f"after '{restr.historical_crop_id}', but only {months_passed} months have passed"
                )
                return [
                    candidate.with_risk(
                        note=Note(NoteCategory.AGRONOMIC, NoteImportance.RISK, msg),
                        fatal_if=True,
                        death_note=DeathNote("HistoryRotation:IntervalViolation"),
                    ),
                ]

        return [candidate]


class ScenarioHistoryRotationStep(HistoryRotationStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead or not candidate.field.history:
            return [candidate]

        plant_month_idx = candidate.plant_date.year * MONTHS_PER_YEAR + candidate.plant_date.month
        max_required_month, restrictions = self._get_restrictions(candidate, context)

        if not restrictions or plant_month_idx >= max_required_month:
            return [candidate]

        curr_cand = candidate
        for restr in restrictions:
            if plant_month_idx < restr.min_allowed_plant_month:
                months_passed = plant_month_idx - restr.hist_month
                msg = (
                    f"This crop requires a {restr.gap_months}-month gap "
                    f"after '{restr.historical_crop_id}', but only {months_passed} months have passed"
                )
                curr_cand = curr_cand.with_risk(
                    note=Note(NoteCategory.AGRONOMIC, NoteImportance.RISK, msg),
                )

        return [curr_cand]
