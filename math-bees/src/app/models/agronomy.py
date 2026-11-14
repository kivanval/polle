from dataclasses import dataclass
from enum import Enum

from app.models.types import (
    GDD,
    VD,
    AreaHa,
    Celsius,
    CropCoefficient,
    CropId,
    CropProfileId,
    DepletionMm,
    Fraction,
    IsPerennial,
    Kilogram,
    LifespanYears,
    MaterialId,
    Meter,
    Percent,
    Rate,
    ScientificName,
    SequenceOrder,
    SoilDrainageClass,
    SoilTexture,
    StageCategory,
    SystemName,
    YieldPerHa,
    YieldUnit,
)


class SoilImpact(str, Enum):
    NEUTRAL = "NEUTRAL"
    ACIDIFIES = "ACIDIFIES"
    ALKALINIZES = "ALKALINIZES"
    SALINIZES = "SALINIZES"


@dataclass(frozen=True)
class Fertilizer:
    id: MaterialId
    n_percent: Percent
    p_percent: Percent
    k_percent: Percent
    soil_impact: SoilImpact


@dataclass(frozen=True, slots=True)
class AppliedFertilizer:
    product: Fertilizer
    rate: Rate[Kilogram, AreaHa]


@dataclass(frozen=True)
class Crop:
    id: CropId
    scientific_name: ScientificName
    allowed_soil_textures: set[SoilTexture] | None
    allowed_drainage_classes: set[SoilDrainageClass] | None
    ph_min: float | None
    ph_max: float | None

    # Maas-Hoffman salinity parameters
    salinity_threshold_ds_m: float | None
    salinity_slope_pct: float | None
    max_salinity_ds_m: float | None


@dataclass(frozen=True)
class CropProfileStage:
    sequence_order: SequenceOrder
    category: StageCategory

    target_accumulated_gdd: GDD | None
    gdd_base_temp_c: Celsius | None
    gdd_optimum_min_temp_c: Celsius | None
    gdd_optimum_max_temp_c: Celsius | None

    absolute_min_temp_c: Celsius | None
    absolute_max_temp_c: Celsius | None

    frost_shock_tolerance_days: int | None
    heat_shock_tolerance_days: int | None
    drought_shock_tolerance_days: int | None
    waterlogging_shock_tolerance_days: int | None

    min_days_since_planting: int | None
    max_days_since_planting: int | None

    target_accumulated_vd: VD | None
    vernalization_absolute_min_temp_c: Celsius | None
    vernalization_absolute_max_temp_c: Celsius | None
    vernalization_optimal_min_temp_c: Celsius | None
    vernalization_optimal_max_temp_c: Celsius | None

    devernalization_start_temp_c: Celsius | None
    devernalization_full_temp_c: Celsius | None

    water_crop_coefficient: CropCoefficient | None
    water_yield_response_factor: CropCoefficient | None

    n_yield_response_factor: CropCoefficient | None
    p_yield_response_factor: CropCoefficient | None
    k_yield_response_factor: CropCoefficient | None

    n_uptake_fraction: Fraction | None
    p_uptake_fraction: Fraction | None
    k_uptake_fraction: Fraction | None

    @property
    def label(self) -> str:
        return self.category.value


@dataclass(frozen=True)
class CropProfile:
    id: CropProfileId
    crop: Crop
    system_name: SystemName

    planting_rate_kg_per_ha: YieldPerHa
    potential_yield_tons_per_ha: YieldPerHa

    stages: list[CropProfileStage]

    is_perennial: IsPerennial
    perennial_restart_stage: SequenceOrder | None
    max_lifespan_years: LifespanYears | None

    max_rooting_depth_m: Meter | None
    moisture_depletion_fraction: Fraction | None
    max_planting_depletion_mm: DepletionMm | None
    min_planting_depletion_mm: DepletionMm | None

    min_planting_n_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_n_kg_ha: Rate[Kilogram, AreaHa] | None
    min_planting_p_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_p_kg_ha: Rate[Kilogram, AreaHa] | None
    min_planting_k_kg_ha: Rate[Kilogram, AreaHa] | None
    max_planting_k_kg_ha: Rate[Kilogram, AreaHa] | None

    n_removal_kg_per_unit: Rate[Kilogram, YieldUnit] | None
    p_removal_kg_per_unit: Rate[Kilogram, YieldUnit] | None
    k_removal_kg_per_unit: Rate[Kilogram, YieldUnit] | None

    n_fixation_kg_per_unit: Rate[Kilogram, YieldUnit] | None
    max_n_fixation_fraction: Fraction | None

    planting_material_id: MaterialId
    product_id: MaterialId | None
