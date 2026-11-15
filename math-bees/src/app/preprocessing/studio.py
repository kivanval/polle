from __future__ import annotations

import asyncio
import logging
from collections.abc import Iterable
from datetime import date, timedelta
from typing import TypedDict

import numpy as np

from app.models.planning import (
    AgronomicField,
    CroppingCanvas,
    CropScheduleItem,
    Horizon,
    PlannedCrop,
    PlantingVariant,
    SolverContext,
)
from app.models.types import CropProfileId, MaterialId
from app.preprocessing.carryover import evaluate_carryover_dynamics
from app.preprocessing.fallow import simulate_fallow_dynamics
from app.preprocessing.generator import CandidateGenerator
from app.preprocessing.simulator import AgronomicSimulator
from app.repositories.crop_profiles import CropProfileRepository
from app.repositories.crop_rotation_rules import CropRotationRuleRepository
from app.repositories.fertilizers import FertilizersRepository
from app.repositories.finance import FinanceRepository
from app.solver.strategy import SolverStrategy

logger = logging.getLogger(__name__)


class ClimateArrays(TypedDict):
    min_temps: np.ndarray
    max_temps: np.ndarray
    mean_temps: np.ndarray
    precipitations: np.ndarray
    et0s: np.ndarray
    horizon_start: date
    horizon_end: date


def _build_climate_arrays(context: SolverContext) -> ClimateArrays:
    """Fills the horizon's daily climate arrays and simulates the field's fallow dynamics into `context.field`."""
    horizon_start = date(context.horizon.start.year, context.horizon.start.month, 1)
    horizon_end = date(
        context.horizon.end.year, context.horizon.end.month, context.horizon.end.days_in_month
    )
    total_horizon_days = (horizon_end - horizon_start).days + 1

    climate_arrays: ClimateArrays = {
        "min_temps": np.empty(total_horizon_days, dtype=np.float32),
        "max_temps": np.empty(total_horizon_days, dtype=np.float32),
        "mean_temps": np.empty(total_horizon_days, dtype=np.float32),
        "precipitations": np.empty(total_horizon_days, dtype=np.float32),
        "et0s": np.empty(total_horizon_days, dtype=np.float32),
        "horizon_start": horizon_start,
        "horizon_end": horizon_end,
    }

    for day_offset in range(total_horizon_days):
        current_date = horizon_start + timedelta(days=day_offset)
        record = context.field.climate.records.get(current_date)
        if record:
            climate_arrays["min_temps"][day_offset] = float(record.temp_min_c)
            climate_arrays["max_temps"][day_offset] = float(record.temp_max_c)
            climate_arrays["mean_temps"][day_offset] = float(record.temp_mean_c)
            climate_arrays["precipitations"][day_offset] = float(record.precipitation_mm)
            climate_arrays["et0s"][day_offset] = record.et0_mm

    context.field = simulate_fallow_dynamics(context.field, horizon_start, total_horizon_days)

    return climate_arrays


