from __future__ import annotations

from typing import Any

from taskiq.result import TaskiqResult
from taskiq_redis import ListQueueBroker

from app.api.tasks import EVALUATE_SCENARIO_TASK_NAME, OPTIMIZE_FIELDS_TASK_NAME
from app.autogen.api.schemas import EvaluateScenarioRequest, OptimizeFieldRequest


class SolverDispatcher:
    def __init__(self, broker: ListQueueBroker) -> None:
        self._broker = broker

    async def enqueue_optimization(self, request: OptimizeFieldRequest) -> str:
        task = self._broker.find_task(OPTIMIZE_FIELDS_TASK_NAME)
        if task is None:
            msg = f"Task '{OPTIMIZE_FIELDS_TASK_NAME}' is not registered on the broker"
            raise RuntimeError(msg)

        job = await task.kiq(request)
        return job.task_id

    async def enqueue_evaluation(self, request: EvaluateScenarioRequest) -> str:
        task = self._broker.find_task(EVALUATE_SCENARIO_TASK_NAME)
        if task is None:
            msg = f"Task '{EVALUATE_SCENARIO_TASK_NAME}' is not registered on the broker"
            raise RuntimeError(msg)

        job = await task.kiq(request)
        return job.task_id

    async def is_ready(self, task_id: str) -> bool:
        if not self._broker.result_backend:
            msg = "Result backend is not configured"
            raise RuntimeError(msg)
        return await self._broker.result_backend.is_result_ready(task_id)

    async def get_result(self, task_id: str) -> TaskiqResult[Any]:
        if not self._broker.result_backend:
            msg = "Result backend is not configured"
            raise RuntimeError(msg)
        return await self._broker.result_backend.get_result(task_id)
