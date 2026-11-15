"""The studio's CPU-bound preprocessing must not hold the worker's event loop.

A taskiq worker process runs many tasks on one event loop. When an optimization's candidate generation and simulation
ran on that loop, an evaluation fetched by the same process could not progress until every optimization ahead of it
had finished, so "Evaluate my plan" looked like it never started. These tests run the studio while a heartbeat
coroutine ticks, and fail if the loop stalls for the whole CPU-bound step.
"""

from __future__ import annotations

import asyncio
import time
from typing import Any
from unittest.mock import MagicMock

import pytest

from app.preprocessing import studio as studio_module
from app.preprocessing.studio import CroppingStudio

BUSY_SECONDS = 0.6


def _busy(*_args: Any, **_kwargs: Any) -> list[Any]:
    end = time.monotonic() + BUSY_SECONDS
    while time.monotonic() < end:
        pass
    return []


async def _ticks_while(work: Any) -> int:
    ticks = 0
    done = asyncio.Event()

    async def heartbeat() -> None:
        nonlocal ticks
        while not done.is_set():
            ticks += 1
            await asyncio.sleep(0.02)

    beat = asyncio.create_task(heartbeat())
    await asyncio.sleep(0)  # let the heartbeat start before the work
    ticks = 0
    try:
        await work
    finally:
        done.set()
        await beat
    return ticks


def _studio(monkeypatch: pytest.MonkeyPatch) -> CroppingStudio:
    simulator = MagicMock()
    simulator.evaluate_candidates.return_value = ([], {})
    simulator.evaluate_scenario.return_value = ([], {})
    studio = CroppingStudio(simulator, MagicMock(), MagicMock(), MagicMock(), MagicMock())

    context = MagicMock()
    context.crop_profiles = []
    climate = {key: None for key in ("min_temps", "max_temps", "mean_temps", "precipitations", "et0s", "horizon_start", "horizon_end")}

    async def prepared(*_args: Any, **_kwargs: Any) -> tuple[Any, Any]:
        return context, climate

    monkeypatch.setattr(studio, "_prepare_environment", prepared)
    monkeypatch.setattr(studio_module.CandidateGenerator, "generate_all_windows", staticmethod(_busy))
    monkeypatch.setattr(studio_module.CandidateGenerator, "generate_for_scenario", staticmethod(_busy))
    monkeypatch.setattr(studio_module, "evaluate_carryover_dynamics", lambda **_kwargs: [])
    return studio


def test_optimization_preprocessing_leaves_the_event_loop_free(monkeypatch: pytest.MonkeyPatch) -> None:
    studio = _studio(monkeypatch)
    ticks = asyncio.run(_ticks_while(studio.prepare(MagicMock(), MagicMock(), MagicMock())))
    assert ticks >= 5, f"the event loop stalled during candidate generation ({ticks} ticks)"


def test_scenario_evaluation_leaves_the_event_loop_free(monkeypatch: pytest.MonkeyPatch) -> None:
    studio = _studio(monkeypatch)
    ticks = asyncio.run(_ticks_while(studio.evaluate_scenario(MagicMock(), MagicMock(), MagicMock(), [])))
    assert ticks >= 5, f"the event loop stalled during scenario simulation ({ticks} ticks)"
