from __future__ import annotations

import logging
from datetime import date
from typing import Final

import numpy as np

from app.models.dekad import DEKAD_START_DAYS
from app.models.planning import PlannedCrop, SolverContext
from app.models.types import UsdCents
from app.preprocessing.candidate import SimulationCandidate

logger = logging.getLogger(__name__)

_EMPTY_DICT: Final[dict] = {}


class CandidateGenerator:
    @staticmethod
    def generate_all_windows(
        context: SolverContext,
        horizon_start: date,
        min_temps: np.ndarray,
        max_temps: np.ndarray,
        mean_temps: np.ndarray,
        precipitations: np.ndarray,
        et0s: np.ndarray,
    ) -> list[SimulationCandidate]:
        initial_candidates = []
        for profile in context.crop_profiles:
            if not profile.stages:
                continue
            for start_ym in context.months:
                for day in DEKAD_START_DAYS:
                    plant_date = date(start_ym.year, start_ym.month, day)
                    plant_idx = (plant_date - horizon_start).days

                    cand = SimulationCandidate(
                        field=context.field.field,
                        profile=profile,
                        plant_date=plant_date,
                        min_temps=min_temps[plant_idx:],
                        max_temps=max_temps[plant_idx:],
                        mean_temps=mean_temps[plant_idx:],
                        precipitations=precipitations[plant_idx:],
                        et0s=et0s[plant_idx:],
                        clearance_date=None,
                        harvest_events=(),
                        stage_offsets=(),
                        yield_multipliers=(),
                        gross_revenue_cents=UsdCents(0),
                        base_opex_cents=UsdCents(0),
                        planting_cost_cents=UsdCents(0),
                        water_cost_cents=UsdCents(0),
                        fertilizer_cost_cents=UsdCents(0),
                        notes=(),
                        monthly_water=_EMPTY_DICT,
                        monthly_nutrients=_EMPTY_DICT,
                        fertilizer=None,
                        fertilizer_application=None,
                        irrigation_application=None,
                    )
                    initial_candidates.append(cand)
        return initial_candidates

    @staticmethod
    def generate_for_scenario(
        context: SolverContext,
        horizon_start: date,
        min_temps: np.ndarray,
        max_temps: np.ndarray,
        mean_temps: np.ndarray,
        precipitations: np.ndarray,
        et0s: np.ndarray,
        planned_crops: list[PlannedCrop],
    ) -> list[SimulationCandidate]:
        candidates: list[SimulationCandidate] = []
        for planned in planned_crops:
            plant_idx = (planned.plant_date - horizon_start).days
            slice_start = (
                len(min_temps) if plant_idx < 0 or plant_idx >= len(min_temps) else plant_idx
            )
            cand = SimulationCandidate(
                field=context.field.field,
                profile=planned.profile,
                plant_date=planned.plant_date,
                min_temps=min_temps[slice_start:],
                max_temps=max_temps[slice_start:],
                mean_temps=mean_temps[slice_start:],
                precipitations=precipitations[slice_start:],
                et0s=et0s[slice_start:],
                clearance_date=planned.clearance_date,
                harvest_events=(),
                stage_offsets=(),
                yield_multipliers=(),
                gross_revenue_cents=UsdCents(0),
                base_opex_cents=UsdCents(0),
                planting_cost_cents=UsdCents(0),
                water_cost_cents=UsdCents(0),
                fertilizer_cost_cents=UsdCents(0),
                notes=(),
                monthly_water=_EMPTY_DICT,
                monthly_nutrients=_EMPTY_DICT,
                fertilizer=None,
                fertilizer_application=planned.fertilizer_application,
                irrigation_application=planned.irrigation_application,
            )
            candidates.append(cand)
        return candidates
