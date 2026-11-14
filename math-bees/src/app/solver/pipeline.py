from __future__ import annotations

import logging
import time

from ortools.sat.python import cp_model

from app.config import LoggingConfig, SolverConfig
from app.models.planning import CroppingCanvas, PlantingVariant
from app.models.types import UsdCents
from app.solver.formulator import ComponentRegistry, Formulator
from app.solver.result import (
    PlantingSchedule,
    SolverResult,
    SolverStatus,
    WallTime,
)

logger = logging.getLogger(__name__)

STATUS_MAPPING: dict[cp_model.CpSolverStatus, SolverStatus] = {
    cp_model.OPTIMAL: SolverStatus.OPTIMAL,
    cp_model.FEASIBLE: SolverStatus.FEASIBLE,
    cp_model.INFEASIBLE: SolverStatus.INFEASIBLE,
    cp_model.MODEL_INVALID: SolverStatus.UNKNOWN,
    cp_model.UNKNOWN: SolverStatus.UNKNOWN,
}


class SolutionProgressLogger(cp_model.CpSolverSolutionCallback):
    def __init__(self) -> None:
        super().__init__()
        self._start_time = time.perf_counter()
        self._solution_count = 0

    def on_solution_callback(self) -> None:
        self._solution_count += 1
        elapsed_time = time.perf_counter() - self._start_time
        logger.info(
            "[Solution #%d] Found at %.2fs | Objective: %.2f",
            self._solution_count,
            elapsed_time,
            self.objective_value,
        )


def _solve_field(
    canvas: CroppingCanvas,
    registry: ComponentRegistry,
    config: SolverConfig,
) -> tuple[SolverStatus, float, list[PlantingVariant]]:
    if not canvas.field:
        logger.error("No field present in canvas.")
        return SolverStatus.UNKNOWN, 0.0, []

    variants = canvas.variants

    if not variants:
        logger.info("No viable variants found. Skipping solver.")
        return SolverStatus.OPTIMAL, 0.0, []

    formulator = Formulator(registry)
    model, var_store = formulator.formulate(canvas)

    logger.info(
        "Model formulated (%d vars, %d constraints). Launching CP-SAT solver...",
        len(model.proto.variables),
        len(model.proto.constraints),
    )

    solver = cp_model.CpSolver()

    solver.parameters.num_workers = config.num_workers
    solver.parameters.max_time_in_seconds = config.time_limit_seconds
    solver.parameters.random_seed = config.random_seed
    solver.parameters.log_search_progress = config.log_search_progress
    solver.parameters.relative_gap_limit = config.relative_gap_limit
    solver.parameters.symmetry_level = 0

    progress_callback = SolutionProgressLogger()

    solve_start_time = time.perf_counter()
    raw_status = solver.Solve(model, progress_callback)
    solve_duration = time.perf_counter() - solve_start_time
    domain_status = STATUS_MAPPING.get(raw_status, SolverStatus.UNKNOWN)

    field_objective = 0.0
    selected_variants: list[PlantingVariant] = []

    if domain_status in (SolverStatus.OPTIMAL, SolverStatus.FEASIBLE):
        field_objective = solver.ObjectiveValue()
        for variant in variants:
            cp_var = var_store.get_variant_var(variant)
            if solver.Value(cp_var) == 1:
                selected_variants.append(variant)

    best_bound = solver.best_objective_bound
    gap_info = ""
    if field_objective > 0 and domain_status in (SolverStatus.OPTIMAL, SolverStatus.FEASIBLE):
        gap_percentage = abs(best_bound - field_objective) / field_objective * 100
        gap_info = f" | Gap: {gap_percentage:.2f}%"

    logger.info(
        "Finished in %.3fs with status: %s | Objective: %.2f | Best Bound: %.2f%s | Selected: %d variants",
        solve_duration,
        domain_status.value,
        field_objective,
        best_bound,
        gap_info,
        len(selected_variants),
    )

    return domain_status, field_objective, selected_variants


class SolverPipeline:
    def __init__(
        self,
        registry: ComponentRegistry,
        config: SolverConfig,
        logging_config: LoggingConfig,
    ) -> None:
        self._registry = registry
        self._config = config
        self._logging_config = logging_config

    def execute(
        self,
        canvas: CroppingCanvas,
    ) -> SolverResult:
        pipeline_start_time = time.perf_counter()

        logger.info("Initiating field optimization...")

        domain_status, field_objective, selected_variants = _solve_field(
            canvas=canvas,
            registry=self._registry,
            config=self._config,
        )

        total_wall_time = WallTime(seconds=round(time.perf_counter() - pipeline_start_time, 4))

        objective_cents = UsdCents(0)
        if domain_status in (SolverStatus.OPTIMAL, SolverStatus.FEASIBLE):
            objective_cents = UsdCents(round(field_objective))

        logger.info(
            "Optimization pipeline finished in %s with status: %s | Total Revenue: %s | Total Selected: %d variants",
            total_wall_time,
            domain_status.value,
            objective_cents,
            len(selected_variants),
        )

        return SolverResult(
            status=domain_status,
            schedule=PlantingSchedule(variants=selected_variants),
            expected_revenue=objective_cents,
        )
