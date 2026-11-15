from __future__ import annotations

import logging
from collections import Counter, defaultdict
from datetime import date

from app.logging import LoggerTags
from app.models.planning import (
    PlantingVariant,
    SolverContext,
    VariantProfile,
)
from app.models.year_month import YearMonth
from app.preprocessing.candidate import SimulationCandidate
from app.preprocessing.steps.base import SimulationStep

logger = logging.getLogger(__name__)


def _to_planting_variant(
    cand: SimulationCandidate,
    context: SolverContext,
    clearance_date: date,
) -> PlantingVariant:
    profile = cand.profile
    return PlantingVariant(
        field_id=context.field.id,
        profile=VariantProfile(
            id=profile.id,
            crop=profile.crop,
            system_name=profile.system_name,
            max_planting_depletion_mm=profile.max_planting_depletion_mm,
            min_planting_depletion_mm=profile.min_planting_depletion_mm,
            min_planting_n_kg_ha=profile.min_planting_n_kg_ha,
            max_planting_n_kg_ha=profile.max_planting_n_kg_ha,
            min_planting_p_kg_ha=profile.min_planting_p_kg_ha,
            max_planting_p_kg_ha=profile.max_planting_p_kg_ha,
            min_planting_k_kg_ha=profile.min_planting_k_kg_ha,
            max_planting_k_kg_ha=profile.max_planting_k_kg_ha,
            planting_material_id=profile.planting_material_id,
            product_id=profile.product_id,
        ),
        plant_date=cand.plant_date,
        clearance_date=clearance_date,
        harvest_events=cand.harvest_events,
        gross_revenue_cents=cand.gross_revenue_cents,
        base_opex_cents=cand.base_opex_cents,
        planting_cost_cents=cand.planting_cost_cents,
        water_cost_cents=cand.water_cost_cents,
        fertilizer_cost_cents=cand.fertilizer_cost_cents,
        monthly_water=cand.monthly_water,
        monthly_nutrients=cand.monthly_nutrients,
        fertilizer=cand.fertilizer,
        irrigation_application=cand.irrigation_application,
        notes=cand.notes,
        yield_multipliers=cand.yield_multipliers,
    )


class AgronomicSimulator:
    def __init__(
        self,
        optimization_steps: list[SimulationStep],
        scenario_steps: list[SimulationStep],
    ) -> None:
        self._optimization_steps = optimization_steps
        self._scenario_steps = scenario_steps

    def evaluate_candidates(
        self, context: SolverContext, candidates: list[SimulationCandidate], horizon_end: date
    ) -> tuple[list[PlantingVariant], Counter[str]]:
        """
        Executes the biophysical simulation across a composable map-filter-flatMap pipeline:
        - Map: Applies the current simulation step (temperature, water, nutrients) to transform candidates.
        - Filter: Fail-fast pruning of non-viable trajectories (removes candidates with fatal death_notes).
        - FlatMap: Unrolls and flattens the surviving branched outcomes into the next generation pool.
        """
        death_notes: Counter[str] = Counter()
        active_candidates = candidates

        for step in self._optimization_steps:
            next_generation = []
            for cand in active_candidates:
                step_results = step.apply(cand, context)
                for res in step_results:
                    if res.is_dead:
                        death_reason = res.death_note.reason if res.death_note else "UnknownDeath"
                        death_notes[death_reason] += 1
                    else:
                        next_generation.append(res)
            active_candidates = next_generation
            logger.info(
                "%s %s: Survived candidate count %s",
                LoggerTags.SIMULATOR_STEP,
                type(step).__name__,
                len(next_generation),
            )
            if not active_candidates:
                break

        variants = []
        cands_by_month_and_profile = defaultdict(list)
        for cand in active_candidates:
            if cand.clearance_date is None or cand.clearance_date > horizon_end:
                continue
            start_ym = YearMonth(cand.plant_date.year, cand.plant_date.month)
            cands_by_month_and_profile[(start_ym, cand.profile.id)].append(cand)

        for month_cands in cands_by_month_and_profile.values():
            if not month_cands:
                continue

            best = max(month_cands, key=lambda c: c.total_utility_cents)

            if best.clearance_date is None:
                continue

            variants.append(_to_planting_variant(best, context, best.clearance_date))

        logger.info("%s Most common candidate rejection reasons:", LoggerTags.DEATH_REASONS)
        for reason, count in death_notes.most_common():
            logger.info("%s - %s: %d times", LoggerTags.DEATH_REASONS, reason, count)

        return variants, death_notes

    def evaluate_scenario(
        self, context: SolverContext, candidates: list[SimulationCandidate]
    ) -> tuple[list[PlantingVariant], Counter[str]]:
        death_notes: Counter[str] = Counter()
        variants: list[PlantingVariant] = []

        for initial_cand in candidates:
            cand = initial_cand
            for step in self._scenario_steps:
                step_results = step.apply(cand, context)
                if step_results:
                    cand = step_results[0]

            if cand.is_dead:
                death_reason = cand.death_note.reason if cand.death_note else "UnknownDeath"
                death_notes[death_reason] += 1

            effective_clearance = cand.clearance_date or cand.plant_date
            variants.append(_to_planting_variant(cand, context, effective_clearance))

        return variants, death_notes
