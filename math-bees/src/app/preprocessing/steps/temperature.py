from __future__ import annotations

from dataclasses import replace
from typing import Final

import numpy as np

from app.models.agronomy import CropProfileStage
from app.models.planning import SolverContext
from app.models.types import (
    DayOffset,
    DeathNote,
    Note,
    NoteCategory,
    NoteImportance,
    StageCategory,
    YieldMultiplier,
)
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

FALLBACK_DAILY_THERMAL_PENALTY: Final[float] = 0.03
CRITICAL_LOSS_THRESHOLD: Final[float] = 0.25
LETHAL_LOSS_THRESHOLD: Final[float] = 1.0
SUBLETHAL_TOLERANCE_FRACTION: Final[float] = 0.80
YIELD_MULTIPLIER_ROUND_DECIMALS: Final[int] = 3

DEFAULT_BASE_YIELD_MULTIPLIER: Final[float] = 1.0
MIN_YIELD_MULTIPLIER: Final[float] = 0.0
MIN_EFFECTIVE_TOLERANCE_DAYS: Final[int] = 0
MIN_STRESS_DAYS_THRESHOLD: Final[int] = 1
PERCENTAGE_MULTIPLIER: Final[float] = 100.0


def _evaluate_thermal_stress(
    candidate: SimulationCandidate,
    context: SolverContext,
    temps: np.ndarray,
    threshold: float | None,
    tolerance_days: int | None,
    stage_label: str,
    stress_name: str,
    is_lower_bound: bool,
    harvest_idx: int,
    harvest_mults: list[float],
    *,
    abort_on_fatal: bool = True,
) -> SimulationCandidate:
    if temps.size == 0:
        return candidate

    bound_name = "minimum" if is_lower_bound else "maximum"
    missing_temp_key = "MissingMinTemp" if is_lower_bound else "MissingMaxTemp"

    if threshold is None:
        is_fatal = abort_on_fatal and not context.risk.allow_missing_data
        return candidate.with_risk(
            note=Note(
                NoteCategory.DATA_MISSING,
                NoteImportance.RISK if not context.risk.allow_missing_data else NoteImportance.WARN,
                f"Missing absolute {bound_name} temperature data for the {stage_label} stage",
            ),
            fatal_if=is_fatal,
            death_note=DeathNote(f"TemperatureCheck:{missing_temp_key}"),
        )

    effective_tolerance = (
        tolerance_days if tolerance_days is not None else MIN_EFFECTIVE_TOLERANCE_DAYS
    )
    stress_days = int(np.count_nonzero(temps < threshold if is_lower_bound else temps > threshold))

    if stress_days > effective_tolerance:
        excess_days = stress_days - effective_tolerance
        loss_fraction = min(LETHAL_LOSS_THRESHOLD, excess_days * FALLBACK_DAILY_THERMAL_PENALTY)
        temp_yield_mult = max(MIN_YIELD_MULTIPLIER, 1.0 - loss_fraction)

        is_critical = loss_fraction >= CRITICAL_LOSS_THRESHOLD
        is_lethal = loss_fraction >= LETHAL_LOSS_THRESHOLD
        is_risk = is_lethal or (is_critical and not context.risk.allow_agronomic_risks)
        is_fatal = abort_on_fatal and is_risk

        death_reason = (
            f"TemperatureCheck:{stress_name}Kill"
            if is_lethal
            else f"TemperatureCheck:{stress_name}Shock"
        )

        penalty_pct = round(loss_fraction * PERCENTAGE_MULTIPLIER)

        stress_desc = "extreme heat" if stress_name == "Heat" else "severe frost"
        prep = "over" if stress_name == "Heat" else "below"

        if is_lethal:
            msg = f"The crop failed due to {stress_days} days of {stress_desc} ({prep} {threshold}°C) during the {stage_label} stage"
        else:
            msg = f"{stress_days} days of {stress_desc} ({prep} {threshold}°C) during the {stage_label} stage reduced the yield by {penalty_pct}%"

        cand = candidate.with_risk(
            note=Note(
                NoteCategory.AGRONOMIC,
                NoteImportance.RISK if is_risk else NoteImportance.WARN,
                msg,
            ),
            fatal_if=is_fatal,
            death_note=DeathNote(death_reason, is_static=False),
        )

        if cand.is_dead:
            return cand

        harvest_mults[harvest_idx] *= temp_yield_mult
        return cand

    risk_threshold = (
        max(MIN_STRESS_DAYS_THRESHOLD, int(effective_tolerance * SUBLETHAL_TOLERANCE_FRACTION))
        if effective_tolerance > MIN_STRESS_DAYS_THRESHOLD
        else MIN_STRESS_DAYS_THRESHOLD
    )
    if stress_days >= risk_threshold:
        desc = "hot" if stress_name == "Heat" else "cold"
        return candidate.with_risk(
            note=Note(
                NoteCategory.AGRONOMIC,
                NoteImportance.WARN,
                f"Frequent {desc} days during the {stage_label} stage caused mild stress",
            ),
            fatal_if=False,
            death_note=DeathNote(f"TemperatureCheck:Sublethal{stress_name}Stress"),
        )

    return candidate