class CroppingStudio:
    def __init__(
        self,
        simulator: AgronomicSimulator,
        crop_profile_repo: CropProfileRepository,
        rotation_repo: CropRotationRuleRepository,
        finance_repo: FinanceRepository,
        fertilizers_repo: FertilizersRepository,
    ) -> None:
        self._simulator = simulator
        self._crop_profile_repo = crop_profile_repo
        self._rotation_repo = rotation_repo
        self._finance_repo = finance_repo
        self._fertilizers_repo = fertilizers_repo

    async def _prepare_environment(
        self,
        field: AgronomicField,
        horizon: Horizon,
        strategy: SolverStrategy,
        extra_material_ids: Iterable[MaterialId] = (),
    ) -> tuple[SolverContext, ClimateArrays]:
        rotation_intervals, crop_profiles, fertilizers = await asyncio.gather(
            self._rotation_repo.get_intervals(),
            self._crop_profile_repo.get_all(),
            self._fertilizers_repo.get_all(),
        )

        material_ids: set[MaterialId] = set(extra_material_ids)
        profile_ids: list[CropProfileId] = []

        for profile in crop_profiles:
            profile_ids.append(profile.id)
            material_ids.add(profile.planting_material_id)
            if profile.product_id is not None:
                material_ids.add(profile.product_id)

        if strategy.replenishment:
            irrigation = strategy.replenishment.irrigation
            if irrigation.apply and irrigation.material_id:
                material_ids.add(irrigation.material_id)

            if strategy.replenishment.fertilizers.apply:
                for fert in fertilizers:
                    material_ids.add(fert.id)

        material_prices, crop_profile_opexs = await asyncio.gather(
            self._finance_repo.get_material_prices(
                material_ids=list(material_ids),
                start_year=horizon.start.year,
                end_year=horizon.end.year,
            ),
            self._finance_repo.get_crop_profile_opexs(
                profile_ids=profile_ids,
                start_year=horizon.start.year,
                end_year=horizon.end.year,
            ),
        )

        context = SolverContext(
            horizon=horizon,
            field=field,
            crop_profiles=crop_profiles,
            risk=strategy.risk,
            rotation_intervals=rotation_intervals,
            material_prices=material_prices,
            crop_profile_opexs=crop_profile_opexs,
            replenishment=strategy.replenishment,
            fertilizers=fertilizers,
        )

        # Day-by-day climate arrays and the fallow simulation are CPU-bound Python: off the event loop, so other tasks in
        # this worker (an evaluation behind a batch of optimizations) keep running.
        climate_arrays = await asyncio.to_thread(_build_climate_arrays, context)
        return context, climate_arrays

    async def prepare(
        self,
        field: AgronomicField,
        horizon: Horizon,
        strategy: SolverStrategy,
    ) -> CroppingCanvas:
        logger.info("Preparing Cropping Canvas for CP-SAT solver..")
        context, climate = await self._prepare_environment(field, horizon, strategy)

        variants = await asyncio.to_thread(self._simulate_all_windows, context, climate)

        return CroppingCanvas(
            horizon=horizon,
            field=context.field,
            variants=variants,
            rotation_intervals=context.rotation_intervals,
            material_prices=context.material_prices,
            risk=strategy.risk,
        )

    def _simulate_all_windows(
        self, context: SolverContext, climate: ClimateArrays
    ) -> list[PlantingVariant]:
        """Generates every planting window and simulates it: CPU-bound, so it runs in a worker thread."""
        initial_candidates = CandidateGenerator.generate_all_windows(
            context=context,
            horizon_start=climate["horizon_start"],
            min_temps=climate["min_temps"],
            max_temps=climate["max_temps"],
            mean_temps=climate["mean_temps"],
            precipitations=climate["precipitations"],
            et0s=climate["et0s"],
        )
        logger.info("Generated %d optimization candidate(s)", len(initial_candidates))

        variants, death_notes = self._simulator.evaluate_candidates(
            context=context, candidates=initial_candidates, horizon_end=climate["horizon_end"]
        )
        logger.info(
            "Canvas preparation complete: %d viable variant(s), %d rejected",
            len(variants),
            len(death_notes),
        )
        return variants

    async def evaluate_scenario(
        self,
        field: AgronomicField,
        horizon: Horizon,
        strategy: SolverStrategy,
        schedule: list[CropScheduleItem],
    ) -> list[PlantingVariant]:
        logger.info("Evaluating scenario with %d planned crop(s)..", len(schedule))
        extra_material_ids: list[MaterialId] = []
        for item in schedule:
            if item.fertilizer_application is not None:
                extra_material_ids.append(item.fertilizer_application.material_id)
            if item.irrigation_application is not None:
                extra_material_ids.append(item.irrigation_application.material_id)

        context, climate = await self._prepare_environment(
            field, horizon, strategy, extra_material_ids=extra_material_ids
        )

        profiles_by_id = {p.id: p for p in context.crop_profiles}
        resolved_crops: list[PlannedCrop] = []

        for item in schedule:
            profile = profiles_by_id.get(item.profile_id)
            if not profile:
                msg = f"Unknown crop_profile_id: {item.profile_id}"
                raise ValueError(msg)
            resolved_crops.append(
                PlannedCrop(
                    profile=profile,
                    plant_date=item.plant_date,
                    clearance_date=item.clearance_date,
                    fertilizer_application=item.fertilizer_application,
                    irrigation_application=item.irrigation_application,
                )
            )

        return await asyncio.to_thread(
            self._simulate_scenario, context, climate, horizon, resolved_crops
        )

    def _simulate_scenario(
        self,
        context: SolverContext,
        climate: ClimateArrays,
        horizon: Horizon,
        resolved_crops: list[PlannedCrop],
    ) -> list[PlantingVariant]:
        """Simulates the farmer's own crops and their carryover: CPU-bound, so it runs in a worker thread."""
        scenario_candidates = CandidateGenerator.generate_for_scenario(
            context=context,
            horizon_start=climate["horizon_start"],
            min_temps=climate["min_temps"],
            max_temps=climate["max_temps"],
            mean_temps=climate["mean_temps"],
            precipitations=climate["precipitations"],
            et0s=climate["et0s"],
            planned_crops=resolved_crops,
        )
        logger.info("Generated %d scenario candidate window(s)", len(scenario_candidates))

        variants, death_notes = self._simulator.evaluate_scenario(
            context=context, candidates=scenario_candidates
        )
        logger.info(
            "Simulator finished: %d viable variant(s), %d rejected", len(variants), len(death_notes)
        )

        if death_notes:
            logger.warning("Scenario crop rejections: %s", dict(death_notes))

        evaluated_variants = evaluate_carryover_dynamics(
            agronomic_field=context.field,
            horizon=horizon,
            rotation_intervals=context.rotation_intervals,
            variants=variants,
        )
        total_risks = sum(len(v.notes) for v in evaluated_variants)
        logger.info(
            "Carryover evaluated: %d variant(s) ready, %d risk note(s) detected",
            len(evaluated_variants),
            total_risks,
        )

        return evaluated_variants
