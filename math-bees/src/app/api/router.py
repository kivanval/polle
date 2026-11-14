from __future__ import annotations

from dishka import FromDishka
from dishka.integrations.fastapi import inject
from fastapi import APIRouter, HTTPException

from app.autogen.api.schemas import (
    EvaluateScenarioRequest,
    OptimizeFieldRequest,
    OptimizeFieldResponse,
    TaskResponse,
    TaskStatusResponse,
)
from app.infrastructure.dispatcher import SolverDispatcher

router = APIRouter(prefix="/api/v1/solver", tags=["Solver"])


@router.post("/optimizations", response_model=TaskResponse, status_code=202)
@inject
async def submit_optimization_task(
    request: OptimizeFieldRequest,
    dispatcher: FromDishka[SolverDispatcher],
) -> TaskResponse:
    task_id = await dispatcher.enqueue_optimization(request)
    return TaskResponse(task_id=task_id)


@router.get("/optimizations/{task_id}/status", response_model=TaskStatusResponse)
@inject
async def get_optimization_status(
    task_id: str,
    dispatcher: FromDishka[SolverDispatcher],
) -> TaskStatusResponse:
    is_ready = await dispatcher.is_ready(task_id)
    if is_ready:
        raise HTTPException(
            status_code=303,
            detail="Task is completed. Fetch the result from the /result endpoint.",
        )
    return TaskStatusResponse(status="processing")


@router.get("/optimizations/{task_id}/result", response_model=OptimizeFieldResponse)
@inject
async def get_optimization_result(
    task_id: str,
    dispatcher: FromDishka[SolverDispatcher],
) -> OptimizeFieldResponse:
    is_ready = await dispatcher.is_ready(task_id)
    if not is_ready:
        raise HTTPException(
            status_code=404,
            detail="Task result not found or task is still running.",
        )
    result = await dispatcher.get_result(task_id)
    if result.is_err:
        raise HTTPException(status_code=500, detail=f"Optimization task failed: {result.error}")

    val = result.return_value
    if isinstance(val, OptimizeFieldResponse):
        return val
    return OptimizeFieldResponse.model_validate(val)


@router.post("/evaluations", response_model=TaskResponse, status_code=202)
@inject
async def submit_scenario_evaluation_task(
    request: EvaluateScenarioRequest,
    dispatcher: FromDishka[SolverDispatcher],
) -> TaskResponse:
    task_id = await dispatcher.enqueue_evaluation(request)
    return TaskResponse(task_id=task_id)


@router.get("/evaluations/{task_id}/status", response_model=TaskStatusResponse)
@inject
async def get_evaluation_status(
    task_id: str,
    dispatcher: FromDishka[SolverDispatcher],
) -> TaskStatusResponse:
    is_ready = await dispatcher.is_ready(task_id)
    if is_ready:
        raise HTTPException(
            status_code=303,
            detail="Task is completed. Fetch the result from the /result endpoint.",
        )
    return TaskStatusResponse(status="processing")


@router.get("/evaluations/{task_id}/result", response_model=OptimizeFieldResponse)
@inject
async def get_evaluation_result(
    task_id: str,
    dispatcher: FromDishka[SolverDispatcher],
) -> OptimizeFieldResponse:
    is_ready = await dispatcher.is_ready(task_id)
    if not is_ready:
        raise HTTPException(
            status_code=404,
            detail="Task result not found or task is still running.",
        )
    result = await dispatcher.get_result(task_id)
    if result.is_err:
        raise HTTPException(status_code=500, detail=f"Evaluation task failed: {result.error}")

    val = result.return_value
    if isinstance(val, OptimizeFieldResponse):
        return val
    return OptimizeFieldResponse.model_validate(val)
