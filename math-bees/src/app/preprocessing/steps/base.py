from __future__ import annotations

from typing import Protocol

from app.models.planning import SolverContext
from app.preprocessing.candidate import SimulationCandidate


class SimulationStep(Protocol):
    def apply(
        self,
        candidate: SimulationCandidate,
        context: SolverContext,
    ) -> list[SimulationCandidate]: ...
