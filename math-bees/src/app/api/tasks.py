from __future__ import annotations

import asyncio
import calendar
import logging
from dataclasses import dataclass
from datetime import date
from functools import partial
from typing import Final

from dishka import FromDishka
from dishka.integrations.taskiq import inject

from app.api.mappers import (
    map_evaluate_request_to_domain,
    map_evaluate_result_to_response,
    map_request_to_domain,
    map_result_to_response,
)
from app.autogen.api.schemas import (
    EvaluateScenarioRequest,
    EvaluateScenarioResponse,
    OptimizeFieldRequest,
    OptimizeFieldResponse,
    SolverResultStatus,
)
from app.infrastructure.caching import Cache
from app.models.planning import AgronomicField
from app.models.types import UsdCents
from app.preprocessing.preparer import CanvasPreparer
from app.preprocessing.studio import CroppingStudio
from app.repositories.climate import ClimateRepository
from app.repositories.fields import FieldsRepository
from app.solver.pipeline import SolverPipeline
from app.solver.result import PlantingSchedule, SolverResult, SolverStatus

logger = logging.getLogger(__name__)

OPTIMIZE_FIELDS_TASK_NAME: Final[str] = "optimize_fields_task"
EVALUATE_SCENARIO_TASK_NAME: Final[str] = "evaluate_scenario_task"


async def _optimize_fields(
    request: OptimizeFieldRequest,
    preparer: CanvasPreparer,
    pipeline: SolverPipeline,
) -> OptimizeFieldResponse:
    logger.info("Executing optimization task...")
    field_id, horizon, strategy = map_request_to_domain(request)
    canvas = await preparer.prepare(
        field_id=field_id,
        horizon=horizon,
        strategy=strategy,
    )
    result = await asyncio.to_thread(pipeline.execute, canvas)
    return map_result_to_response(result)


@dataclass(frozen=True)
class OptimizeFieldsCacheKey:
    request: OptimizeFieldRequest


class OptimizeFieldsCache(Cache[OptimizeFieldsCacheKey, OptimizeFieldResponse]): ...


@inject
async def optimize_fields_task(
    request: OptimizeFieldRequest,
    preparer: FromDishka[CanvasPreparer],
    pipeline: FromDishka[SolverPipeline],
    cache: FromDishka[OptimizeFieldsCache],
) -> OptimizeFieldResponse:
    result = await cache.using(
        OptimizeFieldsCacheKey(request),
        partial(_optimize_fields, request, preparer, pipeline),
    )
    return (
        result
        if result is not None
        else OptimizeFieldResponse(
            status=SolverResultStatus.unknown,
            schedule=[],
        )
    )


@inject
async def evaluate_scenario_task(
    request: EvaluateScenarioRequest,
    fields_repo: FromDishka[FieldsRepository],
    climate_repo: FromDishka[ClimateRepository],
    studio: FromDishka[CroppingStudio],
) -> EvaluateScenarioResponse:
    logger.info("Executing evaluation task...")
    field_id, horizon, strategy, schedule = map_evaluate_request_to_domain(request)

    start_date = horizon.start.first_day
    end_date = date(
        horizon.end.year,
        horizon.end.month,
        calendar.monthrange(horizon.end.year, horizon.end.month)[1],
    )

    field, climate = await asyncio.gather(
        fields_repo.get_by_id(field_id, as_of=horizon.start),
        climate_repo.get_by_field_id(field_id, start_date, end_date),
    )

    if not field or not climate:
        raise ValueError(f"Missing topology or climate for field {field_id}")

    agronomic_field = AgronomicField(
        field=field,
        climate=climate,
        fallow_water_balance={},
        fallow_nutrient_recovery={},
    )

    variants = await studio.evaluate_scenario(
        field=agronomic_field,
        horizon=horizon,
        strategy=strategy,
        schedule=schedule,
    )

    total_cents = sum(v.expected_revenue.real for v in variants)
    res = SolverResult(
        status=SolverStatus.FEASIBLE,
        schedule=PlantingSchedule(variants=variants),
        expected_revenue=UsdCents(int(total_cents)),
    )
    return map_evaluate_result_to_response(res)