class AbsoluteTemperatureStep(SimulationStep):
    @staticmethod
    def _evaluate_stages(
        candidate: SimulationCandidate,
        context: SolverContext,
        *,
        abort_on_fatal: bool,
    ) -> list[SimulationCandidate]:
        if candidate.is_dead:
            return [candidate]

        if candidate.clearance_date is None or not candidate.stage_offsets:
            if not abort_on_fatal:
                return [candidate]
            cand = candidate.with_risk(
                note=Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.RISK,
                    "The growth cycle was incomplete",
                ),
                fatal_if=True,
                death_note=DeathNote("PhenologyStep:IncompleteCycle"),
            )
            return [cand]

        stages = candidate.profile.stages
        is_perennial = candidate.profile.is_perennial

        if abort_on_fatal and not is_perennial and len(candidate.stage_offsets) < len(stages):
            cand = candidate.with_risk(
                note=Note(
                    NoteCategory.AGRONOMIC,
                    NoteImportance.RISK,
                    f"The crop only completed {len(candidate.stage_offsets)} out of {len(stages)} growth stages",
                ),
                fatal_if=True,
                death_note=DeathNote("PhenologyStep:UnfinishedCycle"),
            )
            return [cand]

        if is_perennial and candidate.profile.perennial_restart_stage is not None:
            restart_order = candidate.profile.perennial_restart_stage
            restart_idx = next(
                (idx for idx, s in enumerate(stages) if s.sequence_order == restart_order),
                0,
            )
            stage_sequence: list[CropProfileStage] = []
            cur_idx = 0
            for _ in range(len(candidate.stage_offsets)):
                stage_sequence.append(stages[cur_idx])
                cur_idx += 1
                if cur_idx >= len(stages):
                    cur_idx = restart_idx
        else:
            stage_sequence = stages[: len(candidate.stage_offsets)] if not is_perennial else stages

        num_harvests = max(1, len(candidate.harvest_events))
        if candidate.yield_multipliers:
            if len(candidate.yield_multipliers) == num_harvests:
                harvest_mults = [float(m.real) for m in candidate.yield_multipliers]
            else:
                base_val = float(candidate.yield_multipliers[0].real)
                harvest_mults = [base_val] * num_harvests
        else:
            harvest_mults = [DEFAULT_BASE_YIELD_MULTIPLIER] * num_harvests

        current_candidate = candidate
        start_idx = DayOffset(0)
        min_temps = current_candidate.min_temps
        max_temps = current_candidate.max_temps
        current_harvest_idx = 0

        for stage, end_idx in zip(stage_sequence, current_candidate.stage_offsets, strict=False):
            if start_idx >= end_idx:
                continue

            current_candidate = _evaluate_thermal_stress(
                candidate=current_candidate,
                context=context,
                temps=min_temps[start_idx:end_idx],
                threshold=float(stage.absolute_min_temp_c)
                if stage.absolute_min_temp_c is not None
                else None,
                tolerance_days=stage.frost_shock_tolerance_days,
                stage_label=stage.label,
                stress_name="Frost",
                is_lower_bound=True,
                harvest_idx=current_harvest_idx,
                harvest_mults=harvest_mults,
                abort_on_fatal=abort_on_fatal,
            )
            if current_candidate.is_dead:
                return [current_candidate]

            current_candidate = _evaluate_thermal_stress(
                candidate=current_candidate,
                context=context,
                temps=max_temps[start_idx:end_idx],
                threshold=float(stage.absolute_max_temp_c)
                if stage.absolute_max_temp_c is not None
                else None,
                tolerance_days=stage.heat_shock_tolerance_days,
                stage_label=stage.label,
                stress_name="Heat",
                is_lower_bound=False,
                harvest_idx=current_harvest_idx,
                harvest_mults=harvest_mults,
                abort_on_fatal=abort_on_fatal,
            )
            if current_candidate.is_dead:
                return [current_candidate]

            if stage.category == StageCategory.HARVEST:
                current_harvest_idx = min(current_harvest_idx + 1, num_harvests - 1)

            start_idx = end_idx

        new_mults = tuple(
            YieldMultiplier(round(m, YIELD_MULTIPLIER_ROUND_DECIMALS)) for m in harvest_mults
        )
        return [replace(current_candidate, yield_multipliers=new_mults)]

    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        return self._evaluate_stages(candidate, context, abort_on_fatal=True)


class ScenarioTemperatureStep(AbsoluteTemperatureStep):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]:
        return self._evaluate_stages(candidate, context, abort_on_fatal=False)
