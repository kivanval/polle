from __future__ import annotations

import asyncio
import logging
from typing import TYPE_CHECKING

from dishka.integrations.taskiq import inject

from app.api.mappers import map_request_to_domain, map_result_to_response

if TYPE_CHECKING:
    from dishka import FromDishka

    from app.autogen.api.schemas import OptimizeFieldRequest, OptimizeFieldResponse
    from app.preprocessing.preparer import CanvasPreparer
    from app.solver.pipeline import SolverPipeline

logger = logging.getLogger(__name__)

OPTIMIZE_FIELDS_TASK_NAME = "optimize_fields_task"


@inject
async def optimize_fields_task(
    request: OptimizeFieldRequest,
    preparer: FromDishka[CanvasPreparer],
    pipeline: FromDishka[SolverPipeline],
) -> OptimizeFieldResponse:
    field_id, horizon, strategy = map_request_to_domain(request)

    canvas = await preparer.prepare(
        field_id=field_id,
        horizon=horizon,
        strategy=strategy,
    )

    result = await asyncio.to_thread(pipeline.execute, canvas)

    return map_result_to_response(result)