from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from functools import partial
from typing import Any, Protocol

import cashews
from sqlalchemy import Engine, text

from app.config import CachingConfig
from app.infrastructure.caching import Cache
from app.models.agronomy import Crop, CropProfile, CropProfileStage
from app.models.types import (
    GDD,
    VD,
    AreaHa,
    Celsius,
    CropCoefficient,
    CropId,
    CropName,
    CropProfileId,
    DepletionMm,
    Fraction,
    IsPerennial,
    Kilogram,
    LanguageCode,
    LifespanYears,
    MaterialId,
    Meter,
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

logger = logging.getLogger(__name__)


def _parse_names(raw_names: Any) -> dict[LanguageCode, CropName]:
    if not raw_names:
        return {}

    parsed: Any = raw_names
    if isinstance(raw_names, str):
        try:
            parsed = json.loads(raw_names)
        except (ValueError, TypeError):
            return {}

    if isinstance(parsed, dict):
        return {LanguageCode(str(k)): CropName(str(v)) for k, v in parsed.items()}

    return {}


def _parse_db_array(value: Any) -> list[str]:
    if not value:
        return []

    if isinstance(value, list | tuple | set):
        return [str(item) for item in value if item]

    if isinstance(value, str):
        cleaned = value.strip()
        if not cleaned or cleaned in ("{}", "[]"):
            return []

        if (cleaned.startswith("{") and cleaned.endswith("}")) or (
            cleaned.startswith("[") and cleaned.endswith("]")
        ):
            cleaned = cleaned[1:-1]

        return [item.strip().strip("\"'") for item in cleaned.split(",") if item.strip()]

    return []


def _parse_stages(stages_data: list[dict[str, Any]] | None) -> list[CropProfileStage]:
    if not stages_data:
        return []

    stages = []
    for s in stages_data:
        stages.append(
            CropProfileStage(
                sequence_order=SequenceOrder(s["sequence_order"]),
                category=StageCategory(s["category"]),
                target_accumulated_gdd=(
                    GDD(float(s["target_accumulated_gdd"]))
                    if s.get("target_accumulated_gdd") is not None
                    else None
                ),
                gdd_base_temp_c=(
                    Celsius(float(s["gdd_base_temp_c"]))
                    if s.get("gdd_base_temp_c") is not None
                    else None
                ),
                gdd_optimum_min_temp_c=(
                    Celsius(float(s["gdd_optimum_min_temp_c"]))
                    if s.get("gdd_optimum_min_temp_c") is not None
                    else None
                ),
                gdd_optimum_max_temp_c=(
                    Celsius(float(s["gdd_optimum_max_temp_c"]))
                    if s.get("gdd_optimum_max_temp_c") is not None
                    else None
                ),
                absolute_min_temp_c=(
                    Celsius(float(s["absolute_min_temp_c"]))
                    if s.get("absolute_min_temp_c") is not None
                    else None
                ),
                absolute_max_temp_c=(
                    Celsius(float(s["absolute_max_temp_c"]))
                    if s.get("absolute_max_temp_c") is not None
                    else None
                ),
                frost_shock_tolerance_days=(
                    int(s["frost_shock_tolerance_days"])
                    if s.get("frost_shock_tolerance_days") is not None
                    else None
                ),
                heat_shock_tolerance_days=(
                    int(s["heat_shock_tolerance_days"])
                    if s.get("heat_shock_tolerance_days") is not None
                    else None
                ),
                drought_shock_tolerance_days=(
                    int(s["drought_shock_tolerance_days"])
                    if s.get("drought_shock_tolerance_days") is not None
                    else None
                ),
                waterlogging_shock_tolerance_days=(
                    int(s["waterlogging_shock_tolerance_days"])
                    if s.get("waterlogging_shock_tolerance_days") is not None
                    else None
                ),
                min_days_since_planting=(
                    int(s["min_days_since_planting"])
                    if s.get("min_days_since_planting") is not None
                    else None
                ),
                max_days_since_planting=(
                    int(s["max_days_since_planting"])
                    if s.get("max_days_since_planting") is not None
                    else None
                ),
                target_accumulated_vd=(
                    VD(float(s["target_accumulated_vd"]))
                    if s.get("target_accumulated_vd") is not None
                    else None
                ),
                vernalization_absolute_min_temp_c=(
                    Celsius(float(s["vernalization_absolute_min_temp_c"]))
                    if s.get("vernalization_absolute_min_temp_c") is not None
                    else None
                ),
                vernalization_absolute_max_temp_c=(
                    Celsius(float(s["vernalization_absolute_max_temp_c"]))
                    if s.get("vernalization_absolute_max_temp_c") is not None
                    else None
                ),
                vernalization_optimal_min_temp_c=(
                    Celsius(float(s["vernalization_optimal_min_temp_c"]))
                    if s.get("vernalization_optimal_min_temp_c") is not None
                    else None
                ),
                vernalization_optimal_max_temp_c=(
                    Celsius(float(s["vernalization_optimal_max_temp_c"]))
                    if s.get("vernalization_optimal_max_temp_c") is not None
                    else None
                ),
                devernalization_start_temp_c=(
                    Celsius(float(s["devernalization_start_temp_c"]))
                    if s.get("devernalization_start_temp_c") is not None
                    else None
                ),
                devernalization_full_temp_c=(
                    Celsius(float(s["devernalization_full_temp_c"]))
                    if s.get("devernalization_full_temp_c") is not None
                    else None
                ),
                water_crop_coefficient=(
                    CropCoefficient(float(s["water_crop_coefficient"]))
                    if s.get("water_crop_coefficient") is not None
                    else None
                ),
                water_yield_response_factor=(
                    CropCoefficient(float(s["water_yield_response_factor"]))
                    if s.get("water_yield_response_factor") is not None
                    else None
                ),
                n_yield_response_factor=(
                    CropCoefficient(float(s["n_yield_response_factor"]))
                    if s.get("n_yield_response_factor") is not None
                    else None
                ),
                p_yield_response_factor=(
                    CropCoefficient(float(s["p_yield_response_factor"]))
                    if s.get("p_yield_response_factor") is not None
                    else None
                ),
                k_yield_response_factor=(
                    CropCoefficient(float(s["k_yield_response_factor"]))
                    if s.get("k_yield_response_factor") is not None
                    else None
                ),
                n_uptake_fraction=(
                    Fraction(float(s["n_uptake_fraction"]))
                    if s.get("n_uptake_fraction") is not None
                    else None
                ),
                p_uptake_fraction=(
                    Fraction(float(s["p_uptake_fraction"]))
                    if s.get("p_uptake_fraction") is not None
                    else None
                ),
                k_uptake_fraction=(
                    Fraction(float(s["k_uptake_fraction"]))
                    if s.get("k_uptake_fraction") is not None
                    else None
                ),
            ),
        )
    return stages


def _parse_rate_kg_unit(value: Any) -> Rate[Kilogram, YieldUnit] | None:
    if value is None:
        return None
    return Rate(real=float(value), target_type=Kilogram)


def _parse_rate_kg_ha(value: Any) -> Rate[Kilogram, AreaHa] | None:
    if value is None:
        return None
    return Rate(real=float(value), target_type=Kilogram)


def _map_row_to_profile(row: Any) -> CropProfile:
    raw_textures = _parse_db_array(row.allowed_soil_textures)
    allowed_soil_textures = {SoilTexture(t) for t in raw_textures} if raw_textures else None

    raw_drainage = _parse_db_array(row.allowed_drainage_classes)
    allowed_drainage_classes = (
        {SoilDrainageClass(d) for d in raw_drainage} if raw_drainage else None
    )

    crop = Crop(
        id=CropId(str(row.crop_id)),
        scientific_name=ScientificName(str(row.scientific_name)),
        allowed_soil_textures=allowed_soil_textures,
        allowed_drainage_classes=allowed_drainage_classes,
        ph_min=float(row.ph_min) if row.ph_min is not None else None,
        ph_max=float(row.ph_max) if row.ph_max is not None else None,
        salinity_threshold_ds_m=(
            float(row.salinity_threshold_ds_m)
            if getattr(row, "salinity_threshold_ds_m", None) is not None
            else None
        ),
        salinity_slope_pct=(
            float(row.salinity_slope_pct)
            if getattr(row, "salinity_slope_pct", None) is not None
            else None
        ),
        max_salinity_ds_m=(
            float(row.max_salinity_ds_m) if row.max_salinity_ds_m is not None else None
        ),
    )

    raw_max_planting_depletion = getattr(
        row,
        "max_planting_depletion_mm",
        getattr(row, "max_entry_depletion_mm", None),
    )
    raw_min_planting_depletion = getattr(row, "min_planting_depletion_mm", None)

    return CropProfile(
        id=CropProfileId(str(row.profile_id)),
        crop=crop,
        system_name=SystemName(str(row.system_name)),
        planting_rate_kg_per_ha=YieldPerHa(float(row.planting_rate_kg_per_ha)),
        potential_yield_tons_per_ha=YieldPerHa(float(row.potential_yield_tons_per_ha)),
        stages=_parse_stages(row.stages_json),
        is_perennial=IsPerennial(row.is_perennial),
        perennial_restart_stage=(
            SequenceOrder(int(row.perennial_restart_stage))
            if row.perennial_restart_stage is not None
            else None
        ),
        max_lifespan_years=(
            LifespanYears(int(row.max_lifespan_years))
            if row.max_lifespan_years is not None
            else None
        ),
        max_rooting_depth_m=(
            Meter(float(row.max_rooting_depth_m)) if row.max_rooting_depth_m is not None else None
        ),
        moisture_depletion_fraction=(
            Fraction(float(row.moisture_depletion_fraction))
            if row.moisture_depletion_fraction is not None
            else None
        ),
        max_planting_depletion_mm=(
            DepletionMm(int(raw_max_planting_depletion))
            if raw_max_planting_depletion is not None
            else None
        ),
        min_planting_depletion_mm=(
            DepletionMm(int(raw_min_planting_depletion))
            if raw_min_planting_depletion is not None
            else None
        ),
        min_planting_n_kg_ha=_parse_rate_kg_ha(getattr(row, "min_planting_n_kg_ha", None)),
        max_planting_n_kg_ha=_parse_rate_kg_ha(getattr(row, "max_planting_n_kg_ha", None)),
        min_planting_p_kg_ha=_parse_rate_kg_ha(getattr(row, "min_planting_p_kg_ha", None)),
        max_planting_p_kg_ha=_parse_rate_kg_ha(getattr(row, "max_planting_p_kg_ha", None)),
        min_planting_k_kg_ha=_parse_rate_kg_ha(getattr(row, "min_planting_k_kg_ha", None)),
        max_planting_k_kg_ha=_parse_rate_kg_ha(getattr(row, "max_planting_k_kg_ha", None)),
        n_removal_kg_per_unit=_parse_rate_kg_unit(row.n_removal_kg_per_unit),
        p_removal_kg_per_unit=_parse_rate_kg_unit(row.p_removal_kg_per_unit),
        k_removal_kg_per_unit=_parse_rate_kg_unit(row.k_removal_kg_per_unit),
        n_fixation_kg_per_unit=_parse_rate_kg_unit(row.n_fixation_kg_per_unit),
        max_n_fixation_fraction=(
            Fraction(float(row.max_n_fixation_fraction))
            if row.max_n_fixation_fraction is not None
            else None
        ),
        planting_material_id=MaterialId(str(row.planting_material_id)),
        product_id=MaterialId(str(row.product_id)) if row.product_id is not None else None,
    )


class CropProfileRepository(Protocol):
    async def get_all(self) -> list[CropProfile]: ...

    async def get_by_id(self, crop_profile_id: CropProfileId) -> CropProfile | None: ...


@dataclass(frozen=True)
class CropProfileRepositoryCacheGetAllKey: ...


class CropProfileRepositoryCacheGetAll(
    Cache[CropProfileRepositoryCacheGetAllKey, list[CropProfile]]
):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.crop_profile_repository.ttl
        self._enabled = config.crop_profile_repository.enabled


@dataclass(frozen=True)
class CropProfileRepositoryCacheGetByIdKey:
    crop_profile_id: CropProfileId


class CropProfileRepositoryCacheGetById(Cache[CropProfileRepositoryCacheGetByIdKey, CropProfile]):
    def __init__(self, cache: cashews.Cache, config: CachingConfig):
        super().__init__(cache, config)
        self._ttl = config.crop_profile_repository.ttl
        self._enabled = config.crop_profile_repository.enabled


class SqlCropProfileRepository(CropProfileRepository):
    _BASE_QUERY = """
                  SELECT
                      cp.id AS profile_id,
                      cp.system_name,
                      cp.planting_rate_kg_per_ha,
                      cp.potential_yield_tons_per_ha,
                      cp.is_perennial,
                      cp.planting_material_id,
                      cp.product_id,
                      cp.perennial_restart_stage,
                      cp.max_lifespan_years,

                      cp.max_rooting_depth_m,
                      cp.moisture_depletion_fraction,
                      cp.max_planting_depletion_mm,
                      cp.min_planting_depletion_mm,

                      cp.min_planting_n_kg_ha,
                      cp.max_planting_n_kg_ha,
                      cp.min_planting_p_kg_ha,
                      cp.max_planting_p_kg_ha,
                      cp.min_planting_k_kg_ha,
                      cp.max_planting_k_kg_ha,

                      cp.n_removal_kg_per_unit,
                      cp.p_removal_kg_per_unit,
                      cp.k_removal_kg_per_unit,

                      cp.n_fixation_kg_per_unit,
                      cp.max_n_fixation_fraction,

                      c.id AS crop_id,
                      c.scientific_name,
                      c.name AS raw_names,
                      c.allowed_soil_textures,
                      c.allowed_drainage_classes,
                      c.ph_min,
                      c.ph_max,
                      c.salinity_threshold_ds_m,
                      c.salinity_slope_pct,
                      c.max_salinity_ds_m,

                      (
                          SELECT jsonb_agg(jsonb_build_object(
                                  'sequence_order', cps.sequence_order,
                                  'name', cps.name,
                                  'target_accumulated_gdd', cps.target_accumulated_gdd,
                                  'category', cps.category,
                                  'gdd_base_temp_c', cps.gdd_base_temp_c,
                                  'absolute_min_temp_c', cps.absolute_min_temp_c,
                                  'absolute_max_temp_c', cps.absolute_max_temp_c,
                                  'gdd_optimum_min_temp_c', cps.gdd_optimum_min_temp_c,
                                  'gdd_optimum_max_temp_c', cps.gdd_optimum_max_temp_c,
                                  'frost_shock_tolerance_days', cps.frost_shock_tolerance_days,
                                  'heat_shock_tolerance_days', cps.heat_shock_tolerance_days,
                                  'drought_shock_tolerance_days', cps.drought_shock_tolerance_days,
                                  'waterlogging_shock_tolerance_days', cps.waterlogging_shock_tolerance_days,
                                  'target_accumulated_vd', cps.target_accumulated_vd,
                                  'vernalization_absolute_min_temp_c', cps.vernalization_absolute_min_temp_c,
                                  'vernalization_absolute_max_temp_c', cps.vernalization_absolute_max_temp_c,
                                  'vernalization_optimal_min_temp_c', cps.vernalization_optimal_min_temp_c,
                                  'vernalization_optimal_max_temp_c', cps.vernalization_optimal_max_temp_c,
                                  'min_days_since_planting', cps.min_days_since_planting,
                                  'max_days_since_planting', cps.max_days_since_planting,
                                  'devernalization_start_temp_c', cps.devernalization_start_temp_c,
                                  'devernalization_full_temp_c', cps.devernalization_full_temp_c,
                                  'water_crop_coefficient', cps.water_crop_coefficient,
                                  'water_yield_response_factor', cps.water_yield_response_factor,
                                  'n_yield_response_factor', cps.n_yield_response_factor,
                                  'p_yield_response_factor', cps.p_yield_response_factor,
                                  'k_yield_response_factor', cps.k_yield_response_factor,
                                  'n_uptake_fraction', cps.n_uptake_fraction,
                                  'p_uptake_fraction', cps.p_uptake_fraction,
                                  'k_uptake_fraction', cps.k_uptake_fraction
                                           ) ORDER BY cps.sequence_order)
                          FROM crop_profile_stages cps
                          WHERE cps.crop_profile_id = cp.id
                      ) AS stages_json
                  FROM crop_profiles cp
                           JOIN crops c ON cp.crop_id = c.id
                  """

    def __init__(
        self,
        engine: Engine,
        cache_get_all: CropProfileRepositoryCacheGetAll,
        cache_get_by_id: CropProfileRepositoryCacheGetById,
    ) -> None:
        self._engine = engine
        self._cache_get_all = cache_get_all
        self._cache_get_by_id = cache_get_by_id

    async def _get_all_from_db(self) -> list[CropProfile]:
        query = text(f"{self._BASE_QUERY} ORDER BY cp.id;")

        with self._engine.connect() as conn:
            query_result = conn.execute(query)
            return [_map_row_to_profile(row) for row in query_result]

    async def get_all(self) -> list[CropProfile]:
        result = await self._cache_get_all.using(
            CropProfileRepositoryCacheGetAllKey(), self._get_all_from_db
        )
        return result if result is not None else []

    async def _get_by_id_from_db(self, crop_profile_id: CropProfileId) -> CropProfile | None:
        try:
            db_id = int(crop_profile_id)
        except ValueError:
            return None

        query = text(f"{self._BASE_QUERY} WHERE cp.id = :profile_id;")

        with self._engine.connect() as conn:
            row = conn.execute(query, {"profile_id": db_id}).first()
            result = _map_row_to_profile(row) if row is not None else None
            return result

    async def get_by_id(self, crop_profile_id: CropProfileId) -> CropProfile | None:
        return await self._cache_get_by_id.using(
            CropProfileRepositoryCacheGetByIdKey(crop_profile_id),
            partial(self._get_by_id_from_db, crop_profile_id),
        )
