from __future__ import annotations

from app.solver.components.crop_rotation import CropRotationComponent
from app.solver.components.field_occupancy import FieldOccupancyComponent
from app.solver.components.max_profit import MaxProfitComponent
from app.solver.components.nutrient_accumulation import NutrientAccumulationComponent
from app.solver.components.water_accumulation import WaterAccumulationComponent
from app.solver.plan import ComponentConfig, SolverPlan


class ComponentAssembler:
    def __init__(self) -> None:
        pass

    def assemble(self) -> SolverPlan:
        return SolverPlan(
            variables=[
                ComponentConfig(name=FieldOccupancyComponent.name),
            ],
            constraints=[
                ComponentConfig(
                    name=CropRotationComponent.name,
                ),
                ComponentConfig(name=WaterAccumulationComponent.name),
                ComponentConfig(name=NutrientAccumulationComponent.name),
            ],
            objective=ComponentConfig(name=MaxProfitComponent.name),
        )