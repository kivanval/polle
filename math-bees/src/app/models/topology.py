from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from typing import Final

from app.models.types import (
    AreaHa,
    CropId,
    DepletionMm,
    FieldId,
    FieldName,
    Fraction,
    Kilogram,
    Meter,
    Rate,
    SoilDrainageClass,
    SoilTexture,
)

MAX_NUTRIENT_CARRYOVER_KG_HA: Final[Rate[Kilogram, AreaHa]] = Rate(2000.0, target_type=Kilogram)


@dataclass(frozen=True, slots=True)
class HistoricalVariant:
    crop_id: CropId
    plant_date: date
    harvest_date: date


@dataclass(frozen=True, slots=True)
class SoilHydraulics:
    max_depth_m: Meter
    field_capacity_vol: Fraction
    wilting_point_vol: Fraction

    def calculate_taw_mm(self, rooting_depth_m: Meter | None) -> float:
        fc = float(self.field_capacity_vol)
        wp = float(self.wilting_point_vol)
        soil_depth = float(self.max_depth_m)

        root_depth = float(rooting_depth_m) if rooting_depth_m is not None else soil_depth
        effective_depth = min(soil_depth, root_depth)

        return 1000.0 * (fc - wp) * effective_depth

    @property
    def total_available_water_mm(self) -> float:
        return self.calculate_taw_mm(None)


@dataclass(frozen=True, slots=True)
class NutrientPool:
    initial_kg_ha: Rate[Kilogram, AreaHa]


@dataclass(frozen=True, slots=True)
class SoilNutrients:
    nitrogen: NutrientPool
    phosphorus: NutrientPool
    potassium: NutrientPool


@dataclass(frozen=True, slots=True)
class SoilProperties:
    texture: SoilTexture
    drainage: SoilDrainageClass
    ph: float
    salinity_ds_m: float
    hydraulics: SoilHydraulics
    initial_depletion_mm: DepletionMm | None
    nutrients: SoilNutrients


@dataclass(frozen=True, slots=True)
class Field:
    id: FieldId
    name: FieldName
    area_ha: AreaHa
    soil: SoilProperties
    history: tuple[HistoricalVariant, ...] = ()
