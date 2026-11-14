from dataclasses import dataclass

from app.models.types import MaterialId


@dataclass(frozen=True)
class RiskSetting:
    allow_missing_data: bool
    allow_agronomic_risks: bool
    allow_soil_depletion: bool


@dataclass(slots=True, frozen=True)
class IrrigationSetting:
    apply: bool
    material_id: MaterialId | None
    levels: tuple[float, ...]


@dataclass(slots=True, frozen=True)
class FertilizerSetting:
    apply: bool
    levels: tuple[float, ...]


@dataclass(slots=True, frozen=True)
class ReplenishmentSetting:
    irrigation: IrrigationSetting
    fertilizers: FertilizerSetting
