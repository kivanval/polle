from dataclasses import dataclass

from app.solver.settings import ReplenishmentSetting, RiskSetting


@dataclass(frozen=True)
class SolverStrategy:
    risk: RiskSetting
    replenishment: ReplenishmentSetting